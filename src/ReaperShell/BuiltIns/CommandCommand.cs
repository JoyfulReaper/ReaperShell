using System.Text;
using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class CommandCommand : IShellCommand
{
    private static readonly string[] TemplateNames = ["basic", "file", "process"];
    private static readonly string[] LanguageNames = ["csharp", "fsharp", "vb"];

    private readonly ShellSettings _settings;
    private readonly CommandPackManager _commandPackManager;
    private readonly string _workspaceRoot;

    public CommandCommand(ShellSettings settings, CommandPackManager commandPackManager, string workspaceRoot)
    {
        _settings = settings;
        _commandPackManager = commandPackManager;
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
            "remove" => RemoveCommandAsync(context, args, cancellationToken),
            "delete" => RemoveCommandAsync(context, args, cancellationToken),
            "rm" => RemoveCommandAsync(context, args, cancellationToken),
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
        if (!TryParseNewCommandArgs(context, args, out var options))
        {
            return 1;
        }

        var manifestResult = await LoadRepoManifestAsync(context, options.RepoName, cancellationToken);
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

        var commandDirectory = Path.Combine(commandsRoot, options.CommandName);
        if (Directory.Exists(commandDirectory))
        {
            context.WriteErrorLine($"The command directory already exists: {commandDirectory}");
            return 1;
        }

        var className = ToPascalCase(options.CommandName) + "Command";
        var commandProjectPath = Path.Combine(commandDirectory, className + GetProjectFileExtension(options.Language));
        var commandSourcePath = Path.Combine(commandDirectory, className + GetSourceFileExtension(options.Language));

        Directory.CreateDirectory(commandDirectory);
        try
        {
            await File.WriteAllTextAsync(
                commandProjectPath,
                GetProjectFileContents(commandDirectory, className, options.Language),
                cancellationToken);

            await File.WriteAllTextAsync(
                commandSourcePath,
                GetTemplateSource(options.Template, options.Language, className, options.CommandName),
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

        context.WriteLine($"Created command '{options.CommandName}' in repo '{repo.Name}'.");
        context.WriteLine(commandProjectPath);
        context.WriteLine(commandSourcePath);
        return 0;
    }

    private async Task<int> RemoveCommandAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count != 3)
        {
            context.WriteErrorLine(GetRemoveUsage());
            return 1;
        }

        var repoName = args[1];
        var commandName = args[2];
        if (!TryValidateCommandName(commandName, context, out var validatedCommandName))
        {
            return 1;
        }

        var manifestResult = await LoadRepoManifestAsync(context, repoName, cancellationToken);
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

        var commandDirectory = GetCommandDirectory(commandsRoot, validatedCommandName, context);
        if (commandDirectory is null)
        {
            return 1;
        }

        if (!Directory.Exists(commandDirectory))
        {
            context.WriteErrorLine($"Command directory does not exist: {commandDirectory}");
            return 1;
        }

        try
        {
            Directory.Delete(commandDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            context.WriteErrorLine($"Failed to remove command directory: {ex.Message}");
            return 1;
        }

        context.WriteLine($"Removed command '{validatedCommandName}' from repo '{repo.Name}'.");
        if (_commandPackManager.IsLoaded(repo.Name))
        {
            context.WriteLine(
                $"Repo '{repo.Name}' is currently loaded. Run 'repo reload {repo.Name}' to update loaded commands.");
        }

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
        return CommandProjectDiscovery.DiscoverProjects(commandsRoot);
    }

    private static bool TryValidateCommandName(
        string candidate,
        ShellContext context,
        out string commandName)
    {
        commandName = candidate;
        if (!ShellNameValidator.IsLowerKebabCaseName(candidate))
        {
            context.WriteErrorLine("Command names must start with a lowercase letter and use lowercase kebab-case.");
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

    private static string? GetCommandDirectory(string commandsRoot, string commandName, ShellContext context)
    {
        try
        {
            return CommandPackPathResolver.EnsurePathWithinRoot(
                commandsRoot,
                Path.Combine(commandsRoot, commandName),
                "Command directory");
        }
        catch (Exception ex)
        {
            context.WriteErrorLine(ex.Message);
            return null;
        }
    }

    private static bool TryParseNewCommandArgs(
        ShellContext context,
        IReadOnlyList<string> args,
        out NewCommandOptions options)
    {
        options = default!;
        if (args.Count < 3)
        {
            context.WriteErrorLine(GetNewUsage());
            return false;
        }

        var repoName = args[1];
        var commandName = args[2];
        if (!TryValidateCommandName(commandName, context, out var validatedCommandName))
        {
            return false;
        }

        var template = ScaffoldTemplate.Basic;
        var language = ScaffoldLanguage.CSharp;

        for (var index = 3; index < args.Count; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--template", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    context.WriteErrorLine("Missing value for --template.");
                    return false;
                }

                if (!TryParseTemplate(args[++index], out template))
                {
                    context.WriteErrorLine($"Unknown template: {args[index]}");
                    return false;
                }

                continue;
            }

            if (string.Equals(arg, "--language", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    context.WriteErrorLine("Missing value for --language.");
                    return false;
                }

                if (!TryParseLanguage(args[++index], out language))
                {
                    context.WriteErrorLine($"Unknown language: {args[index]}");
                    return false;
                }

                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal) && arg is not "-")
            {
                context.WriteErrorLine($"Unknown option: {arg}");
                return false;
            }

            context.WriteErrorLine($"Unexpected argument: {arg}");
            return false;
        }

        options = new NewCommandOptions(repoName, validatedCommandName, template, language);
        return true;
    }

    private string GetProjectFileContents(string commandDirectory, string className, ScaffoldLanguage language)
    {
        var abstractionsProjectPath = Path.Combine(
            _workspaceRoot,
            "src",
            "ReaperShell.Abstractions",
            "ReaperShell.Abstractions.csproj");
        var relativeProjectReference = Path.GetRelativePath(commandDirectory, abstractionsProjectPath);

        return language switch
        {
            ScaffoldLanguage.CSharp => $$"""
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
""",
            ScaffoldLanguage.FSharp => $$"""
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="{{className}}.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="{{relativeProjectReference}}" />
  </ItemGroup>

</Project>
""",
            ScaffoldLanguage.VisualBasic => $$"""
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>{{className}}</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="{{relativeProjectReference}}" />
  </ItemGroup>

</Project>
""",
            _ => throw new InvalidOperationException($"Unsupported language: {language}")
        };
    }

    private static string GetTemplateSource(
        ScaffoldTemplate template,
        ScaffoldLanguage language,
        string className,
        string commandName)
    {
        return language switch
        {
            ScaffoldLanguage.CSharp => GetCSharpTemplateSource(template, className, commandName),
            ScaffoldLanguage.FSharp => GetFSharpTemplateSource(template, className, commandName),
            ScaffoldLanguage.VisualBasic => GetVisualBasicTemplateSource(template, className, commandName),
            _ => throw new InvalidOperationException($"Unsupported language: {language}")
        };
    }

    private static string GetCSharpTemplateSource(ScaffoldTemplate template, string className, string commandName)
    {
        return template switch
        {
            ScaffoldTemplate.Basic => GetBasicCSharpTemplateSource(className, commandName),
            ScaffoldTemplate.File => GetFileCSharpTemplateSource(className, commandName),
            ScaffoldTemplate.Process => GetProcessCSharpTemplateSource(className, commandName),
            _ => throw new InvalidOperationException($"Unsupported template: {template}")
        };
    }

    private static string GetFSharpTemplateSource(ScaffoldTemplate template, string className, string commandName)
    {
        return template switch
        {
            ScaffoldTemplate.Basic => GetBasicFSharpTemplateSource(className, commandName),
            ScaffoldTemplate.File => GetFileFSharpTemplateSource(className, commandName),
            ScaffoldTemplate.Process => GetProcessFSharpTemplateSource(className, commandName),
            _ => throw new InvalidOperationException($"Unsupported template: {template}")
        };
    }

    private static string GetVisualBasicTemplateSource(ScaffoldTemplate template, string className, string commandName)
    {
        return template switch
        {
            ScaffoldTemplate.Basic => GetBasicVisualBasicTemplateSource(className, commandName),
            ScaffoldTemplate.File => GetFileVisualBasicTemplateSource(className, commandName),
            ScaffoldTemplate.Process => GetProcessVisualBasicTemplateSource(className, commandName),
            _ => throw new InvalidOperationException($"Unsupported template: {template}")
        };
    }

    private static bool TryParseTemplate(string value, out ScaffoldTemplate template)
    {
        switch (value.ToLowerInvariant())
        {
            case "basic":
                template = ScaffoldTemplate.Basic;
                return true;
            case "file":
                template = ScaffoldTemplate.File;
                return true;
            case "process":
                template = ScaffoldTemplate.Process;
                return true;
            default:
                template = ScaffoldTemplate.Basic;
                return false;
        }
    }

    private static bool TryParseLanguage(string value, out ScaffoldLanguage language)
    {
        switch (value.ToLowerInvariant())
        {
            case "csharp":
            case "cs":
            case "c#":
                language = ScaffoldLanguage.CSharp;
                return true;
            case "fsharp":
            case "fs":
            case "f#":
                language = ScaffoldLanguage.FSharp;
                return true;
            case "vb":
            case "vbnet":
            case "visualbasic":
            case "visual-basic":
                language = ScaffoldLanguage.VisualBasic;
                return true;
            default:
                language = ScaffoldLanguage.CSharp;
                return false;
        }
    }

    private static string GetProjectFileExtension(ScaffoldLanguage language)
    {
        return language switch
        {
            ScaffoldLanguage.CSharp => ".csproj",
            ScaffoldLanguage.FSharp => ".fsproj",
            ScaffoldLanguage.VisualBasic => ".vbproj",
            _ => throw new InvalidOperationException($"Unsupported language: {language}")
        };
    }

    private static string GetSourceFileExtension(ScaffoldLanguage language)
    {
        return language switch
        {
            ScaffoldLanguage.CSharp => ".cs",
            ScaffoldLanguage.FSharp => ".fs",
            ScaffoldLanguage.VisualBasic => ".vb",
            _ => throw new InvalidOperationException($"Unsupported language: {language}")
        };
    }

    private static string GetNewUsage()
    {
        return "Usage: command new <repo> <command-name> [--template <basic|file|process>] [--language <csharp|fsharp|vb>]";
    }

    private static string GetRemoveUsage()
    {
        return "Usage: command <remove|delete|rm> <repo> <command-name>";
    }

    private static string GetBasicCSharpTemplateSource(string className, string commandName)
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
        if (args.Count != 0)
        {
            context.WriteErrorLine("Usage: {{commandName}}");
            return Task.FromResult(1);
        }

        // Replace this stub with your command's real behavior.
        context.WriteLine("{{commandName}} command is alive.");
        return Task.FromResult(0);
    }
}
""";
    }

    private static string GetFileCSharpTemplateSource(string className, string commandName)
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
        try
        {
            if (!File.Exists(filePath))
            {
                context.WriteErrorLine($"File not found: {filePath}");
                return 1;
            }

            var contents = await File.ReadAllTextAsync(filePath, cancellationToken);
            context.WriteLine(contents);
            return 0;
        }
        catch (OperationCanceledException)
        {
            context.WriteErrorLine("File read canceled.");
            return 1;
        }
        catch (IOException ex)
        {
            context.WriteErrorLine($"Failed to read file: {ex.Message}");
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            context.WriteErrorLine($"Failed to read file: {ex.Message}");
            return 1;
        }
        catch (ArgumentException ex)
        {
            context.WriteErrorLine($"Invalid file path: {ex.Message}");
            return 1;
        }
    }
}
""";
    }

    private static string GetProcessCSharpTemplateSource(string className, string commandName)
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

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            context.WriteErrorLine("Process execution canceled.");
            return 1;
        }
    }
}
""";
    }

    private static string GetBasicFSharpTemplateSource(string className, string commandName)
    {
        return $$"""
