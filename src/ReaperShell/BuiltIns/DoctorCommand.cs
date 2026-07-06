using ReaperShell.Abstractions;
using ReaperShell.Plugins;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

public sealed class DoctorCommand : IShellCommand
{
    private readonly CommandPackManager _commandPackManager;
    private readonly CommandParser _commandParser = new();
    private readonly CommandRegistry _commandRegistry;
    private readonly EditorLauncher _editorLauncher;
    private readonly ProcessRunner _processRunner;
    private readonly ShellSettings _settings;
    private readonly string _stateDirectory;
    private readonly ShellWatchService _watchService;

    public DoctorCommand(
        ShellSettings settings,
        ProcessRunner processRunner,
        CommandRegistry commandRegistry,
        CommandPackManager commandPackManager,
        EditorLauncher editorLauncher,
        ShellWatchService watchService,
        string stateDirectory)
    {
        _settings = settings;
        _processRunner = processRunner;
        _commandRegistry = commandRegistry;
        _commandPackManager = commandPackManager;
        _editorLauncher = editorLauncher;
        _watchService = watchService;
        _stateDirectory = stateDirectory;
    }

    public string Name => "doctor";

    public string Description => "Runs a focused ReaperShell environment self-check.";

    public async Task<int> ExecuteAsync(
        ShellContext context,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (args.Count > 1 || args.Count == 1 && !string.Equals(args[0], "--verbose", StringComparison.OrdinalIgnoreCase))
        {
            context.WriteErrorLine("Usage: doctor [--verbose]");
            return 1;
        }

        var verbose = args.Count == 1;
        var report = new DoctorReport(context, verbose);

        report.Section("State");
        await CheckStateAsync(report, context, cancellationToken);

        report.Section("Tools");
        await CheckToolsAsync(report, context, cancellationToken);

        report.Section("Repos");
        await CheckReposAsync(report, context, cancellationToken);

        report.Section("Loaded Packs");
        CheckLoadedPacks(report);

        report.Section("Commands");
        CheckCommands(report);

        report.Section("Hooks");
        CheckHooks(report);

        report.Section("Watchers");
        CheckWatchers(report);

        report.Section("Summary");
        report.Info($"FAIL: {report.FailCount}");
        report.Info($"WARN: {report.WarnCount}");
        report.Info($"OK: {report.OkCount}");

        return report.FailCount == 0 ? 0 : 1;
    }

    private async Task CheckStateAsync(
        DoctorReport report,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        var fullStateDirectory = Path.GetFullPath(_stateDirectory);
        if (Directory.Exists(fullStateDirectory))
        {
            report.Ok("State directory exists.", fullStateDirectory);
        }
        else
        {
            report.Fail("State directory is missing.", fullStateDirectory);
        }

        var settingsPath = ShellSettings.GetSettingsPath(_stateDirectory);
        if (!File.Exists(settingsPath))
        {
            report.Fail("settings.json is missing.", settingsPath);
        }
        else
        {
            try
            {
                await ShellSettings.LoadOrCreateAsync(_stateDirectory, cancellationToken);
                report.Ok("settings.json exists and can be loaded.", settingsPath);
            }
            catch (Exception ex)
            {
                report.Fail("settings.json could not be loaded.", $"{settingsPath}\n{ex.Message}");
            }
        }

        var ritualsDirectory = Path.Combine(_stateDirectory, "rituals");
        if (Directory.Exists(ritualsDirectory))
        {
            report.Ok("Rituals directory exists.", ritualsDirectory);
        }
        else
        {
            report.Fail("Rituals directory is missing.", ritualsDirectory);
        }

        var profilePath = Path.Combine(_stateDirectory, "profile.rsh");
        if (File.Exists(profilePath))
        {
            report.Ok("profile.rsh exists.", profilePath);
        }
        else
        {
            report.Fail("profile.rsh is missing.", profilePath);
        }
    }

