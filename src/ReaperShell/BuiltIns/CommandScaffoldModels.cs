namespace ReaperShell.BuiltIns;

internal enum ScaffoldTemplate
{
    Basic,
    File,
    Process
}

internal enum ScaffoldLanguage
{
    CSharp,
    FSharp,
    VisualBasic
}

internal sealed record NewCommandOptions(
    string RepoName,
    string CommandName,
    ScaffoldTemplate Template,
    ScaffoldLanguage Language);
