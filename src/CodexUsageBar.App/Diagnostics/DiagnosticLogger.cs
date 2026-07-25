using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CodexUsageBar.Core.Time;

namespace CodexUsageBar.App.Diagnostics;

public sealed record PlacementDiagnostic(double Left, double Top, double Width, double Height);

public sealed record DiagnosticEvent(
    string EventCode,
    string StatusCategory,
    int RetrySeconds,
    string CodexVersion,
    PlacementDiagnostic? Placement);

public interface IDiagnosticLogger : IDisposable
{
    void Write(DiagnosticEvent diagnosticEvent, Exception? exception = null);
}

public sealed class DiagnosticLogger : IDiagnosticLogger
{
    private const int RetentionDays = 7;
    private readonly object _sync = new();
    private readonly string _logDirectory;
    private readonly IClock _clock;
    private bool _isDisposed;

    public DiagnosticLogger(IClock clock)
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexUsageBar",
                "logs"),
            clock)
    {
    }

    internal DiagnosticLogger(string logDirectory, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logDirectory = Path.GetFullPath(logDirectory);
        TryCreateAndPrune();
    }

    public void Write(DiagnosticEvent diagnosticEvent, Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        _ = exception;

        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_logDirectory);
                var timestamp = _clock.Now;
                var path = Path.Combine(_logDirectory, $"{timestamp:yyyy-MM-dd}.log");
                File.AppendAllText(
                    path,
                    Serialize(timestamp, diagnosticEvent) + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException)
            {
                // Diagnostics must never interrupt quota refresh or shutdown.
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _isDisposed = true;
        }
    }

    private static string Serialize(DateTimeOffset timestamp, DiagnosticEvent diagnosticEvent)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("timestamp", timestamp.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("eventCode", diagnosticEvent.EventCode);
            writer.WriteString("statusCategory", diagnosticEvent.StatusCategory);
            writer.WriteNumber("retrySeconds", diagnosticEvent.RetrySeconds);
            writer.WriteString("codexVersion", diagnosticEvent.CodexVersion);
            if (diagnosticEvent.Placement is { } placement)
            {
                writer.WriteStartObject("placement");
                writer.WriteNumber("left", placement.Left);
                writer.WriteNumber("top", placement.Top);
                writer.WriteNumber("width", placement.Width);
                writer.WriteNumber("height", placement.Height);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void TryCreateAndPrune()
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var cutoff = _clock.Now.UtcDateTime.Date.AddDays(-RetentionDays);
            foreach (var path in Directory.EnumerateFiles(_logDirectory, "*.log", SearchOption.TopDirectoryOnly))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception startupFailure) when (startupFailure is IOException or UnauthorizedAccessException)
        {
            // Logging is optional; startup remains quiet if the directory is unavailable.
        }
    }
}

internal sealed class NullDiagnosticLogger : IDiagnosticLogger
{
    public static NullDiagnosticLogger Instance { get; } = new();

    public void Write(DiagnosticEvent diagnosticEvent, Exception? exception = null)
    {
    }

    public void Dispose()
    {
    }
}
