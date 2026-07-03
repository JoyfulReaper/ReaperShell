using ReaperShell.Abstractions;

namespace ReaperShell.Shell;

public static class ShellBanner
{
    public static void Write(ShellContext context)
    {
        context.WriteLine("REAPER SHELL v0.1");
        context.WriteLine("Live command environment online.");
    }
}
