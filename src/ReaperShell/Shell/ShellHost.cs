using System.Threading.Channels;
using ReaperShell.Abstractions;

namespace ReaperShell.Shell;

public sealed class ShellHost
{
    private const int AliasExpansionLimit = 8;

    private readonly CommandParser _commandParser;
    private readonly CommandRegistry _commandRegistry;
    private readonly AsyncLocal<int> _commandExecutionDepth = new();
    private readonly SemaphoreSlim _commandExecutionLock = new(1, 1);
    private readonly Channel<InteractiveWorkItem> _interactiveWorkItems = Channel.CreateUnbounded<InteractiveWorkItem>();
    private readonly object _hookGate = new();
    private readonly ShellLifetime _lifetime;
    private readonly HashSet<string> _activeHookEvents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ProcessRunner _processRunner;
    private readonly string _stateDirectory;
    private readonly SemaphoreSlim _interactivePromptReady = new(0, 1);
    private readonly ShellCurseState _curseState;
    private readonly ShellSessionState _sessionState;
    private readonly ShellSettings _settings;
    private string? _currentProfilePath;

    public ShellHost(
        CommandParser commandParser,
        CommandRegistry commandRegistry,
        ShellLifetime lifetime,
        ProcessRunner processRunner,
        ShellSettings settings,
        string stateDirectory,
        ShellSessionState? sessionState = null,
        ShellCurseState? curseState = null)
    {
        _commandParser = commandParser;
        _commandRegistry = commandRegistry;
        _lifetime = lifetime;
        _processRunner = processRunner;
        _settings = settings;
        _stateDirectory = stateDirectory;
        _sessionState = sessionState ?? new ShellSessionState();
        _curseState = curseState ?? new ShellCurseState();
    }

    public bool IsInteractiveModeEnabled { get; private set; }

    public async Task<int> RunInteractiveAsync(
        ShellContext context,
        string? profilePath,
        CancellationToken cancellationToken)
    {
        IsInteractiveModeEnabled = true;

        try
        {
            ShellBanner.Write(context);

            if (!string.IsNullOrWhiteSpace(profilePath))
            {
                _currentProfilePath = profilePath;
                await RunProfileAsync(context, profilePath, cancellationToken);
            }

            await RunHookEventAsync(context, ShellHookEventNames.Startup, cancellationToken);

            SignalInteractivePromptReady();

            _ = Task.Run(
                () => ReadInteractiveInputAsync(context, _interactiveWorkItems.Writer, cancellationToken),
                CancellationToken.None);

            while (!_lifetime.ExitRequested && !cancellationToken.IsCancellationRequested)
            {
                var workItem = await _interactiveWorkItems.Reader.ReadAsync(cancellationToken);

                if (workItem.QueuedCommand is not null)
                {
                    var queuedCommand = workItem.QueuedCommand;
                    var exitCode = await ExecuteCommandAsync(
                        queuedCommand.Context,
                        queuedCommand.CommandText,
                        new CommandExecutionOptions(EchoCommand: queuedCommand.EchoCommand, TriggerCommandHooks: false, AllowCurse: false, AllowAmbient: false),
                        queuedCommand.CancellationToken);
                    queuedCommand.Completion.TrySetResult(exitCode);
                    continue;
                }

                var input = workItem.UserInput;
                if (input is null)
                {
                    break;
                }

                await ExecuteCommandAsync(
                    context,
                    input,
                    new CommandExecutionOptions(EchoCommand: false, TriggerCommandHooks: true, AllowCurse: true, AllowAmbient: true),
                    cancellationToken);
                SignalInteractivePromptReady();
            }

            await RunHookEventAsync(context, ShellHookEventNames.ShellExit, cancellationToken);
            return 0;
        }
        finally
        {
            IsInteractiveModeEnabled = false;
        }
    }

    public async Task<int> RunScriptAsync(
        ShellContext context,
        string scriptPath,
        bool continueOnError,
        CancellationToken cancellationToken)
    {
        ShellBanner.Write(context);
        return await ExecuteScriptFileAsync(
            context,
            scriptPath,
            continueOnError,
            new CommandExecutionOptions(EchoCommand: true, TriggerCommandHooks: true, RecordHistory: false, AllowCurse: false, AllowAmbient: false),
            cancellationToken);
    }

    public async Task<int> RunCommandAsync(
        ShellContext context,
        string commandText,
        CancellationToken cancellationToken)
    {
        ShellBanner.Write(context);
        return await ExecuteCommandAsync(
            context,
            commandText,
            new CommandExecutionOptions(EchoCommand: false, TriggerCommandHooks: true, AllowCurse: true, AllowAmbient: true),
            cancellationToken);
    }

