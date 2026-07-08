using ReaperShell.Abstractions;

namespace ReaperShell.Shell;

public sealed class ShellCurseState : ICursedShell
{
    private static readonly string[] Moods =
    [
        "petty",
        "hungry",
        "dramatic",
        "suspicious"
    ];

    private static readonly string[] ProtectedCommands =
    [
        "pray",
        "curse",
        "fortune",
        "help",
        "exit",
        "quit",
        "history",
        "doctor"
    ];

    private static readonly string[] PrayerResponses =
    [
        "THE HEAP ACCEPTS YOUR OFFERING.",
        "CSS DEMONS REMAIN CONTAINED.",
        "THE COLLECTIBLE ALC MAY OR MAY NOT HAVE UNLOADED.",
        "THE BUILD SPIRITS REQUEST ONE MORE CLEAN RELOAD."
    ];

    private static readonly string[] HardPrayerResponses =
    [
        "THE BUILD SPIRITS REQUEST ONE MORE CLEAN RELOAD.",
        "A STACK TRACE CRAWLS ACROSS THE ALTAR.",
        "THE DAEMON IN THE PATH ASKS FOR A SECOND TITHING.",
        "THE MODULE CACHE WHISPERS ABOUT COMMITMENT ISSUES."
    ];

    private static readonly string[] BlessingMessages =
    [
        "The shell feels briefly less cursed.",
        "The daemon in the PATH has been appeased.",
        "A stack trace bows and backs away.",
        "The build spirits have accepted your paperwork."
    ];

    private static readonly string[] FortuneTexts =
    [
        "A clean build is just a failed build that has not happened yet.",
        "The next reload will reveal whether the spirits respect semver.",
        "Beware the command that works only after you explain it to someone else.",
        "A daemon in the PATH smiles upon your tab completion.",
        "The heap remembers what the stack denies.",
        "Your next command has been reviewed by three ghosts and one linter.",
        "Somewhere, a shell script is becoming self-aware.",
        "The prophecy says: try not to refactor the folder structure today."
    ];

    private static readonly string[] NeutralOmenMessages =
    [
        "Neutral omen: the shell watches, unimpressed.",
        "The prophecy is vague and therefore technically correct."
    ];

    private static readonly string[] MoodOmenMessages =
    [
        "The curse mood shifts to {0}. This is probably fine.",
        "The shell now feels {0}. Great. Wonderful."
    ];

    private static readonly string[] GraceOmenMessages =
    [
        "Good omen: the next command feels slightly less doomed."
    ];

    private static readonly string[] BadOmenMessages =
    [
        "Bad omen: a YAML file somewhere has become smug.",
        "Bad omen: the shell whispers, but refuses to elaborate."
    ];

    private static readonly string[] FailureMessages =
    [
        "The command fizzles. You forgot to pray.",
        "The shell rejects your offering.",
        "A daemon whispers: not this time.",
        "The pipes demand tribute. Try pray."
    ];

    private static readonly string[] AmbientMessages =
    [
        "The shell blinks.",
        "Something under .rsh shifts and goes still.",
        "A daemon in the PATH pretends it was not watching.",
        "The prompt exhales.",
        "A stale stack trace rattles in the walls.",
        "The curse rearranges nothing, suspiciously.",
        "Tab completion dreams of teeth.",
        "The shell whispers: clean build, clean conscience.",
        "A plugin somewhere feels judged."
    ];

    private static readonly string[] AmbientSuccessMessages =
    [
        "A branch goblin approves, reluctantly.",
        "The loaded assemblies hum in a minor key.",
        "The shell nods at the outcome, whatever it was.",
        "The prompt relaxes by one unmeasurable degree."
    ];

    private static readonly string[] AmbientFailureMessages =
    [
        "The curse writes this down as evidence.",
        "Failure accepted. The shell has seen worse.",
        "The daemon nods like this was expected.",
        "A test passed somewhere. The curse finds this suspicious."
    ];

    private const int DefaultFailureChancePercent = 15;
    private const int DefaultAmbientChancePercent = 5;
    private const int JournalLimit = 25;

    private readonly ICurseRandom _random;
    private readonly Queue<string> _journal = new();

