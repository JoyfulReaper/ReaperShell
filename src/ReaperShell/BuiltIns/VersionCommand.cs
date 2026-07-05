using System.Reflection;
using System.Runtime.InteropServices;
using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class VersionCommand : IShellCommand
{
    public string Name => "version";

    public string Description => "Prints ReaperShell version and runtime information.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count != 0)
        {
            context.WriteErrorLine("Usage: version");
            return Task.FromResult(1);
        }

        var entryAssembly = Assembly.GetEntryAssembly() ?? typeof(VersionCommand).Assembly;
        var informationalVersion = entryAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var assemblyVersion = entryAssembly.GetName().Version?.ToString();
        var versionText = informationalVersion ?? assemblyVersion ?? "unknown";

        context.WriteLine($"REAPER SHELL VERSION: {versionText}");
        context.WriteLine($".NET RUNTIME: {Environment.Version}");
        context.WriteLine($"OS DESCRIPTION: {RuntimeInformation.OSDescription}");
        context.WriteLine($"PROCESS ARCHITECTURE: {RuntimeInformation.ProcessArchitecture}");
        context.WriteLine($"PROCESS ID: {Environment.ProcessId}");
        return Task.FromResult(0);
    }
}
