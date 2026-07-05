namespace ReaperShell.Shell;

internal static class ExternalCommandDiagnostics
{
    public static bool TryGetInfo(
        string commandName,
        ShellSettings settings,
        out ExternalCommandInfo info)
    {
        info = default!;

        if (!ExternalCommandResolver.TryResolveExecutable(commandName, out var executablePath))
        {
            return false;
        }

        info = new ExternalCommandInfo(
            executablePath,
            settings.ExternalCommandMode,
            GetRunnableText(settings.ExternalCommandMode));
        return true;
    }

    private static string GetRunnableText(ExternalCommandMode mode)
    {
        return mode switch
        {
            ExternalCommandMode.PathOnly => "Yes",
            ExternalCommandMode.Disabled => "No, external command fallback is disabled",
            ExternalCommandMode.Shell => "No, Shell mode is reserved but not implemented",
            _ => "No"
        };
    }
}

internal sealed record ExternalCommandInfo(
    string Path,
    ExternalCommandMode Mode,
    string RunnableText);