    public ShellCurseState(ICurseRandom? random = null)
    {
        _random = random ?? new CurseRandom();
    }

    public bool Enabled { get; private set; }

    public bool IsEnabled => Enabled;

    public int BlessingCharges { get; private set; }

    public int FailureChancePercent { get; private set; } = DefaultFailureChancePercent;

    public int AttemptedCommands { get; private set; }

    public string Mood { get; private set; } = "suspicious";

    public string? LastOmen { get; private set; }

    public int NextCommandGraceChancePercent { get; private set; }

    public int AmbientChatterChancePercent { get; private set; } = DefaultAmbientChancePercent;

    public bool IsAmbientQuiet => AmbientChatterChancePercent == 0;

    public void Enable()
    {
        Enabled = true;
        Mood = Moods[_random.Next(Moods.Length)];
        AddJournalEvent($"Curse enabled. Mood: {Mood}.");
    }

    public void Disable()
    {
        Enabled = false;
        BlessingCharges = 0;
        NextCommandGraceChancePercent = 0;
        AddJournalEvent("Curse disabled. Blessing charges cleared.");
    }

    public void Exorcise()
    {
        Disable();
        AddJournalEvent("The curse screams in YAML and leaves.");
    }

    public void Quiet()
    {
        AmbientChatterChancePercent = 0;
        AddJournalEvent("Ambient chatter set to quiet.");
    }

    public void Listen()
    {
        AmbientChatterChancePercent = DefaultAmbientChancePercent;
        AddJournalEvent($"Ambient chatter restored to {DefaultAmbientChancePercent}%.");
    }

    public bool TrySetAmbientChatterChance(int percent)
    {
        if (percent < 0 || percent > 25)
        {
            return false;
        }

        AmbientChatterChancePercent = percent;
        AddJournalEvent($"Ambient chatter set to {percent}%.");
        return true;
    }

    public bool TrySetFailureChance(int percent)
    {
        if (percent < 0 || percent > 50)
        {
            return false;
        }

        FailureChancePercent = percent;
        AddJournalEvent($"Failure chance set to {percent}%.");
        return true;
    }

    public CursePrayerResult Pray(bool hard)
    {
        var response = hard
            ? HardPrayerResponses[_random.Next(HardPrayerResponses.Length)]
            : PrayerResponses[_random.Next(PrayerResponses.Length)];

        if (!Enabled)
        {
            var message = $"{response} No curse is active; nothing was actually cursed. Prayer filed as paperwork.";
            AddJournalEvent("Prayer filed as paperwork.");
            return new CursePrayerResult(false, 0, BlessingCharges, message);
        }

        var addedCharges = hard
            ? 5 + _random.Next(6)
            : 2 + _random.Next(4);

        AddBlessing(addedCharges, hard ? "Hard prayer" : "Prayer");

        return new CursePrayerResult(
            true,
            addedCharges,
            BlessingCharges,
            $"{response} Blessing charges increased to {BlessingCharges}.");
    }

    public FortuneResult RevealFortune()
    {
        var fortune = FortuneTexts[_random.Next(FortuneTexts.Length)];
        if (!Enabled)
        {
            return new FortuneResult(fortune, "No curse is active. The omen is purely decorative.", false);
        }

        var omenRoll = _random.Next(100);
        string omenMessage;
        var stateChanged = false;

        if (omenRoll < 45)
        {
            omenMessage = NeutralOmenMessages[_random.Next(NeutralOmenMessages.Length)];
        }
        else if (omenRoll < 70)
        {
            AddBlessing(1, "Good omen");
            stateChanged = true;
            omenMessage = _random.Next(2) == 0
                ? "Good omen: the daemon grants one blessing charge."
                : "A tiny green light flickers in the pipes. Blessing +1.";
        }
        else if (omenRoll < 85)
        {
            var mood = Moods[_random.Next(Moods.Length)];
            ShiftMood(mood);
            stateChanged = true;
            omenMessage = string.Format(
                MoodOmenMessages[_random.Next(MoodOmenMessages.Length)],
                mood);
        }
        else if (omenRoll < 95)
        {
            GrantNextCommandGrace(10);
            stateChanged = true;
            omenMessage = GraceOmenMessages[_random.Next(GraceOmenMessages.Length)];
            AddJournalEvent("Good omen: the next command feels slightly less doomed.");
        }
        else
        {
            omenMessage = BadOmenMessages[_random.Next(BadOmenMessages.Length)];
        }

        LastOmen = omenMessage;
        AddJournalEvent($"Fortune read: {fortune}");
        AddJournalEvent(omenMessage);
        return new FortuneResult(fortune, omenMessage, stateChanged);
    }

