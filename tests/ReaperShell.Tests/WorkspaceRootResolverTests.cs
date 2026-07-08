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
    public void SourceCheckoutFromAppBinResolvesRepoRoot()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Root, "ReaperShell.slnx"), string.Empty);
        Directory.CreateDirectory(Path.Combine(temp.Root, "src", "ReaperShell", "bin", "Debug", "net10.0"));
        Directory.CreateDirectory(Path.Combine(temp.Root, "src", "ReaperShell.Abstractions"));
        File.WriteAllText(
            Path.Combine(temp.Root, "src", "ReaperShell.Abstractions", "ReaperShell.Abstractions.csproj"),
            string.Empty);

        var resolved = WorkspaceRootResolver.FindWorkspaceRoot(Path.Combine(temp.Root, "src", "ReaperShell", "bin", "Debug", "net10.0"));

        Assert.Equal(temp.Root, resolved);
    }

    [Fact]
    public void FindsRootByAdjacentAbstractionsProject()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Root, "ReaperShell.dll"), string.Empty);
        Directory.CreateDirectory(Path.Combine(temp.Root, "ReaperShell.Abstractions"));
        File.WriteAllText(
            Path.Combine(temp.Root, "ReaperShell.Abstractions", "ReaperShell.Abstractions.csproj"),
            string.Empty);

        var resolved = WorkspaceRootResolver.FindWorkspaceRoot(temp.Root);

        Assert.Equal(temp.Root, resolved);
    }

    [Fact]
    public void AdjacentAbstractionsWithoutAppMarkerDoesNotBecomeRoot()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, "src", "ReaperShell.Abstractions"));
        File.WriteAllText(
            Path.Combine(temp.Root, "src", "ReaperShell.Abstractions", "ReaperShell.Abstractions.csproj"),
            string.Empty);

        var resolved = WorkspaceRootResolver.FindWorkspaceRoot(Path.Combine(temp.Root, "src"));

        Assert.Equal(temp.Root, resolved);
    }

    [Fact]
    public void WalksUpwardToFindAdjacentAbstractionsProject()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Root, "ReaperShell.dll"), string.Empty);
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

    [Fact]
    public void SourceLayoutProjectPathIsResolvedUnderSrc()
    {
        using var temp = new TempDirectory();
        var abstractionsDir = Path.Combine(temp.Root, "src", "ReaperShell.Abstractions");
        Directory.CreateDirectory(abstractionsDir);
        var projectPath = Path.Combine(abstractionsDir, "ReaperShell.Abstractions.csproj");
        File.WriteAllText(projectPath, string.Empty);

        var resolved = WorkspaceRootResolver.GetReaperShellAbstractionsProjectPath(temp.Root);

        Assert.Equal(projectPath, resolved);
    }

    [Fact]
    public void PublishedLayoutProjectPathIsResolvedAdjacentToApp()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Root, "ReaperShell.dll"), string.Empty);
        var abstractionsDir = Path.Combine(temp.Root, "ReaperShell.Abstractions");
        Directory.CreateDirectory(abstractionsDir);
        var projectPath = Path.Combine(abstractionsDir, "ReaperShell.Abstractions.csproj");
        File.WriteAllText(projectPath, string.Empty);

        var resolved = WorkspaceRootResolver.GetReaperShellAbstractionsProjectPath(temp.Root);

        Assert.Equal(projectPath, resolved);
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
