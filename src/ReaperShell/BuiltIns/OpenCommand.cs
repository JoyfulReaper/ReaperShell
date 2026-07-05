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

        var target = ResolveTarget(context, args[0]);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
                WorkingDirectory = context.WorkingDirectory.FullName
            };

            Process.Start(startInfo);
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Failed to open '{target}': {ex.Message}");
            return Task.FromResult(1);
        }
    }

    private static string ResolveTarget(ShellContext context, string input)
    {
        if (LooksLikeUrl(input))
        {
            return input;
        }

        return Path.GetFullPath(input, context.WorkingDirectory.FullName);
    }

    private static bool LooksLikeUrl(string input)
    {
        return Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp ||
             uri.Scheme == Uri.UriSchemeHttps ||
             uri.Scheme == Uri.UriSchemeFile);
    }
}
