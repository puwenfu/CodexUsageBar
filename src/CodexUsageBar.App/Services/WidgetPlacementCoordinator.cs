using System.Windows.Threading;
using CodexUsageBar.App.Diagnostics;
using CodexUsageBar.Windows.Codex;
using CodexUsageBar.Windows.Geometry;
using CodexUsageBar.Windows.Taskbar;
using CodexUsageBar.Windows.Tray;

namespace CodexUsageBar.App.Services;

internal sealed class WidgetPlacementCoordinator : IDisposable
{
    internal const double FullTaskbarWidthDip = 160d;
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromMilliseconds(750);
    private readonly WidgetWindow _window;
    private readonly IWidgetPreferences _preferences;
    private readonly IDiagnosticLogger _logger;
    private readonly TaskbarAvailablePlacementFinder _taskbarFinder;
    private readonly CodexSidebarPlacementFinder _codexFinder;
    private readonly TaskbarWindowHost _taskbarHost;
    private readonly CodexSidebarWindowHost _codexHost;
    private readonly SystemTrayIconHost _trayHost;
    private readonly DispatcherTimer _timer;
    private WidgetSurface? _currentSurface;
    private TaskbarPlacement? _currentTaskbarPlacement;
    private CodexSidebarPlacement? _currentCodexPlacement;
    private HorizontalOffsetRange? _currentCodexHorizontalOffsetRange;
    private double _appliedCodexHorizontalOffsetDip;
    private string? _lastCodexUnavailableReason;
    private string? _lastCodexActivationFailure;
    private bool _contextMenuOpen;
    private bool _isDisposed;

    internal WidgetPlacementCoordinator(
        WidgetWindow window,
        IWidgetPreferences preferences,
        IDiagnosticLogger logger)
        : this(
            window,
            preferences,
            logger,
            new TaskbarAvailablePlacementFinder(),
            new CodexSidebarPlacementFinder(),
            new TaskbarWindowHost(),
            new CodexSidebarWindowHost())
    {
    }

    internal WidgetPlacementCoordinator(
        WidgetWindow window,
        IWidgetPreferences preferences,
        IDiagnosticLogger logger,
        TaskbarAvailablePlacementFinder taskbarFinder,
        CodexSidebarPlacementFinder codexFinder,
        TaskbarWindowHost taskbarHost,
        CodexSidebarWindowHost codexHost)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _taskbarFinder = taskbarFinder ?? throw new ArgumentNullException(nameof(taskbarFinder));
        _codexFinder = codexFinder ?? throw new ArgumentNullException(nameof(codexFinder));
        _taskbarHost = taskbarHost ?? throw new ArgumentNullException(nameof(taskbarHost));
        _codexHost = codexHost ?? throw new ArgumentNullException(nameof(codexHost));