    public string? TryGetAmbientMessage(string commandName, int exitCode)
    {
        if (!Enabled || AmbientChatterChancePercent <= 0)
        {
            return null;
        }

        if (_random.Next(100) >= AmbientChatterChancePercent)
        {
            return null;
        }

        var message = SelectAmbientMessage(commandName, exitCode);
        return message;
    }

    public string Poke()
    {
        if (!Enabled)
        {
            return "Nothing answers. Probably for the best.";
        }

        var roll = _random.Next(100);
        string message;

        if (roll < 25)
        {
            message = "The shell bites the stick.";
        }
        else if (roll < 50)
        {
            message = "Something in the prompt hisses.";
        }
        else if (roll < 75)
        {
            var mood = Moods[_random.Next(Moods.Length)];
            ShiftMood(mood);
            message = $"The curse is now {mood}. Great work.";
        }
        else
        {
            message = "A daemon peeks out, sees you, and closes the port.";
            AddAmbientEvent("The curse refuses to perform on command.");
        }

        AddJournalEvent($"Poke: {message}");
        return message;
    }

    public IReadOnlyList<string> GetStatusLines(string heading)
    {
        var lines = new List<string>
        {
            $"{heading}: {(Enabled ? "enabled" : "disabled")}",
            $"Blessing charges: {BlessingCharges}",
            $"Failure chance: {FailureChancePercent}%",
            $"Mood: {Mood}",
            $"Ambient chatter: {AmbientChatterChancePercent}%",
            $"Commands attempted while cursed: {AttemptedCommands}"
        };

        if (NextCommandGraceChancePercent > 0)
        {
            lines.Add($"Next command grace: {NextCommandGraceChancePercent}%");
        }

        if (!string.IsNullOrWhiteSpace(LastOmen))
        {
            lines.Add($"Last omen: {LastOmen}");
        }

        return lines;
    }

    public IReadOnlyList<string> GetInspectLines()
    {
        var lines = GetStatusLines("Cursed mode").ToList();
        lines.RemoveAll(line => line.StartsWith("Commands attempted", StringComparison.Ordinal));
        lines.Add($"Ambient flavor: {(IsAmbientQuiet ? "quiet" : "listening")}");
        lines.Add($"Protected commands: {string.Join(", ", ProtectedCommands)}");
        return lines;
    }

    public IReadOnlyList<string> GetJournalLines()
    {
        var entries = _journal.ToArray();
        var lines = new List<string> { "Recent curse events:" };

        if (entries.Length == 0)
        {
            lines.Add("1. No curse events recorded yet.");
            return lines;
        }

        for (var index = 0; index < entries.Length; index++)
        {
            lines.Add($"{index + 1}. {entries[index]}");
        }

        return lines;
    }

    public void AddAmbientEvent(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        AddJournalEvent(message);
    }

    public void ShiftMood(string mood)
    {
        if (string.IsNullOrWhiteSpace(mood))
        {
            return;
        }

        Mood = mood;
        AddJournalEvent($"Mood shifted to {mood}.");
    }

    public void AddBlessing(int charges, string? reason = null)
    {
        if (charges <= 0)
        {
            return;
        }

        BlessingCharges += charges;
        var prefix = string.IsNullOrWhiteSpace(reason) ? "Blessing" : reason;
        AddJournalEvent($"{prefix}: +{charges}. Blessing charges now {BlessingCharges}.");
    }

    public IReadOnlyList<string> GetProtectedCommands()
    {
        return ProtectedCommands;
    }

    public void GrantNextCommandGrace(int percent)
    {
        NextCommandGraceChancePercent = Math.Clamp(percent, 0, 50);
        AddJournalEvent($"Next command grace set to {NextCommandGraceChancePercent}%.");
    }