    private async Task CheckToolsAsync(
        DoctorReport report,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        await CheckExecutableAsync(
            report,
            context,
            "git",
            ["--version"],
            "git executable is available.",
            "git executable is unavailable.",
            cancellationToken);

        await CheckExecutableAsync(
            report,
            context,
            "dotnet",
            ["--version"],
            "dotnet executable is available.",
            "dotnet executable is unavailable.",
            cancellationToken);

        await CheckExecutableAsync(
            report,
            context,
            "gh",
            ["--version"],
            "gh executable is available.",
            "gh executable is unavailable.",
            cancellationToken,
            warnOnFailure: true);

        var editorCommand = await _editorLauncher.ResolveEditorCommandAsync(context, cancellationToken);
        if (editorCommand is not null)
        {
            var editorTokens = _commandParser.Parse(editorCommand);
            if (editorTokens.Count == 0)
            {
                report.Fail("The configured editor command is empty.", editorCommand);
                return;
            }

            await CheckPathAvailabilityAsync(
                report,
                context,
                editorTokens[0],
                "Editor command appears usable.",
                "Editor command could not be found on PATH.",
                cancellationToken,
                detailsPrefix: $"Command: {editorCommand}");
            return;
        }

        report.Warn("No editor command is configured, and the default 'code' fallback was not found.");
    }

    private async Task CheckReposAsync(
        DoctorReport report,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        if (_settings.Repos.Count == 0)
        {
            report.Ok("No repos are registered.");
            return;
        }

        foreach (var repo in _settings.Repos.Values.OrderBy(repo => repo.Name, StringComparer.OrdinalIgnoreCase))
        {
            var details = new List<string>
            {
                $"Name: {repo.Name}",
                $"Path: {repo.LocalPath}",
                $"Trust: {(repo.Trusted ? "trusted" : "untrusted")}",
                $"Kind: {(repo.IsGitRepo ? "git" : "local")}"
            };

            var failures = new List<string>();
            var warnings = new List<string>();

            if (!Directory.Exists(repo.LocalPath))
            {
                failures.Add("Local path is missing.");
            }

            var manifestPath = Path.Combine(repo.LocalPath, "shellpack.json");
            if (Directory.Exists(repo.LocalPath) && !File.Exists(manifestPath))
            {
                failures.Add("shellpack.json is missing.");
            }
            else if (File.Exists(manifestPath))
            {
                try
                {
                    var manifest = await CommandPackManifest.LoadAsync(manifestPath, cancellationToken);
                    var commandsRoot = CommandPackPathResolver.ResolveCommandsRoot(repo.LocalPath, manifest.CommandsPath);
                    if (report.Verbose)
                    {
                        details.Add($"Commands root: {commandsRoot}");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"commandsPath is invalid: {ex.Message}");
                }
            }

            if (repo.IsGitRepo && Directory.Exists(repo.LocalPath))
            {
                try
                {
                    var gitStatus = await _processRunner.RunAsync(
                        "git",
                        ["status", "--short"],
                        repo.LocalPath,
                        cancellationToken: cancellationToken);

                    if (gitStatus.ExitCode == 0)
                    {
                        if (report.Verbose && !string.IsNullOrWhiteSpace(gitStatus.StandardOutput))
                        {
                            details.Add("git status --short:");
                            details.Add(gitStatus.StandardOutput.TrimEnd());
                        }
                    }
                    else
                    {
                        failures.Add("git status failed.");
                        if (report.Verbose)
                        {
                            AppendCommandOutput(details, gitStatus);
                        }
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"git status failed: {ex.Message}");
                }
            }

            if (failures.Count > 0)
            {
                report.Fail(
                    $"Repo '{repo.Name}' has failing checks: {string.Join(" ", failures)}",
                    string.Join(Environment.NewLine, details));
            }
            else if (warnings.Count > 0)
            {
                report.Warn(
                    $"Repo '{repo.Name}' has warnings: {string.Join(" ", warnings)}",
                    string.Join(Environment.NewLine, details));
            }
            else
            {
                report.Ok($"Repo '{repo.Name}' looks healthy.", string.Join(Environment.NewLine, details));
            }
        }
    }