namespace {{className}}

open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open ReaperShell.Abstractions

type {{className}}() =
    interface IShellCommand with
        member _.Name = "{{commandName}}"

        member _.Description = "Generated by ReaperShell."

        member _.ExecuteAsync(
            context: ShellContext,
            args: IReadOnlyList<string>,
            cancellationToken: CancellationToken) =
            context.WriteLine("{{commandName}} command is alive.")
            Task.FromResult(0)
""";
    }

    private static string GetFileFSharpTemplateSource(string className, string commandName)
    {
        return $$"""
namespace {{className}}

open System.Collections.Generic
open System.IO
open System.Threading
open System.Threading.Tasks
open ReaperShell.Abstractions

type {{className}}() =
    interface IShellCommand with
        member _.Name = "{{commandName}}"

        member _.Description = "Generated by ReaperShell. Reads a text file."

        member _.ExecuteAsync(
            context: ShellContext,
            args: IReadOnlyList<string>,
            cancellationToken: CancellationToken) =
            if args.Count <> 1 then
                context.WriteErrorLine("Usage: {{commandName}} <file>")
                Task.FromResult(1)
            else
                let filePath = Path.GetFullPath(args[0], context.WorkingDirectory.FullName)
                try
                    if not (File.Exists(filePath)) then
                        context.WriteErrorLine($"File not found: {filePath}")
                        Task.FromResult(1)
                    else
                        let contents = File.ReadAllText(filePath)
                        context.WriteLine(contents)
                        Task.FromResult(0)
                with ex ->
                    context.WriteErrorLine($"Failed to read file: {ex.Message}")
                    Task.FromResult(1)
