using ReaperShell.Abstractions;
using ReaperShell.BuiltIns;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class CurseModeTests
{
    [Fact]
    public async Task PrayAddsBlessingChargesWhenCursed()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0, 0));
        curseState.Enable();
        var command = new PrayCommand(curseState);

        var (exitCode, stdout, stderr) = await ExecuteAsync(command, []);

        Assert.Equal(0, exitCode);
        Assert.Contains("THE HEAP ACCEPTS YOUR OFFERING.", stdout);
        Assert.Contains("Blessing charges increased to 2.", stdout);
        Assert.Equal(2, curseState.BlessingCharges);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public async Task PrayStatusReportsCurseState()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(3));
        curseState.Enable();
        var command = new PrayCommand(curseState);

        var (exitCode, stdout, stderr) = await ExecuteAsync(command, ["status"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("Cursed mode status: enabled", stdout);
        Assert.Contains("Blessing charges: 0", stdout);
        Assert.Contains("Failure chance: 15%", stdout);
        Assert.Contains("Mood: suspicious", stdout);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public async Task CurseCommandSupportsEnableDisableExorciseAndFailureRate()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0));
        var curseCommand = new CurseCommand(curseState);
        var prayCommand = new PrayCommand(curseState);

        var enable = await ExecuteAsync(curseCommand, ["enable"]);
        Assert.Equal(0, enable.ExitCode);
        Assert.True(curseState.Enabled);
        Assert.Contains("Cursed mode enabled", enable.StdOut);

        var setFailureRate = await ExecuteAsync(curseCommand, ["set-failure-rate", "25"]);
        Assert.Equal(0, setFailureRate.ExitCode);
        Assert.Equal(25, curseState.FailureChancePercent);
        Assert.Contains("Failure chance set to 25%", setFailureRate.StdOut);

        var pray = await ExecuteAsync(prayCommand, []);
        Assert.Equal(0, pray.ExitCode);
        Assert.Equal(2, curseState.BlessingCharges);

        var disable = await ExecuteAsync(curseCommand, ["disable"]);
        Assert.Equal(0, disable.ExitCode);
        Assert.False(curseState.Enabled);
        Assert.Equal(0, curseState.BlessingCharges);
        Assert.Contains("less cursed", disable.StdOut);

        await ExecuteAsync(curseCommand, ["enable"]);
        await ExecuteAsync(prayCommand, ["hard"]);
        Assert.True(curseState.BlessingCharges > 0);

        var exorcise = await ExecuteAsync(curseCommand, ["exorcise"]);
        Assert.Equal(0, exorcise.ExitCode);
        Assert.False(curseState.Enabled);
        Assert.Equal(0, curseState.BlessingCharges);
        Assert.Contains("YAML", exorcise.StdOut);

        var invalid = await ExecuteAsync(curseCommand, ["set-failure-rate", "99"]);
        Assert.Equal(1, invalid.ExitCode);
        Assert.Contains("between 0 and 50", invalid.StdErr);
    }

    [Fact]
    public async Task CurseCommandSupportsAmbientAndJournalControls()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0));
        var curseCommand = new CurseCommand(curseState);

        await ExecuteAsync(curseCommand, ["enable"]);

        var quiet = await ExecuteAsync(curseCommand, ["quiet"]);
        Assert.Equal(0, quiet.ExitCode);
        Assert.True(curseState.IsAmbientQuiet);

        var listen = await ExecuteAsync(curseCommand, ["listen"]);
        Assert.Equal(0, listen.ExitCode);
        Assert.Equal(5, curseState.AmbientChatterChancePercent);

        var chatter = await ExecuteAsync(curseCommand, ["chatter", "12"]);
        Assert.Equal(0, chatter.ExitCode);
        Assert.Equal(12, curseState.AmbientChatterChancePercent);

        var inspect = await ExecuteAsync(curseCommand, ["inspect"]);
        Assert.Equal(0, inspect.ExitCode);
        Assert.Contains("Ambient chatter: 12%", inspect.StdOut);
        Assert.Contains("Ambient flavor:", inspect.StdOut);
        Assert.Contains("Protected commands:", inspect.StdOut);

        var journal = await ExecuteAsync(curseCommand, ["journal"]);
        Assert.Equal(0, journal.ExitCode);
        Assert.Contains("Recent curse events:", journal.StdOut);
    }

    [Fact]
    public async Task FortuneDoesNotMutateStateWhenCurseModeIsDisabled()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0));
        var fortuneCommand = new FortuneCommand(curseState);

        var beforeCharges = curseState.BlessingCharges;
        var beforeMood = curseState.Mood;
        var beforeLastOmen = curseState.LastOmen;

        var (exitCode, stdout, stderr) = await ExecuteAsync(fortuneCommand, []);

        Assert.Equal(0, exitCode);
        Assert.Contains("No curse is active. The omen is purely decorative.", stdout);
        Assert.Equal(beforeCharges, curseState.BlessingCharges);
        Assert.Equal(beforeMood, curseState.Mood);
        Assert.Equal(beforeLastOmen, curseState.LastOmen);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public async Task FortuneCanAddOneBlessingCharge()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0, 0, 45, 0));
        curseState.Enable();
        var fortuneCommand = new FortuneCommand(curseState);

        var (exitCode, stdout, stderr) = await ExecuteAsync(fortuneCommand, ["read"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, curseState.BlessingCharges);
        Assert.True(
            stdout.Contains("blessing", StringComparison.OrdinalIgnoreCase) ||
            stdout.Contains("green light", StringComparison.OrdinalIgnoreCase));
        Assert.True(string.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public async Task FortuneCanChangeCurseMood()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0, 0, 70, 2));
        curseState.Enable();
        var fortuneCommand = new FortuneCommand(curseState);

        var (exitCode, stdout, stderr) = await ExecuteAsync(fortuneCommand, []);

        Assert.Equal(0, exitCode);
        Assert.Contains("dramatic", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("dramatic", curseState.Mood);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public async Task FortuneStatusReportsLastOmen()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0, 45, 0));
        curseState.Enable();
        var fortuneCommand = new FortuneCommand(curseState);

        var fortune = await ExecuteAsync(fortuneCommand, []);
        Assert.Equal(0, fortune.ExitCode);

        var (exitCode, stdout, stderr) = await ExecuteAsync(fortuneCommand, ["status"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("Cursed mode status: enabled", stdout);
        Assert.Contains("Last omen:", stdout);
        Assert.Contains(curseState.LastOmen!, stdout);
        Assert.True(string.IsNullOrWhiteSpace(stderr));
    }

    [Fact]
    public async Task FortuneRejectsUnknownArguments()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0));
        var fortuneCommand = new FortuneCommand(curseState);

        var (exitCode, stdout, stderr) = await ExecuteAsync(fortuneCommand, ["unexpected"]);

        Assert.Equal(1, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stdout));
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public async Task AmbientMessagesCanAppearAfterUserCommands()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0, 0, 0, 1));
        curseState.Enable();
        curseState.TrySetFailureChance(0);
        curseState.TrySetAmbientChatterChance(25);

        var parser = new CommandParser();
        var registry = new CommandRegistry();
        registry.RegisterBuiltIn(new ConstantCommand("git", "git-ran"));
        registry.RegisterBuiltIn(new EchoCommand());

        var stateDirectory = Path.Combine(
            Path.GetTempPath(),
            "ReaperShell.CurseModeTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(stateDirectory, "rituals"));
        await File.WriteAllTextAsync(
            Path.Combine(stateDirectory, "rituals", "after.rsh"),
            "echo after-hook");

        var settings = new ShellSettings();
        settings.Hooks[ShellHookEventNames.AfterCommand] = ["after"];

        var host = new ShellHost(
            parser,
            registry,
            new ShellLifetime(),
            new ProcessRunner(),
            settings,
            stateDirectory,
            curseState: curseState);

        var result = await RunHostCommandAsync(host, "git");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("git-ran", result.StdOut);
        Assert.Contains("after-hook", result.StdOut);
        Assert.Contains("A branch goblin approves", result.StdOut);
        Assert.True(result.StdOut.IndexOf("git-ran", StringComparison.Ordinal) < result.StdOut.IndexOf("after-hook", StringComparison.Ordinal));
        Assert.True(result.StdOut.IndexOf("after-hook", StringComparison.Ordinal) < result.StdOut.IndexOf("A branch goblin approves", StringComparison.Ordinal));
        Assert.Contains("A branch goblin approves", string.Join(Environment.NewLine, curseState.GetJournalLines()));
    }

    [Fact]
    public async Task DisabledCurseProducesNoAmbientMessages()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0));
        curseState.TrySetAmbientChatterChance(25);

        var parser = new CommandParser();
        var registry = new CommandRegistry();
        registry.RegisterBuiltIn(new ConstantCommand("git", "git-ran"));

        var host = new ShellHost(
            parser,
            registry,
            new ShellLifetime(),
            new ProcessRunner(),
            new ShellSettings(),
            Path.Combine(Path.GetTempPath(), "ReaperShell.CurseModeTests"),
            curseState: curseState);

        var result = await RunHostCommandAsync(host, "git");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("branch goblin", result.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShellHostBlocksUnprotectedCommandsButNotProtectedOnes()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0));
        curseState.Enable();
        curseState.TrySetFailureChance(50);

        var parser = new CommandParser();
        var registry = new CommandRegistry();
        registry.RegisterBuiltIn(new ConstantCommand("storm", "storm-ran"));
        registry.RegisterBuiltIn(new ConstantCommand("help", "help-ran"));
        registry.RegisterBuiltIn(new ConstantCommand("exit", "exit-ran"));
        registry.RegisterBuiltIn(new ConstantCommand("quit", "quit-ran"));
        registry.RegisterBuiltIn(new ConstantCommand("history", "history-ran"));
        registry.RegisterBuiltIn(new ConstantCommand("doctor", "doctor-ran"));
        registry.RegisterBuiltIn(new ConstantCommand("fortune", "fortune-ran"));

        var host = new ShellHost(
            parser,
            registry,
            new ShellLifetime(),
            new ProcessRunner(),
            new ShellSettings(),
            Path.Combine(Path.GetTempPath(), "ReaperShell.CurseModeTests"),
            curseState: curseState);

        foreach (var protectedCommand in new[] { "help", "exit", "quit", "history", "doctor", "fortune" })
        {
            var (exitCode, stdout, stderr) = await RunHostCommandAsync(host, protectedCommand);

            Assert.Equal(0, exitCode);
            Assert.Contains($"{protectedCommand}-ran", stdout);
            Assert.True(string.IsNullOrWhiteSpace(stderr));
        }

        Assert.Equal(0, curseState.AttemptedCommands);

        var blocked = await RunHostCommandAsync(host, "storm");
        Assert.Equal(1, blocked.ExitCode);
        Assert.DoesNotContain("storm-ran", blocked.StdOut);
        Assert.Contains("forgot to pray", blocked.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, curseState.AttemptedCommands);
    }

    [Fact]
    public async Task NextCommandGraceIsClearedAfterBlessingConsumesIt()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0, 0, 0, 0));
        curseState.Enable();
        curseState.TrySetFailureChance(5);
        curseState.GrantNextCommandGrace(10);
        curseState.AddBlessing(1, "test");

        var parser = new CommandParser();
        var registry = new CommandRegistry();
        registry.RegisterBuiltIn(new ConstantCommand("storm", "storm-ran"));

        var host = new ShellHost(
            parser,
            registry,
            new ShellLifetime(),
            new ProcessRunner(),
            new ShellSettings(),
            Path.Combine(Path.GetTempPath(), "ReaperShell.CurseModeTests"),
            curseState: curseState);

        var first = await RunHostCommandAsync(host, "storm");
        Assert.Equal(0, first.ExitCode);
        Assert.Contains("storm-ran", first.StdOut);
        Assert.Equal(0, curseState.BlessingCharges);
        Assert.Equal(0, curseState.NextCommandGraceChancePercent);

        var second = await RunHostCommandAsync(host, "storm");
        Assert.Equal(1, second.ExitCode);
        Assert.DoesNotContain("storm-ran", second.StdOut);
        Assert.Contains("forgot to pray", second.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, curseState.AttemptedCommands);
    }

    [Fact]
    public async Task ShellContextServicesExposeICursedShellToCommands()
    {
        var curseState = new ShellCurseState(new SequenceCurseRandom(0));
        curseState.Enable();
        var services = new ShellServiceProvider().Add<ICursedShell>(curseState);
        var command = new ServiceAwareCommand();

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(
            stdout,
            stderr,
            new DirectoryInfo(Path.GetTempPath()),
            services,
            CancellationToken.None);

        var exitCode = await command.ExecuteAsync(context, [], CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("custom command leaves a strange smell", string.Join(Environment.NewLine, curseState.GetJournalLines()));
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> ExecuteAsync(
        IShellCommand command,
        IReadOnlyList<string> args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(stdout, stderr, new DirectoryInfo(Path.GetTempPath()), services: null, CancellationToken.None);
        var exitCode = await command.ExecuteAsync(context, args, CancellationToken.None);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunHostCommandAsync(
        ShellHost host,
        string commandText)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var context = new ShellContext(stdout, stderr, new DirectoryInfo(Path.GetTempPath()), services: null, CancellationToken.None);
        var exitCode = await host.RunCommandAsync(context, commandText, CancellationToken.None);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed class SequenceCurseRandom : ICurseRandom
    {
        private readonly Queue<int> _values;

        public SequenceCurseRandom(params int[] values)
        {
            _values = new Queue<int>(values);
        }

        public int Next(int maxExclusive)
        {
            if (_values.Count == 0)
            {
                return 0;
            }

            return Math.Max(0, _values.Dequeue() % maxExclusive);
        }
    }

    private sealed class ConstantCommand : IShellCommand
    {
        private readonly string _name;
        private readonly string _output;

        public ConstantCommand(string name, string output)
        {
            _name = name;
            _output = output;
        }

        public string Name => _name;

        public string Description => "Test built-in command.";

        public Task<int> ExecuteAsync(
            ShellContext context,
            IReadOnlyList<string> args,
            CancellationToken cancellationToken = default)
        {
            context.WriteLine(_output);
            return Task.FromResult(0);
        }
    }

    private sealed class ServiceAwareCommand : IShellCommand
    {
        public string Name => "service-aware";

        public string Description => "Uses ICursedShell from context services.";

        public Task<int> ExecuteAsync(
            ShellContext context,
            IReadOnlyList<string> args,
            CancellationToken cancellationToken = default)
        {
            var curse = context.Services?.GetService(typeof(ICursedShell)) as ICursedShell;
            if (curse?.IsEnabled == true)
            {
                curse.AddAmbientEvent("The custom command leaves a strange smell in the heap.");
            }

            return Task.FromResult(0);
        }
    }
}
