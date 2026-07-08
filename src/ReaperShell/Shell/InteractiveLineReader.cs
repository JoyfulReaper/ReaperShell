using System.Text;
using ReaperShell.Abstractions;

namespace ReaperShell.Shell;

internal sealed class InteractiveLineReader
{
    private readonly IInteractiveConsole _console;
    private readonly CommandCompletionService _commandCompletionService;
    private readonly PathCompletionService _pathCompletionService;

    public InteractiveLineReader(
        CommandCompletionService? commandCompletionService = null,
        PathCompletionService? pathCompletionService = null)
        : this(new SystemInteractiveConsole(), commandCompletionService, pathCompletionService)
    {
    }

    internal InteractiveLineReader(
        IInteractiveConsole console,
        CommandCompletionService? commandCompletionService = null,
        PathCompletionService? pathCompletionService = null)
    {
        _console = console;
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
        if (_console.IsInputRedirected)
        {
            return Task.FromResult(_console.ReadLine());
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
            return Task.FromResult(_console.ReadLine());
        }
        catch (IOException)
        {
            return Task.FromResult(_console.ReadLine());
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
        var originalTreatControlCAsInput = _console.TreatControlCAsInput;
        _console.TreatControlCAsInput = true;

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
                var key = _console.ReadKey(intercept: true);

                var initialAction = ProcessKey(
                    key,
                    prompt,
                    buffer,
                    getWorkingDirectory,
                    getHistory,
                    getCommands,
                    getAliases,
                    ref historySnapshot,
                    ref historyIndex,
                    ref draftBeforeHistoryNavigation);

                switch (initialAction)
                {
                    case LineKeyAction.Submit:
                        return buffer.Text;
                    case LineKeyAction.Exit:
                        return null;
                    case LineKeyAction.Render:
                    case LineKeyAction.None:
                        break;
                }

                var character = key.KeyChar;
                if (!IsPlainPrintable(character))
                {
                    continue;
                }

                ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
                buffer.Insert(character);

                if (TryConsumePasteBurst(
                    prompt,
                    buffer,
                    getWorkingDirectory,
                    getHistory,
                    getCommands,
                    getAliases,
                    ref historySnapshot,
                    ref historyIndex,
                    ref draftBeforeHistoryNavigation,
                    out var batchAction))
                {
                    if (batchAction == LineKeyAction.Exit)
                    {
                        return null;
                    }

                    if (batchAction == LineKeyAction.Submit)
                    {
                        return buffer.Text;
                    }

                    if (batchAction == LineKeyAction.None)
                    {
                        RenderLine(prompt, buffer);
                    }

                    continue;
                }

                RenderLine(prompt, buffer);
            }

            _console.WriteLine();
            return null;
        }
        finally
        {
            _console.TreatControlCAsInput = originalTreatControlCAsInput;
        }
    }

    private bool TryConsumePasteBurst(
        string prompt,
        LineEditBuffer buffer,
        Func<DirectoryInfo> getWorkingDirectory,
        Func<IReadOnlyList<string>> getHistory,
        Func<IReadOnlyList<CommandDescriptor>> getCommands,
        Func<IReadOnlyList<string>> getAliases,
        ref IReadOnlyList<string> historySnapshot,
        ref int? historyIndex,
        ref string draftBeforeHistoryNavigation,
        out LineKeyAction batchAction)
    {
        batchAction = LineKeyAction.None;

        while (_console.KeyAvailable)
        {
            var nextKey = _console.ReadKey(intercept: true);
            if (IsEnterKey(nextKey))
            {
                RenderLine(prompt, buffer);
                _console.WriteLine();
                batchAction = LineKeyAction.Submit;
                return true;
            }

            if (IsPlainPrintable(nextKey.KeyChar))
            {
                ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
                buffer.Insert(nextKey.KeyChar);
                continue;
            }

            batchAction = ProcessKey(
                nextKey,
                prompt,
                buffer,
                getWorkingDirectory,
                getHistory,
                getCommands,
                getAliases,
                ref historySnapshot,
                ref historyIndex,
                ref draftBeforeHistoryNavigation);
            return batchAction != LineKeyAction.None;
        }

        return false;
    }

    private LineKeyAction ProcessKey(
        ConsoleKeyInfo key,
        string prompt,
        LineEditBuffer buffer,
        Func<DirectoryInfo> getWorkingDirectory,
        Func<IReadOnlyList<string>> getHistory,
        Func<IReadOnlyList<CommandDescriptor>> getCommands,
        Func<IReadOnlyList<string>> getAliases,
        ref IReadOnlyList<string> historySnapshot,
        ref int? historyIndex,
        ref string draftBeforeHistoryNavigation)
    {
        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key is ConsoleKey.C or ConsoleKey.D)
        {
            if (key.Key == ConsoleKey.C)
            {
                buffer.Clear();
                ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
                RenderLine(prompt, buffer);
                return LineKeyAction.Render;
            }

            ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
            if (buffer.Length == 0)
            {
                _console.WriteLine();
                return LineKeyAction.Exit;
            }

            return LineKeyAction.None;
        }

        if (IsEnterKey(key))
        {
            _console.WriteLine();
            return LineKeyAction.Submit;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
            if (buffer.Backspace())
            {
                RenderLine(prompt, buffer);
                return LineKeyAction.Render;
            }

            return LineKeyAction.None;
        }

        if (key.Key == ConsoleKey.Delete)
        {
            ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
            if (buffer.Delete())
            {
                RenderLine(prompt, buffer);
                return LineKeyAction.Render;
            }

            return LineKeyAction.None;
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
                return LineKeyAction.Render;
            }

            return LineKeyAction.None;
        }

        if (key.Key == ConsoleKey.UpArrow)
        {
            if (TryMoveHistoryBackward(buffer, getHistory, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation))
            {
                RenderLine(prompt, buffer);
                return LineKeyAction.Render;
            }

            return LineKeyAction.None;
        }

        if (key.Key == ConsoleKey.DownArrow)
        {
            if (TryMoveHistoryForward(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation))
            {
                RenderLine(prompt, buffer);
                return LineKeyAction.Render;
            }

            return LineKeyAction.None;
        }

        if (key.Key == ConsoleKey.LeftArrow)
        {
            ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
            if (buffer.MoveLeft())
            {
                RenderLine(prompt, buffer);
                return LineKeyAction.Render;
            }

            return LineKeyAction.None;
        }

        if (key.Key == ConsoleKey.RightArrow)
        {
            ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
            if (buffer.MoveRight())
            {
                RenderLine(prompt, buffer);
                return LineKeyAction.Render;
            }

            return LineKeyAction.None;
        }

        if (key.Key == ConsoleKey.Home)
        {
            ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
            if (buffer.MoveHome())
            {
                RenderLine(prompt, buffer);
                return LineKeyAction.Render;
            }

            return LineKeyAction.None;
        }

        if (key.Key == ConsoleKey.End)
        {
            ExitHistoryNavigation(buffer, ref historySnapshot, ref historyIndex, ref draftBeforeHistoryNavigation);
            if (buffer.MoveEnd())
            {
                RenderLine(prompt, buffer);
                return LineKeyAction.Render;
            }

            return LineKeyAction.None;
        }

        return LineKeyAction.None;
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

        _console.WriteLine();
        foreach (var candidate in completion.Candidates)
        {
            _console.WriteLine(candidate);
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
        var cursorTop = _console.CursorTop;
        _console.Write("\r");
        _console.Write(currentLine);
        var trailingSpaces = Math.Max(0, _previousRenderLength - currentLine.Length);
        if (trailingSpaces > 0)
        {
            _console.Write(new string(' ', trailingSpaces));
        }

        if (buffer.CursorIndex < buffer.Length)
        {
            var desiredColumn = Math.Min(
                prompt.Length + buffer.CursorIndex,
                Math.Max(0, _console.BufferWidth - 1));
            _console.SetCursorPosition(desiredColumn, cursorTop);
        }

        _previousRenderLength = currentLine.Length;
    }

    private static bool IsEnterKey(ConsoleKeyInfo key)
    {
        return key.Key == ConsoleKey.Enter || key.KeyChar is '\r' or '\n';
    }

    private static bool IsPlainPrintable(char character)
    {
        return character != '\0' && !char.IsControl(character);
    }

    private enum LineKeyAction
    {
        None,
        Render,
        Submit,
        Exit
    }

    private int _previousRenderLength;
}