    private void CheckLoadedPacks(DoctorReport report)
    {
        if (_commandPackManager.LoadedPacks.Count == 0)
        {
            report.Ok("No command packs are loaded.");
            return;
        }

        foreach (var pack in _commandPackManager.LoadedPacks.OrderBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase))
        {
            var details = report.Verbose
                ? $"Path: {pack.Path}{Environment.NewLine}Commands: {string.Join(", ", pack.RegisteredCommandNames)}"
                : $"Commands: {string.Join(", ", pack.RegisteredCommandNames)}";

            report.Ok($"Loaded pack '{pack.Name}'.", details);
        }
    }

    private void CheckCommands(DoctorReport report)
    {
        report.Ok($"Registered command count: {_commandRegistry.GetCommandCount()}");
        report.Ok($"Alias count: {_settings.Aliases.Count}");
        report.Ok($"External command mode: {_settings.ExternalCommandMode}");

        var aliasCycles = FindAliasCycles();
        if (aliasCycles.Count == 0)
        {
            report.Ok("No alias recursion risks detected.");
            return;
        }

        foreach (var aliasCycle in aliasCycles)
        {
            report.Warn($"Alias recursion risk detected: {aliasCycle}");
        }
    }

    private void CheckHooks(DoctorReport report)
    {
        if (_settings.Hooks.Count == 0)
        {
            report.Ok("No hooks are configured.");
            return;
        }

        foreach (var hook in _settings.Hooks.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var rituals = hook.Value ?? [];
            var missingRituals = rituals
                .Where(ritual => !File.Exists(Path.Combine(_stateDirectory, "rituals", ritual + ".rsh")))
                .ToArray();

            var details = $"Rituals: {string.Join(", ", rituals)}";
            if (missingRituals.Length > 0)
            {
                report.Warn(
                    $"Hook '{hook.Key}' references missing rituals: {string.Join(", ", missingRituals)}",
                    details);
            }
            else
            {
                report.Ok($"Hook '{hook.Key}' is configured.", details);
            }
        }
    }

    private void CheckWatchers(DoctorReport report)
    {
        var watchedRepos = _watchService.GetWatchedRepoNames();
        if (watchedRepos.Count == 0)
        {
            report.Ok("No repos are being watched.");
            return;
        }

        foreach (var watchedRepo in watchedRepos)
        {
            if (!_settings.Repos.TryGetValue(watchedRepo, out var repo))
            {
                report.Warn($"Watched repo '{watchedRepo}' is no longer registered.");
                continue;
            }

            var warnings = new List<string>();
            if (!repo.Trusted)
            {
                warnings.Add("repo is not trusted");
            }

            if (!Directory.Exists(repo.LocalPath))
            {
                warnings.Add("repo path is missing");
            }

            if (warnings.Count > 0)
            {
                report.Warn(
                    $"Watched repo '{watchedRepo}' has warnings: {string.Join(", ", warnings)}",
                    repo.LocalPath);
            }
            else
            {
                report.Ok($"Watched repo '{watchedRepo}' is healthy.", repo.LocalPath);
            }
        }
    }

    private async Task CheckExecutableAsync(
        DoctorReport report,
        ShellContext context,
        string executable,
        IReadOnlyList<string> arguments,
        string successMessage,
        string failureMessage,
        CancellationToken cancellationToken,
        string? detailsPrefix = null,
        bool warnOnFailure = false)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                executable,
                arguments,
                context.WorkingDirectory.FullName,
                cancellationToken: cancellationToken);

            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(detailsPrefix))
            {
                details.Add(detailsPrefix);
            }

            if (report.Verbose)
            {
                AppendCommandOutput(details, result);
            }

            if (result.ExitCode == 0)
            {
                report.Ok(successMessage, JoinDetails(details));
            }
            else
            {
                if (warnOnFailure)
                {
                    report.Warn(failureMessage, JoinDetails(details));
                }
                else
                {
                    report.Fail(failureMessage, JoinDetails(details));
                }
            }
        }
        catch (Exception ex)
        {
            var details = string.IsNullOrWhiteSpace(detailsPrefix)
                ? ex.Message
                : $"{detailsPrefix}{Environment.NewLine}{ex.Message}";
            if (warnOnFailure)
            {
                report.Warn(failureMessage, details);
            }
            else
            {
                report.Fail(failureMessage, details);
            }
        }
    }

    private async Task CheckPathAvailabilityAsync(
        DoctorReport report,
        ShellContext context,
        string executable,
        string successMessage,
        string failureMessage,
        CancellationToken cancellationToken,
        string? detailsPrefix = null)
    {
        try
        {
            var available = await IsExecutableOnPathAsync(executable, context, cancellationToken);
            if (available)
            {
                report.Ok(successMessage, detailsPrefix);
            }
            else
            {
                report.Fail(failureMessage, detailsPrefix);
            }
        }
        catch (Exception ex)
        {
            var details = string.IsNullOrWhiteSpace(detailsPrefix)
                ? ex.Message
                : $"{detailsPrefix}{Environment.NewLine}{ex.Message}";
            report.Fail(failureMessage, details);
        }
    }

    private List<string> FindAliasCycles()
    {
        var cycles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in _settings.Aliases.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            var chain = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = alias;

            while (_settings.Aliases.TryGetValue(current, out var replacement))
            {
                if (!seen.Add(current))
                {
                    var cycleStart = chain.FindIndex(name => string.Equals(name, current, StringComparison.OrdinalIgnoreCase));
                    var cycle = cycleStart >= 0 ? chain.Skip(cycleStart).Append(current) : [current, current];
                    cycles.Add(string.Join(" -> ", cycle));
                    break;
                }

                chain.Add(current);
                var tokens = _commandParser.Parse(replacement);
                if (tokens.Count == 0)
                {
                    break;
                }

                current = tokens[0];
            }
        }

        return cycles.OrderBy(cycle => cycle, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<bool> IsExecutableOnPathAsync(
        string executable,
        ShellContext context,
        CancellationToken cancellationToken)
    {
        if (Path.IsPathRooted(executable))
        {
            return File.Exists(executable);
        }

        var probeExecutable = OperatingSystem.IsWindows() ? "where" : "which";
        var result = await _processRunner.RunAsync(
            probeExecutable,
            [executable],
            context.WorkingDirectory.FullName,
            cancellationToken: cancellationToken);
        return result.ExitCode == 0;
    }

    private static void AppendCommandOutput(List<string> details, ProcessRunResult result)
    {
        details.Add($"ExitCode: {result.ExitCode}");
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            details.Add("stdout:");
            details.Add(result.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            details.Add("stderr:");
            details.Add(result.StandardError.TrimEnd());
        }
    }

    private static string? JoinDetails(List<string> details)
    {
        return details.Count == 0 ? null : string.Join(Environment.NewLine, details);
    }
}

internal sealed class DoctorReport
{
    private readonly ShellContext _context;

    public DoctorReport(ShellContext context, bool verbose)
    {
        _context = context;
        Verbose = verbose;
    }

    public int FailCount { get; private set; }

    public int OkCount { get; private set; }

    public bool Verbose { get; }

    public int WarnCount { get; private set; }

    public void Section(string title)
    {
        _context.WriteLine(string.Empty);
        _context.WriteLine($"== {title} ==");
    }

    public void Ok(string message, string? details = null)
    {
        OkCount++;
        Write("OK", message, details);
    }

    public void Warn(string message, string? details = null)
    {
        WarnCount++;
        Write("WARN", message, details);
    }

    public void Fail(string message, string? details = null)
    {
        FailCount++;
        Write("FAIL", message, details);
    }

    public void Info(string message)
    {
        _context.WriteLine(message);
    }

    private void Write(string label, string message, string? details)
    {
        _context.WriteLine($"{label}: {message}");
        if (Verbose && !string.IsNullOrWhiteSpace(details))
        {
            foreach (var line in details.Split(Environment.NewLine))
            {
                _context.WriteLine($"  {line}");
            }
        }
    }
}
