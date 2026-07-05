using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class FileSystemCommandsTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReaperShell.FileSystemCommandsTests", Guid.NewGuid().ToString("N"));

    public FileSystemCommandsTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task EchoPrintsJoinedArguments()
    {
        var (exitCode, stdout, stderr) = await ExecuteAsync(new EchoCommand(), ["hello", "world"]);

        Assert.Equal(0, exitCode);
        Assert.Equal("hello world", stdout.TrimEnd());
        Assert.True(string.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public async Task HeadPrintsFirstLines()
    {
        await File.WriteAllLinesAsync(Path.Combine(_root, "head.txt"), ["one", "two", "three", "four"]);

        var (exitCode, stdout, stderr) = await ExecuteAsync(new HeadCommand(), ["-n", "2", "head.txt"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "one", "two" }, SplitLines(stdout));
        Assert.True(string.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public async Task TailPrintsLastLines()
    {
        await File.WriteAllLinesAsync(Path.Combine(_root, "tail.txt"), ["one", "two", "three", "four"]);

        var (exitCode, stdout, stderr) = await ExecuteAsync(new TailCommand(), ["-n", "2", "tail.txt"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "three", "four" }, SplitLines(stdout));
        Assert.True(string.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public async Task GrepReturnsMatchesNoMatchesAndErrors()
    {
        await File.WriteAllLinesAsync(Path.Combine(_root, "grep.txt"), ["alpha", "Beta", "gamma beta"]);

        var match = await ExecuteAsync(new GrepCommand(), ["-i", "beta", "grep.txt"]);
        Assert.Equal(0, match.ExitCode);
        Assert.Equal(new[] { "Beta", "gamma beta" }, SplitLines(match.StdOut));

        var noMatch = await ExecuteAsync(new GrepCommand(), ["zeta", "grep.txt"]);
        Assert.Equal(1, noMatch.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(noMatch.StdOut));

        var fileError = await ExecuteAsync(new GrepCommand(), ["zeta", "missing.txt"]);
        Assert.Equal(2, fileError.ExitCode);
        Assert.Contains("File does not exist", fileError.StdErr);
    }

    [Fact]
    public async Task MkdirCreatesMultipleDirectoriesRelativeToWorkingDirectory()
    {
        var (exitCode, stdout, stderr) = await ExecuteAsync(new MkdirCommand(), ["alpha", Path.Combine("nested", "beta")]);

        Assert.Equal(0, exitCode);
        Assert.True(Directory.Exists(Path.Combine(_root, "alpha")));
        Assert.True(Directory.Exists(Path.Combine(_root, "nested", "beta")));
        Assert.True(string.IsNullOrWhiteSpace(stdout));
        Assert.True(string.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public async Task TreePrintsSimpleDirectoryStructure()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tree-root", "alpha"));
        Directory.CreateDirectory(Path.Combine(_root, "tree-root", "beta"));
        await File.WriteAllTextAsync(Path.Combine(_root, "tree-root", "root.txt"), "root");
        await File.WriteAllTextAsync(Path.Combine(_root, "tree-root", "alpha", "child.txt"), "child");

        var tree = await ExecuteAsync(new TreeCommand(), ["tree-root"]);

        Assert.Equal(0, tree.ExitCode);
        Assert.Contains("tree-root", tree.StdOut);
        Assert.Contains("alpha", tree.StdOut);
        Assert.Contains("beta", tree.StdOut);
        Assert.Contains("root.txt", tree.StdOut);
        Assert.Contains("child.txt", tree.StdOut);
        Assert.True(string.IsNullOrWhiteSpace(tree.StdErr));

        var directoriesOnly = await ExecuteAsync(new TreeCommand(), ["tree-root", "-d"]);
        Assert.Equal(0, directoriesOnly.ExitCode);
        Assert.Contains("tree-root", directoriesOnly.StdOut);
        Assert.Contains("alpha", directoriesOnly.StdOut);
        Assert.Contains("beta", directoriesOnly.StdOut);
        Assert.DoesNotContain("root.txt", directoriesOnly.StdOut);
        Assert.DoesNotContain("child.txt", directoriesOnly.StdOut);
    }

    [Fact]
    public async Task TouchCreatesFilesAndUpdatesLastWriteTime()
    {
        var filePath = Path.Combine("docs", "note.txt");

        var first = await ExecuteAsync(new TouchCommand(), [filePath]);
        Assert.Equal(0, first.ExitCode);

        var absoluteFilePath = Path.Combine(_root, filePath);
        Assert.True(File.Exists(absoluteFilePath));
        var createdTime = File.GetLastWriteTimeUtc(absoluteFilePath);

        File.SetLastWriteTimeUtc(absoluteFilePath, createdTime.AddHours(-2));
        var beforeTouch = File.GetLastWriteTimeUtc(absoluteFilePath);

        var second = await ExecuteAsync(new TouchCommand(), [filePath]);
        Assert.Equal(0, second.ExitCode);
        var afterTouch = File.GetLastWriteTimeUtc(absoluteFilePath);

        Assert.True(afterTouch > beforeTouch);
        Assert.True(string.IsNullOrWhiteSpace(first.StdErr));
        Assert.True(string.IsNullOrWhiteSpace(second.StdErr));
    }

    [Fact]
    public async Task RmRequiresRecursiveForDirectoriesAndForceForMissingPaths()
    {
        var targetDirectory = Path.Combine(_root, "remove-me");
        Directory.CreateDirectory(Path.Combine(targetDirectory, "nested"));
        await File.WriteAllTextAsync(Path.Combine(targetDirectory, "nested", "file.txt"), "content");

        var missing = await ExecuteAsync(new RmCommand(), ["does-not-exist"]);
        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("Path does not exist", missing.StdErr);

        var noRecursive = await ExecuteAsync(new RmCommand(), ["remove-me"]);
        Assert.Equal(1, noRecursive.ExitCode);
        Assert.True(Directory.Exists(targetDirectory));
        Assert.Contains("Cannot remove directory without -r/--recursive", noRecursive.StdErr);

        var forced = await ExecuteAsync(new RmCommand(), ["-f", "does-not-exist"]);
        Assert.Equal(0, forced.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(forced.StdErr));

        var recursive = await ExecuteAsync(new RmCommand(), ["-r", "remove-me"]);
        Assert.Equal(0, recursive.ExitCode);
        Assert.False(Directory.Exists(targetDirectory));
        Assert.True(string.IsNullOrWhiteSpace(recursive.StdErr));
    }

    [Fact]
    public async Task CpCopiesFileAndDirectoryRelativeToWorkingDirectory()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "source.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(_root, "dest"));

        var fileCopy = await ExecuteAsync(new CpCommand(), ["source.txt", "copy.txt"]);
        Assert.Equal(0, fileCopy.ExitCode);
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(_root, "copy.txt")));

        var fileIntoDirectory = await ExecuteAsync(new CpCommand(), ["source.txt", "dest"]);
        Assert.Equal(0, fileIntoDirectory.ExitCode);
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(_root, "dest", "source.txt")));

        Directory.CreateDirectory(Path.Combine(_root, "tree", "child"));
        await File.WriteAllTextAsync(Path.Combine(_root, "tree", "child", "file.txt"), "tree");

        var recursive = await ExecuteAsync(new CpCommand(), ["-r", "tree", "tree-copy"]);
        Assert.Equal(0, recursive.ExitCode);
        Assert.Equal("tree", await File.ReadAllTextAsync(Path.Combine(_root, "tree-copy", "child", "file.txt")));
    }

    [Fact]
    public async Task MvMovesAndRenamesFilesAndDirectories()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "from.txt"), "move me");

        var fileMove = await ExecuteAsync(new MvCommand(), ["from.txt", "to.txt"]);
        Assert.Equal(0, fileMove.ExitCode);
        Assert.False(File.Exists(Path.Combine(_root, "from.txt")));
        Assert.Equal("move me", await File.ReadAllTextAsync(Path.Combine(_root, "to.txt")));

        Directory.CreateDirectory(Path.Combine(_root, "source-dir", "inner"));
        await File.WriteAllTextAsync(Path.Combine(_root, "source-dir", "inner", "data.txt"), "dir move");
        Directory.CreateDirectory(Path.Combine(_root, "target-parent"));

        var directoryMove = await ExecuteAsync(new MvCommand(), ["source-dir", "target-parent"]);
        Assert.Equal(0, directoryMove.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(_root, "source-dir")));
        Assert.Equal("dir move", await File.ReadAllTextAsync(Path.Combine(_root, "target-parent", "source-dir", "inner", "data.txt")));
    }

    [Fact]
    public async Task OpenValidatesArgumentCount()
    {
        var (exitCode, stdout, stderr) = await ExecuteAsync(new OpenCommand(), []);

        Assert.Equal(1, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stdout));
        Assert.Contains("Usage: open <path-or-url>", stderr);
    }

    private async Task<CommandResult> ExecuteAsync(IShellCommand command, IReadOnlyList<string> args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(stdout, stderr, new DirectoryInfo(_root), services: null, CancellationToken.None);
        var exitCode = await command.ExecuteAsync(context, args, CancellationToken.None);
        return new CommandResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private static string[] SplitLines(string value)
    {
        return value.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}