    public CurseDecision EvaluateBeforeCommand(string commandName, bool allowCurse)
    {
        if (!Enabled || !allowCurse)
        {
            return CurseDecision.Allow();
        }

        AttemptedCommands++;

        if (IsProtectedCommand(commandName))
        {
            return CurseDecision.Allow();
        }

        if (BlessingCharges > 0)
        {
            BlessingCharges--;
            var blessingMessage = _random.Next(4) == 0
                ? BlessingMessages[_random.Next(BlessingMessages.Length)]
                : null;

            AddJournalEvent($"{commandName} consumed one blessing charge.");
            if (blessingMessage is not null)
            {
                AddJournalEvent(blessingMessage);
            }

            return CurseDecision.Allow(blessingMessage);
        }

        var effectiveFailureChance = FailureChancePercent;
        if (NextCommandGraceChancePercent > 0)
        {
            effectiveFailureChance = Math.Max(0, effectiveFailureChance - NextCommandGraceChancePercent);
            NextCommandGraceChancePercent = 0;
        }

        if (effectiveFailureChance > 0 && _random.Next(100) < effectiveFailureChance)
        {
            var blockedMessage = FailureMessages[_random.Next(FailureMessages.Length)];
            AddJournalEvent($"Blocked {commandName}: {blockedMessage}");
            return CurseDecision.Block(blockedMessage);
        }

        return CurseDecision.Allow();
    }

    private string SelectAmbientMessage(string commandName, int exitCode)
    {
        var normalized = commandName.Trim().ToLowerInvariant();
        string[]? specificMessages = normalized switch
        {
            var name when name.Contains("git", StringComparison.Ordinal) =>
                new[]
                {
                    "A branch goblin approves, reluctantly.",
                    "The reflog remembers. It always remembers."
                },
            var name when name.Contains("dotnet", StringComparison.Ordinal) || name.Contains("build", StringComparison.Ordinal) || name.Contains("test", StringComparison.Ordinal) =>
                new[]
                {
                    "The build spirits sniff the output.",
                    "A test passed somewhere. The curse finds this suspicious."
                },
            "rm" or "del" or "remove" =>
                new[]
                {
                    "The shell watches your hands very carefully.",
                    "The curse refuses to help with that one. Probably wise."
                },
            var name when name.Contains("repo", StringComparison.Ordinal) || name.Contains("plugin", StringComparison.Ordinal) || name.Contains("reload", StringComparison.Ordinal) =>
                new[]
                {
                    "The loaded assemblies hum in a minor key.",
                    "A collectible ALC may or may not have looked at you funny."
                },
            "fortune" =>
                new[]
                {
                    "The prophecy hums like a live wire.",
                    "The omen nods at itself."
                },
            "pray" =>
                new[]
                {
                    "The altar approves of the paperwork.",
                    "Blessings are easier when the shell is paying attention."
                },
            _ => null
        };

        var pool = exitCode == 0
            ? (specificMessages is null || specificMessages.Length == 0 ? AmbientSuccessMessages : specificMessages)
            : AmbientFailureMessages;

        var message = pool[_random.Next(pool.Length)];
        if (_random.Next(5) == 0)
        {
            message = AmbientMessages[_random.Next(AmbientMessages.Length)];
        }

        return message;
    }

    private bool IsProtectedCommand(string commandName)
    {
        return ProtectedCommands.Any(protectedCommand => string.Equals(
            protectedCommand,
            commandName,
            StringComparison.OrdinalIgnoreCase));
    }

    private void AddJournalEvent(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _journal.Enqueue(message.Trim());
        while (_journal.Count > JournalLimit)
        {
            _journal.Dequeue();
        }
    }
}

public sealed record CurseDecision(bool BlockCommand, string? Message)
{
    public static CurseDecision Allow(string? message = null)
    {
        return new CurseDecision(false, message);
    }

    public static CurseDecision Block(string message)
    {
        return new CurseDecision(true, message);
    }
}

public sealed record CursePrayerResult(bool CurseActive, int AddedBlessingCharges, int BlessingCharges, string Message);

public sealed record FortuneResult(string FortuneText, string OmenMessage, bool StateChanged);
