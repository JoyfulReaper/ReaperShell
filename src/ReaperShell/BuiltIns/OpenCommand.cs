using System.Diagnostics;
using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

public sealed class OpenCommand : IShellCommand
{
    public string Name => "open";

    public string Description => "Opens a file, directory, or URL with the OS default handler.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: open <path-or-url>");
            return Task.FromResult(1);
        }

        var targetResult = ResolveTarget(context, args[0]);
        if (!targetResult.IsUrl && !File.Exists(targetResult.Target) && !Directory.Exists(targetResult.Target))
        {
            context.WriteErrorLine($"Local path does not exist: {targetResult.Target}");
            return Task.FromResult(1);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = targetResult.Target,
                UseShellExecute = true,
                WorkingDirectory = context.WorkingDirectory.FullName
            };

            Process.Start(startInfo);
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Failed to open '{targetResult.Target}': {ex.Message}");
            return Task.FromResult(1);
        }
    }

    private static ResolvedTarget ResolveTarget(ShellContext context, string input)
    {
        if (TryResolveHttpOrHttpsUrl(input, out var url))
        {
            return new ResolvedTarget(url, true);
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedTarget(uri.LocalPath, false);
        }

        return new ResolvedTarget(Path.GetFullPath(input, context.WorkingDirectory.FullName), false);
    }

    private static bool TryResolveHttpOrHttpsUrl(string input, out string url)
    {
        url = input;
        return Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private sealed record ResolvedTarget(string Target, bool IsUrl);
}
