namespace ReaperShell.Shell;

internal interface ILineCompletionResult
{
    string? UpdatedLine { get; }

    bool ShowCandidates { get; }

    IReadOnlyList<string> Candidates { get; }
}
