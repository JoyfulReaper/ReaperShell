using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class WorkspaceRootResolverTests
{
    [Fact]
    public void FindsRootBySolutionFile()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Root, "ReaperShell.slnx"), string.Empty);

        var resolved = WorkspaceRootResolver.FindWorkspaceRoot(temp.Root);

        Assert.Equal(temp.Root, resolved);
    }

    [Fact]
    public void FindsRootBySrcAbstractionsProject()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, "src", "ReaperShell.Abstractions"));
        File.WriteAllText(
            Path.Combine(temp.Root, "src", "ReaperShell.Abstractions", "ReaperShell.Abstractions.csproj"),
            string.Empty);

        var resolved = WorkspaceRootResolver.FindWorkspaceRoot(temp.Root);

        Assert.Equal(temp.Root, resolved);
    }

    [Fact]
    public void FindsRootByAdjacentAbstractionsProject()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, "ReaperShell.Abstractions"));
        File.WriteAllText(
            Path.Combine(temp.Root, "ReaperShell.Abstractions", "ReaperShell.Abstractions.csproj"),
            string.Empty);

        var resolved = WorkspaceRootResolver.FindWorkspaceRoot(temp.Root);

        Assert.Equal(temp.Root, resolved);
    }

    [Fact]
    public void WalksUpwardToFindAdjacentAbstractionsProject()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, "ReaperShell.Abstractions"));
        File.WriteAllText(
            Path.Combine(temp.Root, "ReaperShell.Abstractions", "ReaperShell.Abstractions.csproj"),
            string.Empty);
        var nested = Path.Combine(temp.Root, "some", "nested", "folder");
        Directory.CreateDirectory(nested);

        var resolved = WorkspaceRootResolver.FindWorkspaceRoot(nested);

        Assert.Equal(temp.Root, resolved);
    }

    [Fact]
    public void MissingWorkspaceMarkersThrowUpdatedError()
    {
        using var temp = new TempDirectory();

        var exception = Assert.Throws<InvalidOperationException>(() => WorkspaceRootResolver.FindWorkspaceRoot(temp.Root));

        Assert.Contains("ReaperShell.slnx", exception.Message);
        Assert.Contains("src/ReaperShell.Abstractions/ReaperShell.Abstractions.csproj", exception.Message);
        Assert.Contains("ReaperShell.Abstractions/ReaperShell.Abstractions.csproj", exception.Message);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "ReaperShell.WorkspaceRootResolverTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
