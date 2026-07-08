namespace ReaperShell.Shell;

public sealed record CommandLine(IReadOnlyList<CommandPipeline> Pipelines)
{
    public bool IsSimpleCommand =>
        Pipelines.Count == 1 &&
        Pipelines[0].Segments.Count == 1 &&
        Pipelines[0].Segments[0].Redirections.Count == 0 &&
        Pipelines[0].NextOperator is null;
}

public sealed record CommandPipeline(
    IReadOnlyList<CommandSegment> Segments,
    CommandChainOperator? NextOperator);

public sealed record CommandSegment(
    IReadOnlyList<string> Tokens,
    IReadOnlyList<CommandRedirection> Redirections);

public sealed record CommandRedirection(CommandRedirectionKind Kind, string TargetPath);

public enum CommandRedirectionKind
{
    StdoutOverwrite,
    StdoutAppend,
    StderrOverwrite,
    StderrAppend,
    CombinedOverwrite,
    CombinedAppend
}

public enum CommandChainOperator
{
    AndAlso,
    OrElse
}
