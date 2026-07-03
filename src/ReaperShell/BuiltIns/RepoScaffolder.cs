using ReaperShell.Plugins;

namespace ReaperShell.BuiltIns;

internal static class RepoScaffolder
{
    public static async Task CreateGeneratedPackAsync(
        string repoName,
        string repoRoot,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(repoRoot);
        Directory.CreateDirectory(Path.Combine(repoRoot, "commands", "hello"));

        var manifest = new CommandPackManifest
        {
            Id = repoName,
            Name = $"{repoName} Pack",
            Description = $"Generated command pack '{repoName}'.",
            CommandsPath = "commands"
        };

        await manifest.SaveAsync(Path.Combine(repoRoot, "shellpack.json"), cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(repoRoot, "commands", "hello", "HelloCommand.csproj"),
            GetGeneratedProjectFileContents(repoRoot, workspaceRoot),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(repoRoot, "commands", "hello", "HelloCommand.cs"),
            GetGeneratedCommandContents(),
            cancellationToken);
    }

    private static string GetGeneratedProjectFileContents(string repoRoot, string workspaceRoot)
    {
        var projectDirectory = Path.Combine(repoRoot, "commands", "hello");
        var abstractionsProjectPath = Path.Combine(
            workspaceRoot,
            "src",
            "ReaperShell.Abstractions",
            "ReaperShell.Abstractions.csproj");
        var relativeProjectReference = Path.GetRelativePath(projectDirectory, abstractionsProjectPath);

        return $$"""
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="{{relativeProjectReference}}" />
  </ItemGroup>

</Project>
""";
    }

    private static string GetGeneratedCommandContents()
    {
        return """
using ReaperShell.Abstractions;

namespace HelloCommand;

public sealed class HelloCommand : IShellCommand
{
    public string Name => "hello";

    public string Description => "Prints a hello message from a live-loaded command.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        context.WriteLine("Hello from a live-loaded ReaperShell command.");
        return Task.FromResult(0);
    }
}
""";
    }
}
