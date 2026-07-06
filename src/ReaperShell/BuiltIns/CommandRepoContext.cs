using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

internal sealed record CommandRepoContext(
    CommandRepoSettings Repo,
    CommandPackManifest Manifest,
    string CommandsRoot);
