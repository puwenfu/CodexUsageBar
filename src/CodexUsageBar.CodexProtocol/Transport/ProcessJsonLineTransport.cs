using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace CodexUsageBar.CodexProtocol.Transport;

internal sealed class ProcessJsonLineTransport : IJsonLineTransport
{
    [ThreadStatic]
    private static ProcessJsonLineTransport? _raisingExitFor;

    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    private readonly Process _process;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _disposeSync = new();
    private readonly Task _stderrDrainTask;
    private Task? _disposeTask;
    private int _disposed;

    private ProcessJsonLineTransport(Process process)
    {
        _process = process;
        _process.EnableRaisingEvents = true;
        _process.Exited += OnProcessExited;
        _stderrDrainTask = DrainStandardErrorAsync();
    }

    public event EventHandler<int?>? Exited;

    public static ProcessJsonLineTransport Start(AppServerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolvedCodexPath = OperatingSystem.IsWindows()
            && command.FileName.Equals("codex", StringComparison.OrdinalIgnoreCase)
                ? TryResolveRunningCodexExecutable()
                : null;
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Utf8WithoutBom,
            StandardOutputEncoding = Utf8WithoutBom,
            StandardErrorEncoding = Utf8WithoutBom,
        };

        // On Windows, Process.Start with UseShellExecute=false cannot resolve
        // .cmd/.bat shim scripts (e.g. npm's codex.cmd). Route through cmd.exe
        // so that PATHEXT resolution works correctly.
        if (OperatingSystem.IsWindows() && NeedsCmdWrapper(command.FileName))
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command.FileName);
            if (resolvedCodexPath is not null)
            {
                var currentPath = startInfo.Environment["PATH"] ?? string.Empty;
                startInfo.Environment["PATH"] =
                    Path.GetDirectoryName(resolvedCodexPath)
                    + Path.PathSeparator
                    + currentPath;
            }
        }
        else
        {
            startInfo.FileName = command.FileName;
        }

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            return new ProcessJsonLineTransport(process);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 2 or 3)
        {
            process.Dispose();
            throw new CodexCommandNotFoundException(command.FileName);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static string? TryResolveRunningCodexExecutable()
    {
        var paths = new List<string?>();
        foreach (var process in Process.GetProcessesByName("codex"))
        {
            using (process)
            {
                try
                {
                    paths.Add(process.MainModule?.FileName);
                }
                catch (Exception exception) when (exception is
                    Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Some system-owned processes do not expose their executable path.
                }
            }
        }

        return ResolveCurrentCodexExecutable(paths, File.Exists);
    }

    internal static string? ResolveCurrentCodexExecutable(
        IEnumerable<string?> runningExecutablePaths,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(runningExecutablePaths);
        ArgumentNullException.ThrowIfNull(fileExists);

        return runningExecutablePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .FirstOrDefault(path =>
                path.Contains(
                    $"{Path.DirectorySeparatorChar}AppData{Path.DirectorySeparatorChar}Local{Path.DirectorySeparatorChar}OpenAI{Path.DirectorySeparatorChar}Codex{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(
                    $"{Path.DirectorySeparatorChar}codex.exe",
                    StringComparison.OrdinalIgnoreCase)
                && fileExists(path));
    }

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);

        while (await _process.StandardOutput.ReadLineAsync(linkedCancellation.Token) is { } line)
        {
            yield return line;
        }
    }

    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(line);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        await _writeLock.WaitAsync(linkedCancellation.Token);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_process.HasExited)
            {
                throw new CodexProcessExitedException(TryGetExitCode());
            }

            await _process.StandardInput.WriteLineAsync(line.AsMemory(), linkedCancellation.Token);
            await _process.StandardInput.FlushAsync(linkedCancellation.Token);
        }
        catch (IOException) when (_process.HasExited)
        {
            throw new CodexProcessExitedException(TryGetExitCode());
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (ReferenceEquals(_raisingExitFor, this))
        {
            return new ValueTask(Task.FromException(new InvalidOperationException(
                "The process transport cannot be disposed synchronously from its exit handler.")));
        }

        TaskCompletionSource? starter = null;
        Task disposalTask;
        lock (_disposeSync)
        {
            if (_disposeTask is null)
            {
                Volatile.Write(ref _disposed, 1);
                starter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = starter.Task;
            }

            disposalTask = _disposeTask;
        }

        if (starter is not null)
        {
            _ = CompleteDisposeAsync(starter);
        }

        return new ValueTask(disposalTask);
    }

    private async Task DisposeCoreAsync()
    {
        _lifetimeCancellation.Cancel();
        await _writeLock.WaitAsync();
        try
        {
            _process.StandardInput.Close();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            await _process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            // The process has already ended and released its native handle.
        }
        finally
        {
            _writeLock.Release();
            try
            {
                await _stderrDrainTask;
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                // Disposal deliberately cancels the otherwise unbounded stderr drain.
            }
            catch (IOException)
            {
                // stderr is intentionally discarded; process shutdown can close the pipe first.
            }

            _process.Exited -= OnProcessExited;
            _process.Dispose();
            _writeLock.Dispose();
            _lifetimeCancellation.Dispose();
        }
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            await Task.Yield();
            await DisposeCoreAsync();
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private async Task DrainStandardErrorAsync()
    {
        var buffer = new char[1024];
        while (await _process.StandardError.ReadAsync(buffer.AsMemory(), _lifetimeCancellation.Token) > 0)
        {
        }
    }

    private void OnProcessExited(object? sender, EventArgs eventArgs)
    {
        var exitCode = TryGetExitCode();
        var previousTransport = _raisingExitFor;
        _raisingExitFor = this;
        try
        {
            foreach (EventHandler<int?> handler in Exited?.GetInvocationList() ?? [])
            {
                try
                {
                    handler(this, exitCode);
                }
                catch
                {
                    // A subscriber cannot interfere with process cleanup or other subscribers.
                }
            }
        }
        finally
        {
            _raisingExitFor = previousTransport;
        }
    }

    private int? TryGetExitCode()
    {
        try
        {
            return _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool NeedsCmdWrapper(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(extension) && fileName.Equals("codex", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }
}
