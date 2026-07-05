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

    private async Task<CommandResult> ExecuteAsync(IShellCommand command, IReadOnlyList<string> args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(stdout, stderr, new DirectoryInfo(_root), services: null, CancellationToken.None);
        var exitCode = await command.ExecuteAsync(context, args, CancellationToken.None);
        return new CommandResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    public sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}