        // Prime the transparent WPF surface before later hosts reparent the shared HWND.
        _taskbarHost.Attach(window);
        _codexHost.Attach(window);
        _taskbarHost.WindowLost += OnTaskbarWindowLost;
        _window.ContextMenuActivityChanged += OnContextMenuActivityChanged;
        _window.PlacementPreferenceChanged += OnPlacementPreferenceChanged;
        _window.HorizontalOffsetsChanged += OnHorizontalOffsetsChanged;
        _trayHost = new SystemTrayIconHost(window, window.OpenContextMenuAt);
        _timer = new DispatcherTimer(
            EvaluationInterval,
            DispatcherPriority.Background,
            OnEvaluationTick,
            window.Dispatcher);
        _timer.Start();
        Evaluate();
    }

    internal event EventHandler? WindowLost;

    internal WidgetSurface? CurrentSurface => _currentSurface;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Stop();
        _taskbarHost.WindowLost -= OnTaskbarWindowLost;
        _window.ContextMenuActivityChanged -= OnContextMenuActivityChanged;
        _window.PlacementPreferenceChanged -= OnPlacementPreferenceChanged;
        _window.HorizontalOffsetsChanged -= OnHorizontalOffsetsChanged;
        _trayHost.Dispose();
        _taskbarHost.Dispose();
        _codexHost.Dispose();
    }

    internal void Evaluate(bool allowWhileContextMenuOpen = false)
    {
        if (_isDisposed || (_contextMenuOpen && !allowWhileContextMenuOpen))
        {
            return;
        }

        EnsureTrayIconVisible();
        var hasFullTaskbar = _taskbarFinder.TryFind(
            FullTaskbarWidthDip,
            _preferences.TaskbarHorizontalOffsetDip,
            out var fullTaskbarPlacement,
            out var taskbarHorizontalOffsetRange);
        var shouldProbeCodexSidebar = _preferences.PlacementPreference is
            WidgetPlacementPreference.Automatic or
            WidgetPlacementPreference.CodexSidebarPreferred;
        CodexSidebarPlacement codexPlacement = null!;
        var codexHorizontalOffsetRange = default(HorizontalOffsetRange);
        var hasCodexSidebar = shouldProbeCodexSidebar &&
            _codexFinder.TryFind(
                _preferences.CodexSidebarHorizontalOffsetDip,
                out codexPlacement,
                out codexHorizontalOffsetRange);
        if (hasCodexSidebar)
        {
            _currentCodexHorizontalOffsetRange = codexHorizontalOffsetRange;
        }
        _window.ApplyHorizontalOffsetRanges(
            hasFullTaskbar ? taskbarHorizontalOffsetRange : null,
            hasCodexSidebar ? codexHorizontalOffsetRange : null);
        if (!hasFullTaskbar &&
            shouldProbeCodexSidebar &&
            !hasCodexSidebar &&
            !string.Equals(
                _lastCodexUnavailableReason,
                _codexFinder.LastFailureReason,
                StringComparison.Ordinal))
        {
            _lastCodexUnavailableReason = _codexFinder.LastFailureReason;
            _logger.Write(
                new DiagnosticEvent(
                    "placement.codex_unavailable",
                    _lastCodexUnavailableReason,
                    0,
                    string.Empty,
                    Placement: null));
        }
        else if (hasFullTaskbar || hasCodexSidebar)
        {
            _lastCodexUnavailableReason = null;
        }

        var availability = new WidgetPlacementAvailability(
            hasFullTaskbar,
            hasCodexSidebar);
        var resolved = WidgetPlacementPolicy.Resolve(
            _preferences.PlacementPreference,
            availability);
        if (TryApply(
                resolved,
                fullTaskbarPlacement,
                codexPlacement))
        {
            return;
        }

        var fallback = WidgetPlacementPolicy.Resolve(
            _preferences.PlacementPreference,
            availability with
            {
                TaskbarFull = resolved != WidgetSurface.TaskbarFull &&
                    availability.TaskbarFull,
                CodexSidebar = resolved != WidgetSurface.CodexSidebar &&
                    availability.CodexSidebar,
            });
        if (!TryApply(
                fallback,
                fullTaskbarPlacement,
                codexPlacement))
        {
            if (!ApplySystemTray())
            {
                _ = ApplyEmergencyTaskbar();
            }
        }
    }

    private bool TryApply(
        WidgetSurface surface,
        TaskbarPlacement fullTaskbarPlacement,
        CodexSidebarPlacement codexPlacement) =>
        surface switch
        {
            WidgetSurface.TaskbarFull => ApplyTaskbar(
                WidgetSurface.TaskbarFull,
                fullTaskbarPlacement),
            WidgetSurface.CodexSidebar => ApplyCodexSidebar(codexPlacement),
            WidgetSurface.SystemTray => ApplySystemTray(),
            _ => false,
        };

    private bool ApplyTaskbar(WidgetSurface surface, TaskbarPlacement placement)
    {
        if (_currentSurface == surface && _currentTaskbarPlacement == placement)
        {
            return true;
        }

        _codexHost.Deactivate();
        _window.ApplyPlacementLayout(
            placement.WidthDip,
            useTaskbarOpticalAlignment: true);
        if (!_taskbarHost.Activate(placement))
        {
            return false;
        }

        _currentSurface = surface;
        _currentTaskbarPlacement = placement;
        _currentCodexPlacement = null;
        LogPlacement(
            surface,
            new PlacementDiagnostic(
                placement.LeftPhysicalPixel,
                placement.TopPhysicalPixel,
                placement.RightPhysicalPixel - placement.LeftPhysicalPixel,
                placement.BottomPhysicalPixel - placement.TopPhysicalPixel));
        return true;
    }

    private bool ApplyCodexSidebar(
        CodexSidebarPlacement placement,
        bool logPlacement = true,
        bool isInteractiveRelocation = false)
    {
        if (_currentSurface == WidgetSurface.CodexSidebar &&
            _currentCodexPlacement == placement)
        {
            return _codexHost.Activate(placement);
        }

        if (!isInteractiveRelocation)
        {
            _taskbarHost.Deactivate();
            _window.ApplyPlacementLayout(
                placement.WidthDip,
                useTaskbarOpticalAlignment: false);
        }
        if (!_codexHost.Activate(placement))
        {
            if (!string.Equals(
                    _lastCodexActivationFailure,
                    _codexHost.LastActivationFailure,
                    StringComparison.Ordinal))
            {
                _lastCodexActivationFailure = _codexHost.LastActivationFailure;
                _logger.Write(
                    new DiagnosticEvent(
                        "placement.codex_failed",
                        _lastCodexActivationFailure,
                        0,
                        string.Empty,
                        Placement: null));
            }
            return false;
        }

        _lastCodexActivationFailure = null;
        _currentSurface = WidgetSurface.CodexSidebar;
        _currentTaskbarPlacement = null;
        _currentCodexPlacement = placement;
        _appliedCodexHorizontalOffsetDip =
            _currentCodexHorizontalOffsetRange?.Clamp(
                _preferences.CodexSidebarHorizontalOffsetDip) ??
            _preferences.CodexSidebarHorizontalOffsetDip;
        if (logPlacement)
        {
            LogPlacement(
                WidgetSurface.CodexSidebar,
                new PlacementDiagnostic(
                    placement.Bounds.Left,
                    placement.Bounds.Top,
                    placement.Bounds.Width,
                    placement.Bounds.Height));
        }
        return true;
    }

    private bool ApplySystemTray()
    {
        if (_currentSurface == WidgetSurface.SystemTray &&
            _trayHost.IsVisible)
        {
            return true;
        }

        if (!_trayHost.SetVisible(true))
        {
            return false;
        }

        _taskbarHost.Deactivate();
        _codexHost.Deactivate();
        _currentSurface = WidgetSurface.SystemTray;
        _currentTaskbarPlacement = null;
        _currentCodexPlacement = null;
        LogPlacement(WidgetSurface.SystemTray, placement: null);
        return true;
    }

    private bool ApplyEmergencyTaskbar()
    {
        _codexHost.Deactivate();
        _window.ApplyPlacementLayout(
            FullTaskbarWidthDip,
            useTaskbarOpticalAlignment: true);
        if (!_taskbarHost.ActivateDefault())
        {
            return false;
        }

        _currentSurface = WidgetSurface.TaskbarFull;
        _currentTaskbarPlacement = null;
        _currentCodexPlacement = null;
        LogPlacement(WidgetSurface.TaskbarFull, placement: null);
        return true;
    }

    private void OnEvaluationTick(object? sender, EventArgs eventArgs) => Evaluate();

    private void OnPlacementPreferenceChanged(object? sender, EventArgs eventArgs) => Evaluate();

    private void OnHorizontalOffsetsChanged(object? sender, EventArgs eventArgs)
    {
        if (!TryApplyCurrentCodexHorizontalOffset())
        {
            Evaluate(allowWhileContextMenuOpen: true);
        }
    }

    private void OnContextMenuActivityChanged(object? sender, bool isOpen)
    {
        _contextMenuOpen = isOpen;
        _trayHost.NotifyContextMenuActivity(isOpen);
        if (isOpen)
        {
            RefreshHorizontalOffsetRanges();
        }
        else
        {
            Evaluate();
        }
    }

    private void RefreshHorizontalOffsetRanges()
    {
        var hasTaskbar = _taskbarFinder.TryFind(
            FullTaskbarWidthDip,
            _preferences.TaskbarHorizontalOffsetDip,
            out _,
            out var taskbarRange);
        var hasCodexSidebar = _codexFinder.TryFind(
            _preferences.CodexSidebarHorizontalOffsetDip,
            out _,
            out var codexRange);
        _window.ApplyHorizontalOffsetRanges(
            hasTaskbar ? taskbarRange : null,
            hasCodexSidebar ? codexRange : null);
    }

    private void EnsureTrayIconVisible()
    {
        if (!_trayHost.IsVisible)
        {
            _ = _trayHost.SetVisible(true);
        }
    }

    private bool TryApplyCurrentCodexHorizontalOffset()
    {
        if (_currentSurface != WidgetSurface.CodexSidebar ||
            _currentCodexPlacement is not { } placement ||
            _currentCodexHorizontalOffsetRange is not { } range)
        {
            return false;
        }

        var requestedOffsetDip = range.Clamp(
            _preferences.CodexSidebarHorizontalOffsetDip);
        var deltaDip = requestedOffsetDip - _appliedCodexHorizontalOffsetDip;
        if (Math.Abs(deltaDip) < double.Epsilon)
        {
            return true;
        }

        var scale = placement.Bounds.Width / placement.WidthDip;
        if (!double.IsFinite(scale) || scale <= 0d)
        {
            return false;
        }

        var deltaPhysicalPixels = checked((int)Math.Round(deltaDip * scale));
        if (deltaPhysicalPixels == 0)
        {
            return true;
        }

        var bounds = placement.Bounds;
        var relocatedPlacement = placement with
        {
            Bounds = new PhysicalRect(
                bounds.Left + deltaPhysicalPixels,
                bounds.Top,
                bounds.Right + deltaPhysicalPixels,
                bounds.Bottom),
        };
        return ApplyCodexSidebar(
            relocatedPlacement,
            logPlacement: false,
            isInteractiveRelocation: true);
    }

    private void OnTaskbarWindowLost(object? sender, EventArgs eventArgs)
    {
        if (_currentSurface == WidgetSurface.TaskbarFull)
        {
            WindowLost?.Invoke(this, EventArgs.Empty);
        }
    }

    private void LogPlacement(
        WidgetSurface surface,
        PlacementDiagnostic? placement) =>
        _logger.Write(
            new DiagnosticEvent(
                "placement.changed",
                surface.ToString(),
                0,
                string.Empty,
                placement));
}