""";
    }

    private static string GetProcessFSharpTemplateSource(string className, string commandName)
    {
        return $$"""
namespace {{className}}

open System.Collections.Generic
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open ReaperShell.Abstractions

type {{className}}() =
    interface IShellCommand with
        member _.Name = "{{commandName}}"

        member _.Description = "Generated by ReaperShell. Runs a local process."

        member _.ExecuteAsync(
            context: ShellContext,
            args: IReadOnlyList<string>,
            cancellationToken: CancellationToken) =
            if args.Count = 0 then
                context.WriteErrorLine("Usage: {{commandName}} <executable> [args...]")
                Task.FromResult(1)
            else
                try
                    let startInfo = ProcessStartInfo(
                        FileName = args[0],
                        WorkingDirectory = context.WorkingDirectory.FullName,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true)

                    for argument in args |> Seq.skip 1 do
                        startInfo.ArgumentList.Add(argument)

                    use process = new Process()
                    process.StartInfo <- startInfo
                    if not (process.Start()) then
                        context.WriteErrorLine($"Failed to start process: {args[0]}")
                        Task.FromResult(1)
                    else
                        process.WaitForExit()
                        let stdout = process.StandardOutput.ReadToEnd()
                        let stderr = process.StandardError.ReadToEnd()

                        if not (string.IsNullOrWhiteSpace(stdout)) then
                            context.WriteLine(stdout.TrimEnd())

                        if not (string.IsNullOrWhiteSpace(stderr)) then
                            context.WriteErrorLine(stderr.TrimEnd())

                        Task.FromResult(process.ExitCode)
                with ex ->
                    context.WriteErrorLine($"Failed to start process: {ex.Message}")
                    Task.FromResult(1)
