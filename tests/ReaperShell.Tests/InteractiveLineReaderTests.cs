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
        Assert.DoesNotContain("rsh> rsh>", console.Output);
        Assert.True(CountOccurrences(console.Output, "rsh> ") <= 2);
        Assert.Equal("rsh> ".Length + "echo hi".Length, console.LastSetCursorLeft);
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
        Assert.DoesNotContain("rsh> rsh>", console.Output);
        Assert.True(CountOccurrences(console.Output, "rsh> ") <= 4);
    }

    [Fact]
    public async Task PastedCommandDoesNotDoubleRenderPrompt()
    {
        var console = new ScriptedInteractiveConsole(
            Keys("echo pasted", includeEnter: true));
        var reader = new InteractiveLineReader(console);

        var line = await reader.ReadLineAsync(
            "rsh> ",
            () => new DirectoryInfo(Path.GetTempPath()),
            () => [],
            () => [],
            () => [],
            CancellationToken.None);

        Assert.Equal("echo pasted", line);
        Assert.DoesNotContain("rsh> rsh>", console.Output);
        Assert.True(CountOccurrences(console.Output, "rsh> ") <= 2);
    }

    [Fact]
    public async Task LeftArrowDoesNotDoubleRenderPrompt()
    {
        var console = new ScriptedInteractiveConsole(
            Keys("abc", includeEnter: false)
                .Concat([ConsoleKeyInfoFor(ConsoleKey.LeftArrow, '\0'), ConsoleKeyInfoFor(ConsoleKey.X, 'X'), ConsoleKeyInfoFor(ConsoleKey.Enter, '\r')]));
        var reader = new InteractiveLineReader(console);

        var line = await reader.ReadLineAsync(
            "rsh> ",
            () => new DirectoryInfo(Path.GetTempPath()),
            () => [],
            () => [],
            () => [],
            CancellationToken.None);

        Assert.Equal("abXc", line);
        Assert.DoesNotContain("rsh> rsh>", console.Output);
        Assert.Equal("rsh> ".Length + 3, console.LastSetCursorLeft);
    }

    [Fact]
    public async Task HomeDoesNotDoubleRenderPrompt()
    {
        var console = new ScriptedInteractiveConsole(
            Keys("abc", includeEnter: false)
                .Concat([ConsoleKeyInfoFor(ConsoleKey.Home, '\0'), ConsoleKeyInfoFor(ConsoleKey.X, 'X'), ConsoleKeyInfoFor(ConsoleKey.Enter, '\r')]));
        var reader = new InteractiveLineReader(console);

        var line = await reader.ReadLineAsync(
            "rsh> ",
            () => new DirectoryInfo(Path.GetTempPath()),
            () => [],
            () => [],
            () => [],
            CancellationToken.None);

        Assert.Equal("Xabc", line);
        Assert.DoesNotContain("rsh> rsh>", console.Output);
        Assert.Equal("rsh> ".Length + 1, console.LastSetCursorLeft);
    }

    [Fact]
    public async Task HistoryNavigationDoesNotDoubleRenderPrompt()
    {
        var console = new ScriptedInteractiveConsole(
            [ConsoleKeyInfoFor(ConsoleKey.UpArrow, '\0'), ConsoleKeyInfoFor(ConsoleKey.Enter, '\r')]);
        var reader = new InteractiveLineReader(console);

        var line = await reader.ReadLineAsync(
            "rsh> ",
            () => new DirectoryInfo(Path.GetTempPath()),
            () => new[] { "echo old" },
            () => [],
            () => [],
            CancellationToken.None);

        Assert.Equal("echo old", line);
        Assert.DoesNotContain("rsh> rsh>", console.Output);
        Assert.Equal("rsh> ".Length + "echo old".Length, console.LastSetCursorLeft);
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
        Assert.Equal("rsh> ".Length + 1, console.LastSetCursorLeft);
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

    [Theory]
    [InlineData(ConsoleKey.A, "Xone two", 1)]
    [InlineData(ConsoleKey.E, "one twoX", 8)]
    [InlineData(ConsoleKey.K, "one twX", 7)]
    [InlineData(ConsoleKey.U, "Xo", 1)]
    [InlineData(ConsoleKey.W, "one Xo", 5)]
    [InlineData(ConsoleKey.L, "one twXo", 7)]
    public async Task ControlShortcutsEditAtCursor(ConsoleKey key, string expected, int cursor)
    {
        var console = new ScriptedInteractiveConsole(
            Keys("one two", includeEnter: false).Concat([
                ConsoleKeyInfoFor(ConsoleKey.LeftArrow, '\0'),
                ControlKey(key),
                ToKey('X'),
                ConsoleKeyInfoFor(ConsoleKey.Enter, '\r')]));

        Assert.Equal(expected, await ReadLine(console));
        Assert.Equal(5 + cursor, console.LastSetCursorLeft);
    }

    [Theory]
    [InlineData(ConsoleKey.A, "one two")]
    [InlineData(ConsoleKey.E, "one two")]
    [InlineData(ConsoleKey.K, "one two")]
    [InlineData(ConsoleKey.U, "")]
    [InlineData(ConsoleKey.W, "one ")]
    [InlineData(ConsoleKey.L, "one two")]
    public async Task ControlShortcutsExitHistoryNavigation(ConsoleKey key, string expected)
    {
        var console = new ScriptedInteractiveConsole([
            ConsoleKeyInfoFor(ConsoleKey.UpArrow, '\0'),
            ControlKey(key),
            ConsoleKeyInfoFor(ConsoleKey.DownArrow, '\0'),
            ConsoleKeyInfoFor(ConsoleKey.Enter, '\r')]);

        Assert.Equal(expected, await ReadLine(console));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(PlatformNotSupportedException))]
    public async Task ControlLRedrawsAndPreservesInputWhenClearIsUnavailable(Type? errorType)
    {
        var console = new ScriptedInteractiveConsole(
            Keys("abc", includeEnter: false).Concat([
                ConsoleKeyInfoFor(ConsoleKey.LeftArrow, '\0'),
                ControlKey(ConsoleKey.L),
                ConsoleKeyInfoFor(ConsoleKey.Enter, '\r')]))
        {
            ClearException = errorType is null ? null : (Exception)Activator.CreateInstance(errorType)!
        };
        console.WriteLine("old output");

        Assert.Equal("abc", await ReadLine(console));
        Assert.Equal(1, console.ClearCount);
        Assert.Equal(7, console.LastSetCursorLeft);
        Assert.Equal(errorType is null ? 0 : 1, console.LastSetCursorTop);
        Assert.EndsWith("\rrsh> abc" + Environment.NewLine, console.Output);
    }

    [Theory]
    [InlineData(ConsoleKey.C, "abc", "")]
    [InlineData(ConsoleKey.D, "abc", "abc")]
    [InlineData(ConsoleKey.D, "", null)]
    public async Task ExistingControlShortcutsKeepTheirBehavior(ConsoleKey key, string input, string? expected)
    {
        var console = new ScriptedInteractiveConsole(
            Keys(input, includeEnter: false).Concat([
                ControlKey(key), ConsoleKeyInfoFor(ConsoleKey.Enter, '\r')]));

        Assert.Equal(expected, await ReadLine(console));
        Assert.False(console.TreatControlCAsInput);
    }

    private static Task<string?> ReadLine(ScriptedInteractiveConsole console)
    {
        return new InteractiveLineReader(console).ReadLineAsync(
            "rsh> ",
            () => new DirectoryInfo(Path.GetTempPath()),
            () => new[] { "one two" },
            () => [],
            () => []);
    }

    private static ConsoleKeyInfo ControlKey(ConsoleKey key)
    {
        return new ConsoleKeyInfo((char)((int)key - (int)ConsoleKey.A + 1), key, shift: false, alt: false, control: true);
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

        public int CursorLeft { get; set; }

        public int CursorTop { get; private set; }

        public int BufferWidth { get; set; } = 120;

        public int LastSetCursorLeft { get; private set; }

        public int LastSetCursorTop { get; private set; }

        public bool TreatControlCAsInput { get; set; }

        public string Output { get; private set; } = string.Empty;

        public string? RedirectedLine { get; }

        public int ClearCount { get; private set; }

        public Exception? ClearException { get; init; }

        public void Clear()
        {
            ClearCount++;
            if (ClearException is not null)
            {
                throw ClearException;
            }

            CursorLeft = 0;
            CursorTop = 0;
        }

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

        public void SetCursorPosition(int left, int top)
        {
            CursorLeft = left;
            CursorTop = top;
            LastSetCursorLeft = left;
            LastSetCursorTop = top;
        }

        public void Write(string value)
        {
            Output += value;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (character == '\r')
                {
                    CursorLeft = 0;
                    continue;
                }

                if (character == '\n')
                {
                    CursorTop++;
                    CursorLeft = 0;
                    continue;
                }

                CursorLeft++;
            }
        }

        public void WriteLine()
        {
            Output += Environment.NewLine;
            CursorTop++;
            CursorLeft = 0;
        }

        public void WriteLine(string value)
        {
            Output += value;
            Output += Environment.NewLine;
            CursorTop++;
            CursorLeft = 0;
        }
    }
}
