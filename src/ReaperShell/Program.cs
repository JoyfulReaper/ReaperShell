using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!TryParseOptions(args, out var options, out var errorMessage))
        {
            Console.Error.WriteLine(errorMessage);
            Console.Error.WriteLine(GetUsage());
            return 1;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(GetUsage());
            return 0;
        }

        var workspaceRoot = Environment.CurrentDirectory;
        var stateDirectory = Path.GetFullPath(
            options.StateDirectory ?? Path.Combine(workspaceRoot, ".rsh"),
            workspaceRoot);
        var settings = await LoadSettingsAsync(stateDirectory);

        var parser = new CommandParser();
        var registry = new CommandRegistry();
        var processRunner = new ProcessRunner();
        var commandPackManager = new CommandPackManager(registry, processRunner);
        var lifetime = new ShellLifetime();

        RegisterBuiltIns(
            registry,
            settings,
            processRunner,
            commandPackManager,
            lifetime,
            workspaceRoot,
            stateDirectory);

        var context = new ShellContext(
            Console.Out,
            Console.Error,
            new DirectoryInfo(workspaceRoot),
            services: null,
            cancellationToken: CancellationToken.None);

        var host = new ShellHost(parser, registry, lifetime);

        if (options.ScriptPath is not null)
        {
            var scriptPath = Path.GetFullPath(options.ScriptPath, workspaceRoot);
            return await host.RunScriptAsync(
                context,
                scriptPath,
                options.ContinueOnError,
                CancellationToken.None);
        }

        if (options.CommandText is not null)
        {
            return await host.RunCommandAsync(context, options.CommandText, CancellationToken.None);
        }

        return await host.RunInteractiveAsync(context, CancellationToken.None);
    }

    private static void RegisterBuiltIns(
        CommandRegistry registry,
        ShellSettings settings,
        ProcessRunner processRunner,
        CommandPackManager commandPackManager,
        ShellLifetime lifetime,
        string workspaceRoot,
        string stateDirectory)
    {
        registry.RegisterBuiltIn(new HelpCommand(registry));
        registry.RegisterBuiltIn(new ClearCommand());
        registry.RegisterBuiltIn(new ExitCommand("exit", lifetime));
        registry.RegisterBuiltIn(new ExitCommand("quit", lifetime));
        registry.RegisterBuiltIn(new PwdCommand());
        registry.RegisterBuiltIn(new LsCommand());
        registry.RegisterBuiltIn(new CdCommand());
        registry.RegisterBuiltIn(new CatCommand());
        registry.RegisterBuiltIn(
            new RepoCommand(
                settings,
                processRunner,
                commandPackManager,
                workspaceRoot,
                stateDirectory));
        registry.RegisterBuiltIn(new PluginsCommand(commandPackManager));
    }

    private static async Task<ShellSettings> LoadSettingsAsync(string stateDirectory)
    {
        try
        {
            return await ShellSettings.LoadOrCreateAsync(stateDirectory, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load settings: {ex.Message}");
            return new ShellSettings();
        }
    }

    private static bool TryParseOptions(
        IReadOnlyList<string> args,
        out ProgramOptions options,
        out string? errorMessage)
    {
        options = new ProgramOptions();
        errorMessage = null;

        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--script":
                    if (!TryReadOptionValue(args, ref index, "--script", out var scriptPath, out errorMessage))
                    {
                        return false;
                    }

                    options.ScriptPath = scriptPath;
                    break;

                case "--command":
                    if (!TryReadOptionValue(args, ref index, "--command", out var commandText, out errorMessage))
                    {
                        return false;
                    }

                    options.CommandText = commandText;
                    break;

                case "--state-dir":
                    if (!TryReadOptionValue(args, ref index, "--state-dir", out var stateDirectory, out errorMessage))
                    {
                        return false;
                    }

                    options.StateDirectory = stateDirectory;
                    break;

                case "--continue-on-error":
                    options.ContinueOnError = true;
                    break;

                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;

                default:
                    errorMessage = $"Unknown argument: {args[index]}";
                    return false;
            }
        }

        if (options.ScriptPath is not null && options.CommandText is not null)
        {
            errorMessage = "Choose either --script or --command, not both.";
            return false;
        }

        return true;
    }

    private static bool TryReadOptionValue(
        IReadOnlyList<string> args,
        ref int index,
        string optionName,
        out string value,
        out string? errorMessage)
    {
        value = string.Empty;
        errorMessage = null;

        if (index + 1 >= args.Count)
        {
            errorMessage = $"Missing value for {optionName}.";
            return false;
        }

        value = args[++index];
        return true;
    }

    private static string GetUsage()
    {
        return """
Usage:
  ReaperShell
  ReaperShell --command "<command>"
  ReaperShell --script <path> [--continue-on-error] [--state-dir <path>]

Options:
  --command <command>         Execute one command and exit.
  --script <path>             Execute commands from a script file and exit.
  --continue-on-error         Continue running a script after a command fails.
  --state-dir <path>          Store settings.json and managed repos under this directory.
  --help, -h                  Show usage information.
""";
    }
}

internal sealed class ProgramOptions
{
    public string? CommandText { get; set; }

    public bool ContinueOnError { get; set; }

    public string? ScriptPath { get; set; }

    public bool ShowHelp { get; set; }

    public string? StateDirectory { get; set; }
}