""";
    }

    private static string GetBasicVisualBasicTemplateSource(string className, string commandName)
    {
        return $$"""
Imports System.Collections.Generic
Imports System.Threading
Imports System.Threading.Tasks
Imports ReaperShell.Abstractions

Namespace {{className}}
    Public NotInheritable Class {{className}}
        Implements IShellCommand

        Public ReadOnly Property Name As String Implements IShellCommand.Name
            Get
                Return "{{commandName}}"
            End Get
        End Property

        Public ReadOnly Property Description As String Implements IShellCommand.Description
            Get
                Return "Generated by ReaperShell."
            End Get
        End Property

        Public Function ExecuteAsync(
            context As ShellContext,
            args As IReadOnlyList(Of String),
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer) Implements IShellCommand.ExecuteAsync
            context.WriteLine("{{commandName}} command is alive.")
            Return Task.FromResult(0)
        End Function
    End Class
End Namespace
""";
    }

    private static string GetFileVisualBasicTemplateSource(string className, string commandName)
    {
        return $$"""
Imports System.Collections.Generic
Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports ReaperShell.Abstractions

Namespace {{className}}
    Public NotInheritable Class {{className}}
        Implements IShellCommand

        Public ReadOnly Property Name As String Implements IShellCommand.Name
            Get
                Return "{{commandName}}"
            End Get
        End Property

        Public ReadOnly Property Description As String Implements IShellCommand.Description
            Get
                Return "Generated by ReaperShell. Reads a text file."
            End Get
        End Property

        Public Function ExecuteAsync(
            context As ShellContext,
            args As IReadOnlyList(Of String),
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer) Implements IShellCommand.ExecuteAsync
            If args.Count <> 1 Then
                context.WriteErrorLine("Usage: {{commandName}} <file>")
                Return Task.FromResult(1)
            End If

            Dim filePath = Path.GetFullPath(args(0), context.WorkingDirectory.FullName)
            Try
                If Not File.Exists(filePath) Then
                    context.WriteErrorLine($"File not found: {filePath}")
                    Return Task.FromResult(1)
                End If

                Dim contents = File.ReadAllText(filePath)
                context.WriteLine(contents)
                Return Task.FromResult(0)
            Catch ex As Exception
                context.WriteErrorLine($"Failed to read file: {ex.Message}")
                Return Task.FromResult(1)
            End Try
        End Function
    End Class
