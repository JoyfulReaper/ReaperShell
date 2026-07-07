using System.Text;
using ReaperShell.Abstractions;

namespace ReaperShell.Shell;

internal sealed class InteractiveLineReader
{
    private readonly CommandCompletionService _commandCompletionService;
    private readonly PathCompletionService _pathCompletionService;

    public InteractiveLineReader(
        CommandCompletionService? commandCompletionService = null,
        PathCompletionService? pathCompletionService = null)
    {
        _commandCompletionService = commandCompletionService ?? new CommandCompletionService();
        _pathCompletionService = pathCompletionService ?? new PathCompletionService();
    }

    public Task<string?> ReadLineAsync(
        string prompt,
        Func<DirectoryInfo> getWorkingDirectory,
        Func<IReadOnlyList<string>> getHistory,
        Func<IReadOnlyList<CommandDescriptor>> getCommands,
        Func<IReadOnlyList<string>> getAliases,
        CancellationToken cancellationToken = default)
    {
        if (Console.IsInputRedirected)
        {
            return Task.FromResult(Console.ReadLine());
        }

        try
        {
            return Task.FromResult(
                ReadInteractiveLine(
                    prompt,
                    getWorkingDirectory,
                    getHistory,
                    getCommands,
                    getAliases,
                    cancellationToken));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(Console.ReadLine());
        }
        catch (IOException)
        {
            return Task.FromResult(Console.ReadLine());
        }
    }

    private string? ReadInteractiveLine(
        string prompt,
        Func<DirectoryInfo> getWorkingDirectory,
        Func<IReadOnlyList<string>> getHistory,
        Func<IReadOnlyList<CommandDescriptor>> getCommands,
        Func<IReadOnlyList<string>> getAliases,
        CancellationToken cancellationToken)
    {
        var originalTreatControlCAsInput = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;

        try
        {
            var buffer = new LineEditBuffer();
            IReadOnlyList<string> historySnapshot = [];
            int? historyIndex = null;
            string draftBeforeHistoryNavigation = string.Empty;
            _previousRenderLength = 0;
            RenderLine(prompt, buffer);

            while (!cancellationToken.IsCancellationRequested)
            {
                var key = Console.ReadKey(intercept: true);

                if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key is ConsoleKey.C or ConsoleKey.D)
                {
                    if (key.Key == ConsoleKey.C)
                    {
                        buffer.Clear();
                        ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
                        RenderLine(prompt, buffer);
                        continue;
                    }

                    ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
                    if (buffer.Length == 0)
                    {
                        Console.WriteLine();
                        return null;
                    }
                }

                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return buffer.Text;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
                    if (buffer.Backspace())
                    {
                        RenderLine(prompt, buffer);
                    }

                    continue;
                }

                if (key.Key == ConsoleKey.Delete)
                {
                    ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
                    if (buffer.Delete())
                    {
                        RenderLine(prompt, buffer);
                    }

                    continue;
                }

                if (key.Key == ConsoleKey.Tab)
                {
                    ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
                    if (TryComplete(
                        buffer.Text,
                        getWorkingDirectory,
                        getCommands,
                        getAliases,
                        out var completion) &&
                        completion is not null)
                    {
                        ApplyCompletion(prompt, buffer, completion);
                    }

                    continue;
                }

                if (key.Key == ConsoleKey.UpArrow)
                {
                    if (TryMoveHistoryBackward(buffer, getHistory, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation))
                    {
                        RenderLine(prompt, buffer);
                    }

                    continue;
                }

                if (key.Key == ConsoleKey.DownArrow)
                {
                    if (TryMoveHistoryForward(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation))
                    {
                        RenderLine(prompt, buffer);
                    }

                    continue;
                }

                if (key.Key == ConsoleKey.LeftArrow)
                {
                    ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
                    if (buffer.MoveLeft())
                    {
                        RenderLine(prompt, buffer);
                    }

                    continue;
                }

                if (key.Key == ConsoleKey.RightArrow)
                {
                    ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
                    if (buffer.MoveRight())
                    {
                        RenderLine(prompt, buffer);
                    }

                    continue;
                }

                if (key.Key == ConsoleKey.Home)
                {
                    ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
                    if (buffer.MoveHome())
                    {
                        RenderLine(prompt, buffer);
                    }

                    continue;
                }

                if (key.Key == ConsoleKey.End)
                {
                    ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
                    if (buffer.MoveEnd())
                    {
                        RenderLine(prompt, buffer);
                    }

                    continue;
                }

                var character = key.KeyChar;
                if (character == '\0' || char.IsControl(character))
                {
                    continue;
                }

                ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
                buffer.Insert(character);
                RenderLine(prompt, buffer);
            }

