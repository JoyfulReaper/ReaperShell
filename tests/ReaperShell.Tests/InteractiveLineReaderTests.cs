using ReaperShell.Abstractions;
using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class InteractiveLineReaderTests
{
    [Fact]
    public async Task PastedSingleCommandIsReadAsOneCleanLine()
    {
        var console = new ScriptedInteractiveConsole(
            Keys("echo hi", includeEnter: true));
        var reader = new InteractiveLineReader(console);

        var line = await reader.ReadLineAsync(
            "rsh> ",
            () => new DirectoryInfo(Path.GetTempPath()),
            () => [],
            () => [],
            () => [],
            CancellationToken.None);

        Assert.Equal("echo hi", line);
        Assert.Equal(2, CountOccurrences(console.Output, "rsh> "));
    }

    [Fact]
    public async Task MultilinePasteSubmitsTheFirstLineAndKeepsTheNextOneQueued()
    {
        var console = new ScriptedInteractiveConsole(
            Keys("echo one", includeEnter: true)
                .Concat(Keys("echo two", includeEnter: true)));
        var reader = new InteractiveLineReader(console);

        var first = await reader.ReadLineAsync(
            "rsh> ",
            () => new DirectoryInfo(Path.GetTempPath()),
            () => [],
            () => [],
            () => [],
            CancellationToken.None);

        var second = await reader.ReadLineAsync(
            "rsh> ",
            () => new DirectoryInfo(Path.GetTempPath()),
            () => [],
            () => [],
            () => [],
            CancellationToken.None);

        Assert.Equal("echo one", first);
        Assert.Equal("echo two", second);
        Assert.Equal(4, CountOccurrences(console.Output, "rsh> "));
    }

    [Fact]
    public async Task BackspaceStillEditsTheCurrentLine()
    {
        var console = new ScriptedInteractiveConsole(
            Keys("ab", includeEnter: false)
                .Concat([ConsoleKeyInfoFor(ConsoleKey.Backspace, '\b'), ConsoleKeyInfoFor(ConsoleKey.Enter, '\r')]));
        var reader = new InteractiveLineReader(console);

        var line = await reader.ReadLineAsync(
            "rsh> ",
            () => new DirectoryInfo(Path.GetTempPath()),
            () => [],
            () => [],
            () => [],
            CancellationToken.None);

        Assert.Equal("a", line);
    }

    [Fact]
    public async Task RedirectedInputFallsBackToReadLine()
    {
        var console = new ScriptedInteractiveConsole([], redirected: true, redirectedLine: "from-redirect");
        var reader = new InteractiveLineReader(console);

        var line = await reader.ReadLineAsync(
            "rsh> ",
            () => new DirectoryInfo(Path.GetTempPath()),
            () => [],
            () => [],
            () => [],
            CancellationToken.None);

        Assert.Equal("from-redirect", line);
    }

    private static IEnumerable<ConsoleKeyInfo> Keys(string text, bool includeEnter)
    {
        foreach (var character in text)
        {
            yield return ToKey(character);
        }

        if (includeEnter)
        {
            yield return ConsoleKeyInfoFor(ConsoleKey.Enter, '\r');
        }
    }

    private static ConsoleKeyInfo ToKey(char character)
    {
        if (character == ' ')
        {
            return ConsoleKeyInfoFor(ConsoleKey.Spacebar, character);
        }

        if (char.IsLetter(character))
        {
            var key = (ConsoleKey)Enum.Parse(typeof(ConsoleKey), char.ToUpperInvariant(character).ToString());
            return ConsoleKeyInfoFor(key, character);
        }

        if (char.IsDigit(character))
        {
            var key = (ConsoleKey)Enum.Parse(typeof(ConsoleKey), $"D{character}");
            return ConsoleKeyInfoFor(key, character);
        }

        return ConsoleKeyInfoFor(ConsoleKey.NoName, character);
    }

    private static ConsoleKeyInfo ConsoleKeyInfoFor(ConsoleKey key, char character)
    {
        return new ConsoleKeyInfo(character, key, shift: false, alt: false, control: false);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while (index >= 0)
        {
            index = text.IndexOf(value, index, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            count++;
            index += value.Length;
        }

        return count;
    }

    private sealed class ScriptedInteractiveConsole : IInteractiveConsole
    {
        private readonly Queue<ConsoleKeyInfo> _keys;

        public ScriptedInteractiveConsole(
            IEnumerable<ConsoleKeyInfo> keys,
            bool redirected = false,
            string? redirectedLine = null)
        {
            _keys = new Queue<ConsoleKeyInfo>(keys);
            IsInputRedirected = redirected;
            RedirectedLine = redirectedLine;
        }

        public bool IsInputRedirected { get; }

        public bool KeyAvailable => _keys.Count > 0;

        public bool TreatControlCAsInput { get; set; }

        public string Output { get; private set; } = string.Empty;

        public string? RedirectedLine { get; }

        public ConsoleKeyInfo ReadKey(bool intercept)
        {
            if (_keys.Count == 0)
            {
                throw new InvalidOperationException("No scripted keys remain.");
            }

            return _keys.Dequeue();
        }

        public string? ReadLine()
        {
            return RedirectedLine;
        }

        public void Write(string value)
        {
            Output += value;
        }

        public void WriteLine()
        {
            Output += Environment.NewLine;
        }

        public void WriteLine(string value)
        {
            Output += value;
            Output += Environment.NewLine;
        }
    }
}