End Namespace
""";
    }

    private static string GetProcessVisualBasicTemplateSource(string className, string commandName)
    {
        return $$"""
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Threading
Imports System.Threading.Tasks
Imports ReaperShell.Abstractions

Namespace {{className}}
    Public NotInheritable Class {{className}}
        Implements IShellCommand

        Public ReadOnly Property Name As String Implements IShellCommand.Name
            Get
                Return "{{commandName}}"
            End Get
        End Property

        Public ReadOnly Property Description As String Implements IShellCommand.Description
            Get
                Return "Generated by ReaperShell. Runs a local process."
            End Get
        End Property

        Public Async Function ExecuteAsync(
            context As ShellContext,
            args As IReadOnlyList(Of String),
            Optional cancellationToken As CancellationToken = Nothing) As Task(Of Integer) Implements IShellCommand.ExecuteAsync
            If args.Count = 0 Then
                context.WriteErrorLine("Usage: {{commandName}} <executable> [args...]")
                Return 1
            End If

            Try
                Dim startInfo = New ProcessStartInfo With {
                    .FileName = args(0),
                    .WorkingDirectory = context.WorkingDirectory.FullName,
                    .UseShellExecute = False,
                    .RedirectStandardOutput = True,
                    .RedirectStandardError = True,
                    .CreateNoWindow = True
                }

                For index As Integer = 1 To args.Count - 1
                    startInfo.ArgumentList.Add(args(index))
                Next

                Using process = New Process With {.StartInfo = startInfo}
                    If Not process.Start() Then
                        context.WriteErrorLine($"Failed to start process: {args(0)}")
                        Return 1
                    End If

                    Await process.WaitForExitAsync(cancellationToken)

                    Dim stdout = Await process.StandardOutput.ReadToEndAsync()
                    Dim stderr = Await process.StandardError.ReadToEndAsync()

                    If Not String.IsNullOrWhiteSpace(stdout) Then
                        context.WriteLine(stdout.TrimEnd())
                    End If

                    If Not String.IsNullOrWhiteSpace(stderr) Then
                        context.WriteErrorLine(stderr.TrimEnd())
                    End If

                    Return process.ExitCode
                End Using
            Catch ex As Exception
                context.WriteErrorLine($"Failed to start process: {ex.Message}")
                Return 1
            End Try
        End Function
    End Class
End Namespace
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
        context.WriteErrorLine("Usage: command <templates|list|new|remove|delete|rm> ...");
        return 1;
    }
}

internal enum ScaffoldTemplate
{
    Basic,
    File,
    Process
}

internal enum ScaffoldLanguage
{
    CSharp,
    FSharp,
    VisualBasic
}

internal sealed record NewCommandOptions(
    string RepoName,
    string CommandName,
    ScaffoldTemplate Template,
    ScaffoldLanguage Language);

internal sealed record RepoManifestLoadResult(
    bool Success,
    CommandRepoSettings? Repo,
    CommandPackManifest? Manifest);
