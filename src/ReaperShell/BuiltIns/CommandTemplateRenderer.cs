using System.Text;
using ReaperShell.Abstractions;

namespace ReaperShell.BuiltIns;

internal sealed class CommandTemplateRenderer
{
    public string GetProjectFileContents(string workspaceRoot, string commandDirectory, string className, ScaffoldLanguage language)
    {
        var abstractionsProjectPath = Path.Combine(
            workspaceRoot,
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

    public string GetTemplateSource(
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

    public string GetProjectFileExtension(ScaffoldLanguage language)
    {
        return language switch
        {
            ScaffoldLanguage.CSharp => ".csproj",
            ScaffoldLanguage.FSharp => ".fsproj",
            ScaffoldLanguage.VisualBasic => ".vbproj",
            _ => throw new InvalidOperationException($"Unsupported language: {language}")
        };
    }

    public string GetSourceFileExtension(ScaffoldLanguage language)
    {
        return language switch
        {
            ScaffoldLanguage.CSharp => ".cs",
            ScaffoldLanguage.FSharp => ".fs",
            ScaffoldLanguage.VisualBasic => ".vb",
            _ => throw new InvalidOperationException($"Unsupported language: {language}")
        };
    }

    public string ToPascalCase(string commandName)
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
}