    public async Task<int> RunProfileAsync(
        ShellContext context,
        string profilePath,
        CancellationToken cancellationToken)
    {
        _currentProfilePath = profilePath;
        if (!File.Exists(profilePath))
        {
            context.WriteErrorLine($"Profile file does not exist: {profilePath}");
            return 1;
        }

        context.WriteLine($"Running profile: {profilePath}");
        return await ExecuteScriptFileAsync(
            context,
            profilePath,
            continueOnError: true,
            new CommandExecutionOptions(EchoCommand: true, TriggerCommandHooks: false, RecordHistory: false, AllowCurse: false, AllowAmbient: false),
            cancellationToken);
    }

    public async Task<int> RunRitualAsync(
        ShellContext context,
        string ritualPath,
        bool continueOnError,
        CancellationToken cancellationToken)
    {
        return await ExecuteScriptFileAsync(
            context,
            ritualPath,
            continueOnError,
            new CommandExecutionOptions(EchoCommand: true, TriggerCommandHooks: false, RecordHistory: false, AllowCurse: false, AllowAmbient: false),
            cancellationToken);
    }

    public async Task<int> RunAutomationCommandAsync(
        ShellContext context,
        string commandText,
        bool echoCommand,
        CancellationToken cancellationToken)
    {
        return await ExecuteCommandAsync(
            context,
            commandText,
            new CommandExecutionOptions(EchoCommand: echoCommand, TriggerCommandHooks: false, RecordHistory: false, AllowCurse: false, AllowAmbient: false),
            cancellationToken);
    }

    public Task<int> QueueInteractiveCommandAsync(
        ShellContext context,
        string commandText,
        bool echoCommand,
        CancellationToken cancellationToken)
    {
        if (!IsInteractiveModeEnabled)
        {
            return RunAutomationCommandAsync(context, commandText, echoCommand, cancellationToken);
        }

        var queuedCommand = new QueuedInteractiveCommand(
            context,
            commandText,
            echoCommand,
            cancellationToken,
            new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously));

        if (!_interactiveWorkItems.Writer.TryWrite(new InteractiveWorkItem(null, queuedCommand)))
        {
            queuedCommand.Completion.TrySetResult(1);
        }

