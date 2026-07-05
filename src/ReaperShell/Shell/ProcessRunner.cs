using System.Diagnostics;
using System.Text;

namespace ReaperShell.Shell;

public sealed class ProcessRunner
{
    private readonly ShellSessionState _sessionState;

    public ProcessRunner(ShellSessionState? sessionState = null)
    {
        _sessionState = sessionState ?? new ShellSessionState();
    }

    public void StartDetached(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        ApplySessionEnvironment(startInfo);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Failed to start process '{executable}'.");
        }

        process.Dispose();
    }

    public async Task<ProcessRunResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onStandardOutput = null,
        Action<string>? onStandardError = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        ApplySessionEnvironment(startInfo);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environmentVariables is not null)
        {
            foreach (var pair in environmentVariables)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            standardOutput.AppendLine(eventArgs.Data);
            onStandardOutput?.Invoke(eventArgs.Data);
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            standardError.AppendLine(eventArgs.Data);
            onStandardError?.Invoke(eventArgs.Data);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{executable}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            throw;
        }

        return new ProcessRunResult(
            process.ExitCode,
            standardOutput.ToString(),
            standardError.ToString());
    }

    private void ApplySessionEnvironment(ProcessStartInfo startInfo)
    {
        foreach (var pair in _sessionState.GetEnvironmentVariables())
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }
    }
}

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);
