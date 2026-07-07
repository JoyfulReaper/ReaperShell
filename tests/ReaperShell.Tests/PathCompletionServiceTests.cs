using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class PathCompletionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReaperShell.PathCompletionServiceTests", Guid.NewGuid().ToString("N"));
    private readonly PathCompletionService _service = new();

    public PathCompletionServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    [Fact]
    public void CompletesFileInCurrentDirectory()
    {
        var workingDirectory = CreateDirectory("current");
        File.WriteAllText(Path.Combine(workingDirectory, "README.md"), "hello");

        var completed = TryComplete("cat READ", workingDirectory);

        Assert.Equal("cat README.md", completed.UpdatedLine);
    }

    [Fact]
    public void CompletesFromCurrentDirectoryAfterTrailingWhitespace()
    {
        var workingDirectory = CreateDirectory("trailing-space");
        File.WriteAllText(Path.Combine(workingDirectory, "README.md"), "hello");

        var completed = TryComplete("cat ", workingDirectory);

        Assert.Equal("cat README.md", completed.UpdatedLine);
    }

    [Fact]
    public void CompletesDirectoryAndAppendsSeparator()
    {
        var workingDirectory = CreateDirectory("dirs");
        Directory.CreateDirectory(Path.Combine(workingDirectory, "src", "ReaperShell"));

        var completed = TryComplete("cd src/Rea", workingDirectory);

        Assert.Equal($"cd src{Path.DirectorySeparatorChar}ReaperShell{Path.DirectorySeparatorChar}", completed.UpdatedLine);
    }

    [Fact]
    public void UsesLongestCommonPrefixForMultipleMatches()
    {
        var workingDirectory = CreateDirectory("lcp");
        File.WriteAllText(Path.Combine(workingDirectory, "alpha.txt"), "a");
        File.WriteAllText(Path.Combine(workingDirectory, "alphabet.txt"), "b");

        var completed = TryComplete("cat alph", workingDirectory);

        Assert.Equal("cat alpha", completed.UpdatedLine);
    }

    [Fact]
    public void ShowsCandidatesWhenCommonPrefixDoesNotAdvance()
    {
        var workingDirectory = CreateDirectory("candidates");
        File.WriteAllText(Path.Combine(workingDirectory, "Red.txt"), "a");
        File.WriteAllText(Path.Combine(workingDirectory, "Reaper.md"), "b");

        var completed = TryComplete("cat Re", workingDirectory);

        Assert.Null(completed.UpdatedLine);
        Assert.True(completed.ShowCandidates);
        Assert.Contains("Red.txt", completed.Candidates);
        Assert.Contains("Reaper.md", completed.Candidates);
    }

    [Fact]
    public void HandlesQuotedPathWithSpaces()
    {
        var workingDirectory = CreateDirectory("quoted");
        File.WriteAllText(Path.Combine(workingDirectory, "some file.txt"), "hello");

        var completed = TryComplete("cat \"some f", workingDirectory);

        Assert.Equal("cat \"some file.txt\"", completed.UpdatedLine);
    }

    [Fact]
    public void CompletesQuotedPathAfterTrailingWhitespace()
    {
        var workingDirectory = CreateDirectory("quoted-trailing");
        File.WriteAllText(Path.Combine(workingDirectory, "some file.txt"), "hello");

        var completed = TryComplete("cat \"", workingDirectory);

        Assert.Equal("cat \"some file.txt\"", completed.UpdatedLine);
    }

    [Fact]
    public void ResolvesRelativePathsAgainstWorkingDirectory()
    {
        var workingDirectory = CreateDirectory("relative");
        Directory.CreateDirectory(Path.Combine(workingDirectory, "child"));
        File.WriteAllText(Path.Combine(workingDirectory, "child", "notes.txt"), "hello");

        var completed = TryComplete("cat child/no", workingDirectory);

        Assert.Equal($"cat child{Path.DirectorySeparatorChar}notes.txt", completed.UpdatedLine);
    }

    [Fact]
    public void DoesNotThrowForMissingDirectoriesOrInvalidFragments()
    {
        var workingDirectory = CreateDirectory("missing");

        var missingDirectory = _service.TryComplete("cat nope/file", () => new DirectoryInfo(workingDirectory), out var missingResult);
        var invalidFragment = _service.TryComplete("cat \"unterminated[", () => new DirectoryInfo(workingDirectory), out var invalidResult);

        Assert.False(missingDirectory);
        Assert.False(invalidFragment);
        Assert.Null(missingResult);
        Assert.Null(invalidResult);
    }

    [Fact]
    public void MatchesCaseInsensitivelyOnWindows()
    {
        var workingDirectory = CreateDirectory("case");
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path.Combine(workingDirectory, "README.md"), "hello");

            var completed = TryComplete("cat read", workingDirectory);
            Assert.Equal("cat README.md", completed.UpdatedLine);
        }
        else
        {
            File.WriteAllText(Path.Combine(workingDirectory, "readme.md"), "hello");

            var completed = TryComplete("cat read", workingDirectory);
            Assert.Equal("cat readme.md", completed.UpdatedLine);
        }
    }

    private PathCompletionResult TryComplete(string input, string workingDirectory)
    {
        var didComplete = _service.TryComplete(input, () => new DirectoryInfo(workingDirectory), out var result);
        Assert.True(didComplete);
        Assert.NotNull(result);
        return result;
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
