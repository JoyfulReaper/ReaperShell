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
        var host = new ShellHost(parser, registry, lifetime, processRunner, settings, stateDirectory);
        var watchService = new ShellWatchService(host);
        var editorLauncher = new EditorLauncher(settings, processRunner);

        RegisterBuiltIns(
            registry,
            settings,
            processRunner,
            commandPackManager,
            lifetime,
            host,
            watchService,
            editorLauncher,
            workspaceRoot,
            stateDirectory);

        var context = new ShellContext(
            Console.Out,
            Console.Error,
            new DirectoryInfo(workspaceRoot),
            services: null,
            cancellationToken: CancellationToken.None);

        var defaultProfilePath = Path.Combine(stateDirectory, "profile.rsh");
        await EnsureDefaultProfileExistsAsync(defaultProfilePath);
        var shouldRunProfile =
            !options.NoProfile &&
            (options.ProfilePath is not null || options.ScriptPath is null && options.CommandText is null);
        var profilePath = options.ProfilePath is null
            ? defaultProfilePath
            : Path.GetFullPath(options.ProfilePath, workspaceRoot);

        if (options.ScriptPath is not null)
        {
            var scriptPath = Path.GetFullPath(options.ScriptPath, workspaceRoot);
            if (shouldRunProfile &&
                !await TryRunProfileAsync(host, context, profilePath, options.ProfilePath is not null))
            {
                return 1;
            }

            return await host.RunScriptAsync(
                context,
                scriptPath,
                options.ContinueOnError,
                CancellationToken.None);
        }

        if (options.CommandText is not null)
        {
            if (shouldRunProfile &&
                !await TryRunProfileAsync(host, context, profilePath, options.ProfilePath is not null))
            {
                return 1;
            }

            return await host.RunCommandAsync(context, options.CommandText, CancellationToken.None);
        }

        return await host.RunInteractiveAsync(
            context,
            shouldRunProfile ? profilePath : null,
            CancellationToken.None);
    }

    private static void RegisterBuiltIns(
        CommandRegistry registry,
        ShellSettings settings,
        ProcessRunner processRunner,
        CommandPackManager commandPackManager,
        ShellLifetime lifetime,
        ShellHost host,
        ShellWatchService watchService,
        EditorLauncher editorLauncher,
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
        registry.RegisterBuiltIn(new EchoCommand());
        registry.RegisterBuiltIn(new MkdirCommand());
        registry.RegisterBuiltIn(new TouchCommand());
        registry.RegisterBuiltIn(new HeadCommand());
        registry.RegisterBuiltIn(new TailCommand());
        registry.RegisterBuiltIn(new GrepCommand());
        registry.RegisterBuiltIn(new TreeCommand());
        registry.RegisterBuiltIn(new OpenCommand());
        registry.RegisterBuiltIn(new RmCommand());
        registry.RegisterBuiltIn(new CpCommand());
        registry.RegisterBuiltIn(new MvCommand());
        registry.RegisterBuiltIn(new AliasCommand(settings, registry, stateDirectory));
        registry.RegisterBuiltIn(new RitualCommand(host, stateDirectory));
        registry.RegisterBuiltIn(new HookCommand(settings, stateDirectory));
        registry.RegisterBuiltIn(new CommandCommand(settings, workspaceRoot));
        registry.RegisterBuiltIn(new WhichCommand(settings, registry));
        registry.RegisterBuiltIn(new DescribeCommand(settings, registry));
        registry.RegisterBuiltIn(new EditCommand(editorLauncher));
        registry.RegisterBuiltIn(new SourceCommand(settings, registry, editorLauncher, workspaceRoot));
        registry.RegisterBuiltIn(new BannerCommand());
        registry.RegisterBuiltIn(new StatusCommand(settings, registry, commandPackManager, watchService, stateDirectory));
        registry.RegisterBuiltIn(
            new DoctorCommand(
                settings,
                processRunner,
                registry,
                commandPackManager,
                editorLauncher,
                watchService,
                stateDirectory));
        registry.RegisterBuiltIn(new FortuneCommand());
        registry.RegisterBuiltIn(new PrayCommand());
        registry.RegisterBuiltIn(
            new RepoCommand(
                settings,
                processRunner,
                commandPackManager,
                host,
                watchService,
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

    private static async Task EnsureDefaultProfileExistsAsync(string profilePath)
    {
        var profileDirectory = Path.GetDirectoryName(profilePath);
        if (!string.IsNullOrWhiteSpace(profileDirectory))
        {
            Directory.CreateDirectory(profileDirectory);
        }

        if (!File.Exists(profilePath))
        {
            await File.WriteAllTextAsync(
                profilePath,
                """
# ReaperShell startup profile
# Commands here run when the interactive shell starts.
# Example:
# repo list
# plugins
""");
        }

        var stateDirectory = profileDirectory!;
        var ritualsDirectory = Path.Combine(stateDirectory, "rituals");
        Directory.CreateDirectory(ritualsDirectory);

        var awakenPath = Path.Combine(ritualsDirectory, "awaken.rsh");
        if (!File.Exists(awakenPath))
        {
            await File.WriteAllTextAsync(
                awakenPath,
                """
# Ritual: awaken
# Add this to startup with:
# hook add startup awaken
banner
status
pray
""");
        }
    }

    private static async Task<bool> TryRunProfileAsync(
        ShellHost host,
        ShellContext context,
        string profilePath,
        bool explicitProfile)
    {
        if (!explicitProfile && !File.Exists(profilePath))
        {
            return true;
        }

        var exitCode = await host.RunProfileAsync(context, profilePath, CancellationToken.None);
        return exitCode == 0 || !explicitProfile;
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

                case "--no-profile":
                    options.NoProfile = true;
                    break;

                case "--profile":
                    if (!TryReadOptionValue(args, ref index, "--profile", out var profilePath, out errorMessage))
                    {
                        return false;
                    }

                    options.ProfilePath = profilePath;
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
  ReaperShell --command "<command>" [--profile <path>] [--no-profile]
  ReaperShell --script <path> [--continue-on-error] [--state-dir <path>] [--profile <path>] [--no-profile]

Options:
  --command <command>         Execute one command and exit.
  --script <path>             Execute commands from a script file and exit.
  --continue-on-error         Continue running a script after a command fails.
  --no-profile                Disable profile execution.
  --profile <path>            Execute the provided profile instead of <state-dir>/profile.rsh.
  --state-dir <path>          Store settings.json and managed repos under this directory.
  --help, -h                  Show usage information.
""";
    }
}

internal sealed class ProgramOptions
{
    public string? CommandText { get; set; }

    public bool ContinueOnError { get; set; }

    public bool NoProfile { get; set; }

    public string? ProfilePath { get; set; }

    public string? ScriptPath { get; set; }

    public bool ShowHelp { get; set; }

    public string? StateDirectory { get; set; }
}