        return queuedCommand.Completion.Task;
    }

    public async Task RunHookEventAsync(
        ShellContext context,
        string eventName,
        CancellationToken cancellationToken)
    {
        if (!_settings.Hooks.TryGetValue(eventName, out var rituals) || rituals.Count == 0)
        {
            return;
        }

        lock (_hookGate)
        {
            if (!_activeHookEvents.Add(eventName))
            {
                return;
            }
        }

        try
        {
            foreach (var ritualName in rituals.ToArray())
            {
                var ritualPath = Path.Combine(_stateDirectory, "rituals", ritualName + ".rsh");
                if (!File.Exists(ritualPath))
                {
                    context.WriteErrorLine($"Hook ritual '{ritualName}' for event '{eventName}' does not exist.");
                    continue;
                }

                var exitCode = await ExecuteScriptFileAsync(
                    context,
                    ritualPath,
                    continueOnError: true,
                    new CommandExecutionOptions(EchoCommand: false, TriggerCommandHooks: false, RecordHistory: false, AllowCurse: false, AllowAmbient: false),
                    cancellationToken);

                if (exitCode != 0)
                {
                    context.WriteErrorLine(
                        $"Hook ritual '{ritualName}' for event '{eventName}' completed with errors.");
                }
            }
        }
        finally
        {
            lock (_hookGate)
            {
                _activeHookEvents.Remove(eventName);
            }
        }
    }

    private async Task<int> ExecuteCommandAsync(
        ShellContext context,
        string input,
        CommandExecutionOptions options,
        CancellationToken cancellationToken)
    {
        var ownsExecutionLock = false;
        if (_commandExecutionDepth.Value == 0)
        {
            await _commandExecutionLock.WaitAsync(cancellationToken);
            ownsExecutionLock = true;
        }

        _commandExecutionDepth.Value++;

        try
        {
            if (options.EchoCommand)
            {
                context.WriteLine($"{FormatPrompt(context)}{input}");
            }

            var tokens = _commandParser.Parse(input);
            if (tokens.Count == 0)
            {
                return 0;
            }

            if (!TryResolveCommandTokens(tokens, options.EchoCommand, context, out var resolvedTokens))
            {
                return 1;
            }

            if (options.RecordHistory && !IsHistoryCommand(resolvedTokens))
            {
                _sessionState.RecordHistory(input);
            }

            var curseDecision = _curseState.EvaluateBeforeCommand(resolvedTokens[0], options.AllowCurse);
            if (curseDecision.Message is not null)
            {
                if (curseDecision.BlockCommand)
                {
                    context.WriteErrorLine(curseDecision.Message);
                    return 1;
                }

                context.WriteLine(curseDecision.Message);
            }

            if (options.TriggerCommandHooks)
            {
                await RunHookEventAsync(context, ShellHookEventNames.BeforeCommand, cancellationToken);
            }

            var exitCode = 1;
            try
            {
                if (!_commandRegistry.TryGet(resolvedTokens[0], out var command))
                {
                    exitCode = await TryRunExternalCommandAsync(
                        context,
                        resolvedTokens,
                        cancellationToken);
                }
                else
                {
                    exitCode = await command.ExecuteAsync(context, resolvedTokens.Skip(1).ToArray(), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                context.WriteErrorLine("Command canceled.");
            }
            catch (Exception ex)
            {
                context.WriteErrorLine($"Command failed: {ex.Message}");
            }
            finally
            {
                if (options.TriggerCommandHooks)
                {
                    await RunHookEventAsync(context, ShellHookEventNames.AfterCommand, cancellationToken);
                }
            }

            MaybeEmitAmbientMessage(context, resolvedTokens[0], exitCode, options);
            return exitCode;
        }
        finally
        {
            _commandExecutionDepth.Value--;
            if (ownsExecutionLock)
            {
                _commandExecutionLock.Release();
            }
        }
    }

    private async Task<int> ExecuteScriptFileAsync(
        ShellContext context,
        string scriptPath,
        bool continueOnError,
        CommandExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath))
        {
            context.WriteErrorLine($"Script file does not exist: {scriptPath}");
            return 1;
        }

        var exitCode = 0;
        foreach (var line in await File.ReadAllLinesAsync(scriptPath, cancellationToken))
        {
            var commandText = line.Trim();
            if (string.IsNullOrWhiteSpace(commandText) || commandText.StartsWith('#'))
            {
                continue;
            }

            var commandExitCode = await ExecuteCommandAsync(
                context,
                commandText,
                options,
                cancellationToken);

            if (commandExitCode != 0)
            {
                exitCode = commandExitCode;
                if (!continueOnError)
                {
                    return exitCode;
                }
            }

            if (_lifetime.ExitRequested || cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return exitCode;
    }

    private bool TryResolveCommandTokens(
        IReadOnlyList<string> tokens,
        bool echoExpandedCommand,
        ShellContext context,
        out IReadOnlyList<string> resolvedTokens)
    {
        resolvedTokens = tokens.ToArray();
        var expansions = 0;

        while (_settings.Aliases.TryGetValue(resolvedTokens[0], out var replacement))
        {
            expansions++;
            if (expansions > AliasExpansionLimit)
            {
                context.WriteErrorLine($"Alias expansion limit exceeded for '{tokens[0]}'.");
                return false;
            }

            var replacementTokens = _commandParser.Parse(replacement);
            if (replacementTokens.Count == 0)
            {
                context.WriteErrorLine($"Alias '{resolvedTokens[0]}' expands to an empty command.");
                return false;
            }

            resolvedTokens = replacementTokens
                .Concat(resolvedTokens.Skip(1))
                .ToArray();

            if (echoExpandedCommand)
            {
                context.WriteLine($"-> {string.Join(" ", resolvedTokens)}");
            }
        }

        return true;
    }

    private async Task<int> TryRunExternalCommandAsync(
        ShellContext context,
        IReadOnlyList<string> resolvedTokens,
        CancellationToken cancellationToken)
    {
        var commandName = resolvedTokens[0];
        switch (_settings.ExternalCommandMode)
        {
            case ExternalCommandMode.Disabled:
                WriteUnknownCommand(context, commandName, "External command fallback is disabled.");
                return 1;

            case ExternalCommandMode.Shell:
                WriteUnknownCommand(context, commandName, "External command mode 'Shell' is not implemented.");
                return 1;

            case ExternalCommandMode.PathOnly:
                if (!ExternalCommandResolver.TryResolveExecutable(commandName, out var executablePath))
                {
                    WriteUnknownCommand(context, commandName, $"No executable named '{commandName}' was found on PATH.");
                    return 1;
                }

                try
                {
                    return (await _processRunner.RunAsync(
                        executablePath,
                        resolvedTokens.Skip(1).ToArray(),
                        context.WorkingDirectory.FullName,
                        context.WriteLine,
                        context.WriteErrorLine,
                        cancellationToken: cancellationToken)).ExitCode;
                }
                catch (OperationCanceledException)
                {
                    context.WriteErrorLine("Command canceled.");
                    return 1;
                }
                catch (Exception ex)
                {
                    context.WriteErrorLine($"External command failed: {ex.Message}");
                    return 1;
                }

            default:
                WriteUnknownCommand(context, commandName, "External command fallback is unavailable.");
                return 1;
        }
    }

    private static void WriteUnknownCommand(ShellContext context, string commandName, string hint)
    {
        context.WriteErrorLine($"Unknown command: {commandName}");
        context.WriteErrorLine(hint);
    }

    private void MaybeEmitAmbientMessage(
        ShellContext context,
        string commandName,
        int exitCode,
        CommandExecutionOptions options)
    {
        if (!options.AllowAmbient)
        {
            return;
        }

        var ambientMessage = _curseState.TryGetAmbientMessage(commandName, exitCode);
        if (ambientMessage is null)
        {
            return;
        }

        _curseState.AddAmbientEvent(ambientMessage);
        context.WriteLine(ambientMessage);
    }

    public async Task<int> ReloadAsync(ShellContext context, CancellationToken cancellationToken)
    {
        try
        {
            var reloadedSettings = await ShellSettings.LoadOrCreateAsync(_stateDirectory, cancellationToken);
            _settings.ApplyFrom(reloadedSettings.Normalize());
            context.WriteLine("Reloaded settings.");
        }
        catch (Exception ex)
        {
            context.WriteErrorLine($"Failed to reload settings: {ex.Message}");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(_currentProfilePath))
        {
            context.WriteLine("No active profile to reload.");
            return 0;
        }

        if (!File.Exists(_currentProfilePath))
        {
            context.WriteErrorLine($"Profile file does not exist: {_currentProfilePath}");
            return 1;
        }

        context.WriteLine($"Reloading profile: {_currentProfilePath}");
        return await RunProfileAsync(context, _currentProfilePath, cancellationToken);
    }

    private static bool IsHistoryCommand(IReadOnlyList<string> resolvedTokens)
    {
        return resolvedTokens.Count > 0 &&
            string.Equals(resolvedTokens[0], "history", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ReadInteractiveInputAsync(
        ShellContext context,
        ChannelWriter<InteractiveWorkItem> writer,
        CancellationToken cancellationToken)
    {
        var lineReader = new InteractiveLineReader();
        try
        {
            while (true)
            {
                await _interactivePromptReady.WaitAsync(cancellationToken);
                var prompt = FormatPrompt(context);
                var input = await lineReader.ReadLineAsync(
                    prompt,
                    () => context.WorkingDirectory,
                    () => _sessionState.GetHistory(),
                    () => _commandRegistry.GetAllCommands(),
                    () => _settings.Aliases.Keys.ToArray(),
                    cancellationToken);
                await writer.WriteAsync(new InteractiveWorkItem(input, null), cancellationToken);
                if (input is null)
                {
                    break;
                }
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private void SignalInteractivePromptReady()
    {
        if (_interactivePromptReady.CurrentCount == 0)
        {
            _interactivePromptReady.Release();
        }
    }

    internal string FormatPrompt(ShellContext context)
    {
        var prompt = _settings.ShowPathInPrompt
            ? FormatPrompt(context.WorkingDirectory.FullName)
            : "rsh> ";

        if (_curseState.Enabled)
        {
            return "☠ " + prompt;
        }

        return prompt;
    }

    internal static string FormatPrompt(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return "rsh> ";
        }

        var displayPath = AbbreviateHomeDirectory(workingDirectory);
        return displayPath + "> ";
    }

    private static string AbbreviateHomeDirectory(string workingDirectory)
    {
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            return workingDirectory;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(workingDirectory, homeDirectory, comparison))
        {
            return "~";
        }

        var normalizedHome = homeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!workingDirectory.StartsWith(normalizedHome, comparison))
        {
            return workingDirectory;
        }

        var suffix = workingDirectory[normalizedHome.Length..];
        if (suffix.Length == 0)
        {
            return "~";
        }

        if (suffix[0] != Path.DirectorySeparatorChar && suffix[0] != Path.AltDirectorySeparatorChar)
        {
            return workingDirectory;
        }

        return "~" + suffix;
    }
}

public sealed record CommandExecutionOptions(bool EchoCommand, bool TriggerCommandHooks, bool RecordHistory = true, bool AllowCurse = false, bool AllowAmbient = false);

internal sealed record QueuedInteractiveCommand(
    ShellContext Context,
    string CommandText,
    bool EchoCommand,
    CancellationToken CancellationToken,
    TaskCompletionSource<int> Completion);

internal sealed record InteractiveWorkItem(string? UserInput, QueuedInteractiveCommand? QueuedCommand);

public sealed class ShellLifetime
{
    public bool ExitRequested { get; private set; }

    public void RequestExit()
    {
        ExitRequested = true;
    }
}
