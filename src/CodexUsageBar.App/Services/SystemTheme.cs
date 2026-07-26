using System.IO;
using System.Security;
using System.Windows;
using Microsoft.Win32;

namespace CodexUsageBar.App.Services;

internal enum SystemTheme
{
    Dark,
    Light,
}

internal sealed class SystemThemeChangedEventArgs(SystemTheme theme) : EventArgs
{
    public SystemTheme Theme { get; } = theme;
}

internal interface ISystemThemeWatcher : IDisposable
{
    SystemTheme CurrentTheme { get; }

    event EventHandler<SystemThemeChangedEventArgs>? ThemeChanged;
}

internal sealed class WindowsSystemThemeWatcher : ISystemThemeWatcher
{
    private const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string SystemUsesLightThemeValueName = "SystemUsesLightTheme";
    private const string AppsUseLightThemeValueName = "AppsUseLightTheme";
    private readonly object _sync = new();
    private bool _subscribed;
    private bool _disposed;
    private SystemTheme _currentTheme;

    public WindowsSystemThemeWatcher()
    {
        _currentTheme = ReadCurrentTheme();
        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _subscribed = true;
        }
        catch (InvalidOperationException)
        {
            // Theme detection still works at startup when system event delivery is unavailable.
        }
    }

    public SystemTheme CurrentTheme
    {
        get
        {
            lock (_sync)
            {
                return _currentTheme;
            }
        }
    }

    public event EventHandler<SystemThemeChangedEventArgs>? ThemeChanged;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (_subscribed)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _subscribed = false;
        }
    }

    internal static SystemTheme ResolveTheme(
        object? systemUsesLightTheme,
        object? appsUseLightTheme) =>
        TryReadBoolean(systemUsesLightTheme)
        ?? TryReadBoolean(appsUseLightTheme)
        ?? false
            ? SystemTheme.Light
            : SystemTheme.Dark;

    private static SystemTheme ReadCurrentTheme()
    {
        try
        {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            return ResolveTheme(
                personalizeKey?.GetValue(SystemUsesLightThemeValueName),
                personalizeKey?.GetValue(AppsUseLightThemeValueName));
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            return SystemTheme.Dark;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs eventArgs)
    {
        var theme = ReadCurrentTheme();
        lock (_sync)
        {
            if (_disposed || _currentTheme == theme)
            {
                return;
            }

            _currentTheme = theme;
        }

        ThemeChanged?.Invoke(this, new SystemThemeChangedEventArgs(theme));
    }

    private static bool? TryReadBoolean(object? value) =>
        value switch
        {
            int number => number != 0,
            uint number => number != 0,
            long number => number != 0,
            byte number => number != 0,
            string text when int.TryParse(text, out var number) => number != 0,
            _ => null,
        };
}

internal sealed class SessionSystemThemeWatcher(SystemTheme currentTheme) : ISystemThemeWatcher
{
    public SystemTheme CurrentTheme { get; private set; } = currentTheme;

    public event EventHandler<SystemThemeChangedEventArgs>? ThemeChanged;

    public void SetTheme(SystemTheme theme)
    {
        if (CurrentTheme == theme)
        {
            return;
        }

        CurrentTheme = theme;
        ThemeChanged?.Invoke(this, new SystemThemeChangedEventArgs(theme));
    }

    public void Dispose()
    {
    }
}

internal static class SystemThemeResources
{
    private const string ThemeDictionaryMarker = "/Themes/SystemTheme";

    public static void Replace(ResourceDictionary resources, SystemTheme theme)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var merged = resources.MergedDictionaries;
        var insertionIndex = 0;
        var foundTheme = false;

        for (var index = merged.Count - 1; index >= 0; index--)
        {
            if (merged[index].Source?.ToString().Contains(
                    ThemeDictionaryMarker,
                    StringComparison.OrdinalIgnoreCase) != true)
            {
                continue;
            }

            insertionIndex = index;
            foundTheme = true;
            merged.RemoveAt(index);
        }

        if (!foundTheme)
        {
            insertionIndex = 0;
        }

        var dictionary = new ResourceDictionary
        {
            Source = new Uri(
                $"/CodexUsageBar.App;component/Themes/SystemTheme{theme}.xaml",
                UriKind.Relative),
        };
        merged.Insert(Math.Min(insertionIndex, merged.Count), dictionary);
    }
}
