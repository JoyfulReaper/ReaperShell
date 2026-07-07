namespace ReaperShell.Abstractions;

public static class ShellColorPolicy
{
    public static bool ShouldUseColor(
        ShellColorMode mode,
        bool isRedirected,
        bool noColorEnvironmentSet,
        bool isConsoleWriter)
    {
        if (mode == ShellColorMode.Never)
        {
            return false;
        }

        if (!isConsoleWriter || isRedirected)
        {
            return false;
        }

        return mode == ShellColorMode.Always || !noColorEnvironmentSet;
    }
}
