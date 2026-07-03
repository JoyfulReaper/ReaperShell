using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class EditCommand : IShellCommand
{
    private readonly EditorLauncher _editorLauncher;

    public EditCommand(EditorLauncher editorLauncher)
    {
        _editorLauncher = editorLauncher;
    }

    public string Name => "edit";

    public string Description => "Opens a file or directory in the configured editor.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: edit <path>");
            return 1;
        }

        var targetPath = Path.GetFullPath(args[0], context.WorkingDirectory.FullName);
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            context.WriteErrorLine($"Path does not exist: {targetPath}");
            return 1;
        }

        return await _editorLauncher.TryOpenAsync(context, targetPath, cancellationToken) ? 0 : 1;
    }
}
