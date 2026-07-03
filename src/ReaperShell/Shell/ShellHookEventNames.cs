namespace ReaperShell.Shell;

public static class ShellHookEventNames
{
    public const string Startup = "startup";
    public const string BeforeCommand = "before-command";
    public const string AfterCommand = "after-command";
    public const string RepoLoaded = "repo-loaded";
    public const string RepoUnloaded = "repo-unloaded";
    public const string RepoReloaded = "repo-reloaded";
    public const string RepoReloadFailed = "repo-reload-failed";
    public const string ShellExit = "shell-exit";

    public static IReadOnlyList<string> All { get; } =
    [
        Startup,
        BeforeCommand,
        AfterCommand,
        RepoLoaded,
        RepoUnloaded,
        RepoReloaded,
        RepoReloadFailed,
        ShellExit
    ];

    public static bool IsSupported(string eventName)
    {
        return All.Contains(eventName, StringComparer.OrdinalIgnoreCase);
    }
}
