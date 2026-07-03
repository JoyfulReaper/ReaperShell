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
    private readonly string _stateDirectory;
    private readonly ShellSettings _settings;

    public ShellHost(
        CommandParser commandParser,
        CommandRegistry commandRegistry,
        ShellLifetime lifetime,
        ShellSettings settings,
        string stateDirectory)
    {
        _commandParser = commandParser;
        _commandRegistry = commandRegistry;
        _lifetime = lifetime;
        _settings = settings;
        _stateDirectory = stateDirectory;
    }

    public bool IsInteractiveModeEnabled { get; private set; }

    public async Task<int> RunInteractiveAsync(
        ShellContext context,
        string? profilePath,
        CancellationToken cancellationToken)
    {
        IsInteractiveModeEnabled = true;
        _ = Task.Run(() => ReadInteractiveInputAsync(_interactiveWorkItems.Writer), CancellationToken.None);

        try
        {
            ShellBanner.Write(context);

            if (!string.IsNullOrWhiteSpace(profilePath))
            {
                await RunProfileAsync(context, profilePath, cancellationToken);
            }

            await RunHookEventAsync(context, ShellHookEventNames.Startup, cancellationToken);

            while (!_lifetime.ExitRequested && !cancellationToken.IsCancellationRequested)
            {
                Console.Write("rsh> ");
                var workItem = await _interactiveWorkItems.Reader.ReadAsync(cancellationToken);

                if (workItem.QueuedCommand is not null)
                {
                    var queuedCommand = workItem.QueuedCommand;
                    var exitCode = await ExecuteCommandAsync(
                        queuedCommand.Context,
                        queuedCommand.CommandText,
                        new CommandExecutionOptions(EchoCommand: queuedCommand.EchoCommand, TriggerCommandHooks: false),
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
                    new CommandExecutionOptions(EchoCommand: false, TriggerCommandHooks: true),
                    cancellationToken);
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
            new CommandExecutionOptions(EchoCommand: true, TriggerCommandHooks: true),
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
            new CommandExecutionOptions(EchoCommand: false, TriggerCommandHooks: true),
            cancellationToken);
    }

    public async Task<int> RunProfileAsync(
        ShellContext context,
        string profilePath,
        CancellationToken cancellationToken)
    {
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
            new CommandExecutionOptions(EchoCommand: true, TriggerCommandHooks: false),
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
            new CommandExecutionOptions(EchoCommand: true, TriggerCommandHooks: false),
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
            new CommandExecutionOptions(EchoCommand: echoCommand, TriggerCommandHooks: false),
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
                    new CommandExecutionOptions(EchoCommand: false, TriggerCommandHooks: false),
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
                context.WriteLine($"rsh> {input}");
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

            if (options.TriggerCommandHooks)
            {
                await RunHookEventAsync(context, ShellHookEventNames.BeforeCommand, cancellationToken);
            }

            if (!_commandRegistry.TryGet(resolvedTokens[0], out var command))
            {
                context.WriteErrorLine($"Unknown command: {resolvedTokens[0]}");
                return 1;
            }

            var exitCode = 1;
            try
            {
                exitCode = await command.ExecuteAsync(context, resolvedTokens.Skip(1).ToArray(), cancellationToken);
                return exitCode;
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

    private static async Task ReadInteractiveInputAsync(ChannelWriter<InteractiveWorkItem> writer)
    {
        try
        {
            while (true)
            {
                var input = Console.ReadLine();
                await writer.WriteAsync(new InteractiveWorkItem(input, null));
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
}

public sealed record CommandExecutionOptions(bool EchoCommand, bool TriggerCommandHooks);

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