            Console.WriteLine();
            return null;
        }
        finally
        {
            Console.TreatControlCAsInput = originalTreatControlCAsInput;
        }
    }

    private bool TryComplete(
        string input,
        Func<DirectoryInfo> getWorkingDirectory,
        Func<IReadOnlyList<CommandDescriptor>> getCommands,
        Func<IReadOnlyList<string>> getAliases,
        out ILineCompletionResult? result)
    {
        if (_commandCompletionService.TryComplete(input, getCommands, getAliases, out var commandCompletion))
        {
            result = commandCompletion;
            return true;
        }

        if (_pathCompletionService.TryComplete(input, getWorkingDirectory, out var pathCompletion))
        {
            result = pathCompletion;
            return true;
        }

        result = null;
        return false;
    }

    private void ApplyCompletion(
        string prompt,
        LineEditBuffer buffer,
        ILineCompletionResult completion)
    {
        if (completion.UpdatedLine is not null)
        {
            buffer.Replace(completion.UpdatedLine);
            RenderLine(prompt, buffer);
            return;
        }

        if (!completion.ShowCandidates)
        {
            return;
        }

        Console.WriteLine();
        foreach (var candidate in completion.Candidates)
        {
            Console.WriteLine(candidate);
        }

        RenderLine(prompt, buffer);
    }

    private static bool TryMoveHistoryBackward(
        LineEditBuffer buffer,
        Func<IReadOnlyList<string>> getHistory,
        ref IReadOnlyList<string> historySnapshot,
        ref int? historyIndex,
        ref string draftBeforeHistoryNavigation)
    {
        if (historyIndex is null)
        {
            historySnapshot = getHistory();
            if (historySnapshot.Count == 0)
            {
                return false;
            }

            draftBeforeHistoryNavigation = buffer.Text;
            historyIndex = historySnapshot.Count - 1;
        }
        else if (historyIndex.Value > 0)
        {
            historyIndex--;
        }

        if (historyIndex is null)
        {
            return false;
        }

        ReplaceBuffer(buffer, historySnapshot[historyIndex.Value]);
        return true;
    }

    private static bool TryMoveHistoryForward(
        LineEditBuffer buffer,
        ref IReadOnlyList<string> historySnapshot,
        ref int? historyIndex,
        ref string draftBeforeHistoryNavigation)
    {
        if (historyIndex is null)
        {
            return false;
        }

        if (historyIndex.Value < historySnapshot.Count - 1)
        {
            historyIndex++;
            ReplaceBuffer(buffer, historySnapshot[historyIndex.Value]);
            return true;
        }

        ReplaceBuffer(buffer, draftBeforeHistoryNavigation);
        historyIndex = null;
        historySnapshot = [];
        draftBeforeHistoryNavigation = string.Empty;
        return true;
    }

    private static void ExitHistoryNavigation(
        LineEditBuffer buffer,
        ref IReadOnlyList<string> historySnapshot,
        ref int? historyIndex,
        ref string draftBeforeHistoryNavigation)
    {
        if (historyIndex is null)
        {
            return;
        }

        historyIndex = null;
        historySnapshot = [];
        draftBeforeHistoryNavigation = buffer.Text;
    }

    private static void ReplaceBuffer(LineEditBuffer buffer, string value)
    {
        buffer.Replace(value);
    }

    private void RenderLine(string prompt, LineEditBuffer buffer)
    {
        var currentLine = prompt + buffer.Text;
        Console.Write("\r");
        Console.Write(currentLine);
        var trailingSpaces = Math.Max(0, _previousRenderLength - currentLine.Length);
        if (trailingSpaces > 0)
        {
            Console.Write(new string(' ', trailingSpaces));
        }

        Console.Write("\r");
        Console.Write(prompt);
        if (buffer.CursorIndex > 0)
        {
            Console.Write(buffer.Text[..buffer.CursorIndex]);
        }

        _previousRenderLength = currentLine.Length;
    }

    private int _previousRenderLength;
}
