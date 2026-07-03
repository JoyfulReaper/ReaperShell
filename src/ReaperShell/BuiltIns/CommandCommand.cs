using System.Text;
using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class CommandCommand : IShellCommand
{
    private static readonly string[] TemplateNames = ["basic", "file", "process"];

    private readonly ShellSettings _settings;
    private readonly string _workspaceRoot;

    public CommandCommand(ShellSettings settings, string workspaceRoot)
    {
        _settings = settings;
        _workspaceRoot = workspaceRoot;
    }

    public string Name => "command";

    public string Description => "Lists and forges commands inside an existing command pack.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            return Task.FromResult(WriteUsage(context));
        }

        return args[0].ToLowerInvariant() switch
        {
            "templates" => Task.FromResult(ListTemplates(context, args)),
            "list" => ListCommandsAsync(context, args, cancellationToken),
            "new" => CreateCommandAsync(context, args, cancellationToken),
            _ => Task.FromResult(WriteUsage(context))
        };
    }

    private int ListTemplates(ShellContext context, IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: command templates");
            return 1;
        }

        foreach (var templateName in TemplateNames)
        {
            context.WriteLine(templateName);
        }

        return 0;
    }

    private async Task<int> ListCommandsAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 2)
        {
            context.WriteErrorLine("Usage: command list <repo>");
            return 1;
        }

        var manifestResult = await LoadRepoManifestAsync(context, args[1], cancellationToken);
        if (!manifestResult.Success)
        {
            return 1;
        }

        var repo = manifestResult.Repo!;
        var manifest = manifestResult.Manifest!;
        if (!TryResolveCommandsRoot(repo.LocalPath, manifest, context, out var commandsRoot))
        {
            return 1;
        }

        var commandProjects = GetCommandProjects(commandsRoot);
        if (commandProjects.Count == 0)
        {
            context.WriteLine("No command projects were found.");
            return 0;
        }

        foreach (var commandProject in commandProjects)
        {
            context.WriteLine($"{Path.GetFileNameWithoutExtension(commandProject)} | {commandProject}");
        }

        return 0;
    }

    private async Task<int> CreateCommandAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count is not 3 and not 5)
        {
            context.WriteErrorLine("Usage: command new <repo> <command-name> [--template <basic|file|process>]");
            return 1;
        }

        var templateName = "basic";
        if (args.Count == 5)
        {
            if (!string.Equals(args[3], "--template", StringComparison.OrdinalIgnoreCase))
            {
                context.WriteErrorLine("Usage: command new <repo> <command-name> [--template <basic|file|process>]");
                return 1;
            }

            templateName = args[4].ToLowerInvariant();
            if (!TemplateNames.Contains(templateName, StringComparer.OrdinalIgnoreCase))
            {
                context.WriteErrorLine($"Unknown template: {args[4]}");
                return 1;
            }
        }

        if (!TryValidateCommandName(args[2], context, out var commandName))
        {
            return 1;
        }

        var manifestResult = await LoadRepoManifestAsync(context, args[1], cancellationToken);
        if (!manifestResult.Success)
        {
            return 1;
        }

        var repo = manifestResult.Repo!;
        var manifest = manifestResult.Manifest!;
        if (!TryResolveCommandsRoot(repo.LocalPath, manifest, context, out var commandsRoot))
        {
            return 1;
        }

        Directory.CreateDirectory(commandsRoot);

        var commandDirectory = Path.Combine(commandsRoot, commandName);
        if (Directory.Exists(commandDirectory))
        {
            context.WriteErrorLine($"The command directory already exists: {commandDirectory}");
            return 1;
        }

        var className = ToPascalCase(commandName) + "Command";
        var commandProjectPath = Path.Combine(commandDirectory, className + ".csproj");
        var commandSourcePath = Path.Combine(commandDirectory, className + ".cs");

        Directory.CreateDirectory(commandDirectory);
        try
        {
            await File.WriteAllTextAsync(
                commandProjectPath,
                GetProjectFileContents(commandDirectory),
                cancellationToken);

            await File.WriteAllTextAsync(
                commandSourcePath,
                GetTemplateSource(templateName, className, commandName),
                cancellationToken);
        }
        catch
        {
            if (Directory.Exists(commandDirectory))
            {
                Directory.Delete(commandDirectory, recursive: true);
            }

            throw;
        }

        context.WriteLine($"Created command '{commandName}' in repo '{repo.Name}'.");
        context.WriteLine(commandProjectPath);
        context.WriteLine(commandSourcePath);
        return 0;
    }

    private async Task<RepoManifestLoadResult> LoadRepoManifestAsync(
        ShellContext context,
        string repoName,
        CancellationToken cancellationToken)
    {
        if (!_settings.Repos.TryGetValue(repoName, out var foundRepo))
        {
            context.WriteErrorLine($"Repo '{repoName}' is not registered.");
            return new RepoManifestLoadResult(false, null, null);
        }

        if (!Directory.Exists(foundRepo.LocalPath))
        {
            context.WriteErrorLine($"Repo path does not exist: {foundRepo.LocalPath}");
            return new RepoManifestLoadResult(false, null, null);
        }

        var manifestPath = Path.Combine(foundRepo.LocalPath, "shellpack.json");
        if (!File.Exists(manifestPath))
        {
            context.WriteErrorLine($"shellpack.json was not found: {manifestPath}");
            return new RepoManifestLoadResult(false, null, null);
        }

        try
        {
            var manifest = await CommandPackManifest.LoadAsync(manifestPath, cancellationToken);
            return new RepoManifestLoadResult(true, foundRepo, manifest);
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Failed to load shellpack.json: {ex.Message}");
            return new RepoManifestLoadResult(false, null, null);
        }
    }

    private static List<string> GetCommandProjects(string commandsRoot)
    {
        if (!Directory.Exists(commandsRoot))
        {
            return [];
        }

        return Directory.GetFiles(commandsRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(path => CommandPackPathResolver.EnsurePathWithinRoot(commandsRoot, path, "Command project path"))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool TryValidateCommandName(
        string candidate,
        ShellContext context,
        out string commandName)
    {
        commandName = candidate;
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Any(char.IsWhiteSpace) ||
            candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            candidate.Contains(Path.DirectorySeparatorChar) ||
            candidate.Contains(Path.AltDirectorySeparatorChar))
        {
            context.WriteErrorLine("Command names must be simple lowercase kebab-case names.");
            return false;
        }

        if (candidate.Any(char.IsUpper) ||
            !candidate.All(character => char.IsLower(character) || char.IsDigit(character) || character == '-') ||
            candidate.StartsWith('-') ||
            candidate.EndsWith('-') ||
            candidate.Contains("--", StringComparison.Ordinal))
        {
            context.WriteErrorLine("Command names must be lowercase kebab-case.");
            return false;
        }

        return true;
    }

    private static bool TryResolveCommandsRoot(
        string repoRoot,
        CommandPackManifest manifest,
        ShellContext context,
        out string commandsRoot)
    {
        commandsRoot = string.Empty;

        try
        {
            commandsRoot = CommandPackPathResolver.ResolveCommandsRoot(repoRoot, manifest.CommandsPath);
            return true;
        }
        catch (Exception ex)
        {
            context.WriteErrorLine(ex.Message);
            return false;
        }
    }

    private string GetProjectFileContents(string commandDirectory)
    {
        var abstractionsProjectPath = Path.Combine(
            _workspaceRoot,
            "src",
            "ReaperShell.Abstractions",
            "ReaperShell.Abstractions.csproj");
        var relativeProjectReference = Path.GetRelativePath(commandDirectory, abstractionsProjectPath);

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

    private static string GetTemplateSource(string templateName, string className, string commandName)
    {
        return templateName switch
        {
            "basic" => GetBasicTemplateSource(className, commandName),
            "file" => GetFileTemplateSource(className, commandName),
            "process" => GetProcessTemplateSource(className, commandName),
            _ => throw new InvalidOperationException($"Unsupported template: {templateName}")
        };
    }

    private static string GetBasicTemplateSource(string className, string commandName)
    {
        return $$"""
using ReaperShell.Abstractions;

namespace {{className}};

public sealed class {{className}} : IShellCommand
{
    public string Name => "{{commandName}}";

    public string Description => "Generated by ReaperShell.";

    public Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        context.WriteLine("{{commandName}} command is alive.");
        return Task.FromResult(0);
    }
}
""";
    }

    private static string GetFileTemplateSource(string className, string commandName)
    {
        return $$"""
using ReaperShell.Abstractions;

namespace {{className}};

public sealed class {{className}} : IShellCommand
{
    public string Name => "{{commandName}}";

    public string Description => "Generated by ReaperShell. Reads a text file.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count != 1)
        {
            context.WriteErrorLine("Usage: {{commandName}} <file>");
            return 1;
        }

        var filePath = Path.GetFullPath(args[0], context.WorkingDirectory.FullName);
        if (!File.Exists(filePath))
        {
            context.WriteErrorLine($"File not found: {filePath}");
            return 1;
        }

        var contents = await File.ReadAllTextAsync(filePath, cancellationToken);
        context.WriteLine(contents);
        return 0;
    }
}
""";
    }

    private static string GetProcessTemplateSource(string className, string commandName)
    {
        return $$"""
using System.Diagnostics;
using ReaperShell.Abstractions;

namespace {{className}};

public sealed class {{className}} : IShellCommand
{
    public string Name => "{{commandName}}";

    public string Description => "Generated by ReaperShell. Runs a local process.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count == 0)
        {
            context.WriteErrorLine("Usage: {{commandName}} <executable> [args...]");
            return 1;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = args[0],
            WorkingDirectory = context.WorkingDirectory.FullName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in args.Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                context.WriteLine(eventArgs.Data);
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                context.WriteErrorLine(eventArgs.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                context.WriteErrorLine($"Failed to start process: {args[0]}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Failed to start process: {ex.Message}");
            return 1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
""";
    }

    private static string ToPascalCase(string commandName)
    {
        var builder = new StringBuilder();
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

    private static int WriteUsage(ShellContext context)
    {
        context.WriteErrorLine("Usage: command <templates|list|new> ...");
        return 1;
    }
}

internal sealed record RepoManifestLoadResult(
    bool Success,
    CommandRepoSettings? Repo,
    CommandPackManifest? Manifest);
