using System.Diagnostics;
using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Plugins;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class CommandScaffoldingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReaperShell.CommandScaffoldingTests", Guid.NewGuid().ToString("N"));
    private readonly string _workspaceRoot = WorkspaceRootResolver.FindWorkspaceRoot();

    public CommandScaffoldingTests()
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
    public async Task DefaultLanguageScaffoldsCSharpProject()
    {
        var result = await ScaffoldAsync("default-csharp", "sample-tool");

        Assert.Equal(0, result.ExitCode);
        AssertGeneratedFiles(result, ".csproj", ".cs");
        await BuildProjectAsync(result.ProjectPath);
        AssertProjectReferencesAstractions(result.ProjectPath);
    }

    [Theory]
    [InlineData("csharp", ".csproj", ".cs")]
    [InlineData("cs", ".csproj", ".cs")]
    [InlineData("c#", ".csproj", ".cs")]
    [InlineData("fsharp", ".fsproj", ".fs")]
    [InlineData("fs", ".fsproj", ".fs")]
    [InlineData("f#", ".fsproj", ".fs")]
    [InlineData("vb", ".vbproj", ".vb")]
    [InlineData("vbnet", ".vbproj", ".vb")]
    public async Task LanguageAliasesScaffoldAndBuildBasicCommands(
        string languageArg,
        string projectExtension,
        string sourceExtension)
    {
        var result = await ScaffoldAsync($"alias-{SafeToken(languageArg)}", $"sample-{SafeToken(languageArg)}", "--language", languageArg);

        Assert.Equal(0, result.ExitCode);
        AssertGeneratedFiles(result, projectExtension, sourceExtension);
        await BuildProjectAsync(result.ProjectPath);
        AssertProjectReferencesAstractions(result.ProjectPath);
    }

    [Fact]
    public async Task TemplateAndLanguageOptionsAreOrderIndependent()
    {
        var leftToRight = await ScaffoldAsync("order-left", "sample-tool", "--template", "file", "--language", "vbnet");
        var rightToLeft = await ScaffoldAsync("order-right", "sample-tool", "--language", "vbnet", "--template", "file");

        Assert.Equal(0, leftToRight.ExitCode);
        Assert.Equal(0, rightToLeft.ExitCode);
        AssertGeneratedFiles(leftToRight, ".vbproj", ".vb");
        AssertGeneratedFiles(rightToLeft, ".vbproj", ".vb");
        AssertProjectReferencesAstractions(leftToRight.ProjectPath);
        AssertProjectReferencesAstractions(rightToLeft.ProjectPath);
    }

    [Fact]
    public async Task UnknownLanguageReturnsClearError()
    {
        var result = await ScaffoldAsync("unknown-language", "sample-tool", "--language", "ada");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown language: ada", result.StdErr);
    }

    [Fact]
    public void WorkspaceRootResolverFindsRepositoryRootFromNestedPath()
    {
        var tempRoot = Path.Combine(_root, "workspace-root");
        var nestedPath = Path.Combine(tempRoot, "src", "ReaperShell", "bin", "Debug", "net10.0");
        Directory.CreateDirectory(nestedPath);
        File.WriteAllText(Path.Combine(tempRoot, "ReaperShell.slnx"), string.Empty);

        var resolved = WorkspaceRootResolver.FindWorkspaceRoot(nestedPath);

        Assert.Equal(tempRoot, resolved);
    }

    [Fact]
    public void WorkspaceRootResolverThrowsWhenNoRootCanBeFound()
    {
        var pathA = Path.Combine(_root, "missing-a");
        var pathB = Path.Combine(_root, "missing-b");

        var exception = Assert.Throws<InvalidOperationException>(() => WorkspaceRootResolver.FindWorkspaceRoot(pathA, pathB));

        Assert.Contains("Could not locate the ReaperShell workspace root.", exception.Message);
    }

    [Theory]
    [InlineData("file", "fsharp", ".fsproj", ".fs")]
    [InlineData("process", "vb", ".vbproj", ".vb")]
    public async Task NonBasicTemplatesAreGeneratedForNonCSharpLanguages(
        string template,
        string languageArg,
        string projectExtension,
        string sourceExtension)
    {
        var result = await ScaffoldAsync($"template-{template}-{SafeToken(languageArg)}", $"sample-{template}-{SafeToken(languageArg)}", "--template", template, "--language", languageArg);

        Assert.Equal(0, result.ExitCode);
        AssertGeneratedFiles(result, projectExtension, sourceExtension);
        await BuildProjectAsync(result.ProjectPath);
        AssertProjectReferencesAstractions(result.ProjectPath);
    }

    private async Task<ScaffoldResult> ScaffoldAsync(string repoName, string commandName, params string[] extraArgs)
    {
        var repoRoot = Path.Combine(_root, repoName);
        Directory.CreateDirectory(repoRoot);

        await new CommandPackManifest
        {
            Id = repoName,
            Name = $"{repoName} Pack",
            Description = $"Generated command pack '{repoName}'.",
            CommandsPath = "commands"
        }.SaveAsync(Path.Combine(repoRoot, "shellpack.json"), CancellationToken.None);

        var settings = new ShellSettings();
        settings.Repos[repoName] = new CommandRepoSettings
        {
            Name = repoName,
            Source = repoRoot,
            LocalPath = repoRoot,
            Trusted = true
        };

        var command = new CommandCommand(settings, _workspaceRoot);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(stdout, stderr, new DirectoryInfo(repoRoot), services: null, CancellationToken.None);

        var arguments = new List<string> { "new", repoName, commandName };
        arguments.AddRange(extraArgs);

        var exitCode = await command.ExecuteAsync(context, arguments, CancellationToken.None);
        var className = ToPascalCase(commandName) + "Command";
        var commandDirectory = Path.Combine(repoRoot, "commands", commandName);

        var hasKnownLanguage = TryGetLanguage(arguments, out var normalizedLanguage);
        var projectPath = hasKnownLanguage
            ? Path.Combine(commandDirectory, className + GetProjectExtension(normalizedLanguage))
            : string.Empty;
        var sourcePath = hasKnownLanguage
            ? Path.Combine(commandDirectory, className + GetSourceExtension(normalizedLanguage))
            : string.Empty;

        return new ScaffoldResult(
            exitCode,
            stdout.ToString(),
            stderr.ToString(),
            commandDirectory,
            projectPath,
            sourcePath);
    }

    private static string GetProjectExtension(string language)
    {
        return language switch
        {
            "csharp" => ".csproj",
            "fsharp" => ".fsproj",
            "vb" => ".vbproj",
            _ => throw new InvalidOperationException($"Unsupported language: {language}")
        };
    }

    private static string GetSourceExtension(string language)
    {
        return language switch
        {
            "csharp" => ".cs",
            "fsharp" => ".fs",
            "vb" => ".vb",
            _ => throw new InvalidOperationException($"Unsupported language: {language}")
        };
    }

    private static bool TryGetLanguage(IReadOnlyList<string> args, out string language)
    {
        for (var index = 3; index < args.Count; index++)
        {
            if (string.Equals(args[index], "--language", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Count)
            {
                language = NormalizeLanguage(args[index + 1]);
                return language is "csharp" or "fsharp" or "vb";
            }
        }

        language = "csharp";
        return true;
    }

    private static string NormalizeLanguage(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "csharp" or "cs" or "c#" => "csharp",
            "fsharp" or "fs" or "f#" => "fsharp",
            "vb" or "vbnet" or "visualbasic" or "visual-basic" => "vb",
            _ => value.ToLowerInvariant()
        };
    }

    private void AssertGeneratedFiles(ScaffoldResult result, string projectExtension, string sourceExtension)
    {
        Assert.True(File.Exists(result.ProjectPath));
        Assert.True(File.Exists(result.SourcePath));
        Assert.EndsWith(projectExtension, result.ProjectPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(sourceExtension, result.SourcePath, StringComparison.OrdinalIgnoreCase);
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

    private static string SafeToken(string value)
    {
        return value
            .ToLowerInvariant()
            .Replace("#", "sharp")
            .Replace("+", "plus");
    }

    private static string ToPascalCase(string commandName)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var segment in commandName.Split('-', StringSplitOptions.RemoveEmptyEntries))
        {
            builder.Append(char.ToUpperInvariant(segment[0]));
            if (segment.Length > 1)
            {
                builder.Append(segment[1..]);
            }
        }

        return builder.ToString();
    }

    private sealed record ScaffoldResult(
        int ExitCode,
        string StdOut,
        string StdErr,
        string CommandDirectory,
        string ProjectPath,
        string SourcePath);
}
