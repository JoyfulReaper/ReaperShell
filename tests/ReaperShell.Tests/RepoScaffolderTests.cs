using System.Diagnostics;
using ReaperShell.BuiltIns;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class RepoScaffolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReaperShell.RepoScaffolderTests", Guid.NewGuid().ToString("N"));
    private readonly string _workspaceRoot = WorkspaceRootResolver.FindWorkspaceRoot();

    public RepoScaffolderTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    [Fact]
    public async Task RepoNewScaffoldsProjectReferenceToAbstractions()
    {
        var repoRoot = Path.Combine(_root, "generated-repo");
        await RepoScaffolder.CreateGeneratedPackAsync("generated-repo", repoRoot, _workspaceRoot, CancellationToken.None);

        var projectPath = Path.Combine(repoRoot, "commands", "hello", "HelloCommand.csproj");

        Assert.True(File.Exists(projectPath));
        AssertProjectReferencesAstractions(projectPath);
        await BuildProjectAsync(projectPath);
    }

    private void AssertProjectReferencesAstractions(string projectPath)
    {
        var projectContents = File.ReadAllText(projectPath);
        var abstractionsProjectPath = Path.Combine(
            _workspaceRoot,
            "src",
            "ReaperShell.Abstractions",
            "ReaperShell.Abstractions.csproj");
        var relativeProjectReference = Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, abstractionsProjectPath);

        Assert.Contains(relativeProjectReference, projectContents, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task BuildProjectAsync(string projectPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--nologo");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start build for '{projectPath}'.");
        }

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Build failed for '{projectPath}' with exit code {process.ExitCode}.");
        }
    }
}
