namespace ReaperShell.BuiltIns;

internal static class CommandCommandUsage
{
    public static string TopLevel => "Usage: command <templates|list|new|remove|delete|rm> ...";

    public static string New => "Usage: command new <repo> <command-name> [--template <basic|file|process>] [--language <csharp|fsharp|vb>]";

    public static string Remove => "Usage: command <remove|delete|rm> <repo> <command-name>";
}
