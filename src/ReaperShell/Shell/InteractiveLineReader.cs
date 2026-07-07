using System.Text;

namespace ReaperShell.Shell;

internal sealed class InteractiveLineReader
{
    private readonly PathCompletionService _pathCompletionService;

    public InteractiveLineReader(PathCompletionService? pathCompletionService = null)
    {
        _pathCompletionService = pathCompletionService ?? new PathCompletionService();
    }

    public Task<string?> ReadLineAsync(
        string prompt,
        Func<DirectoryInfo> getWorkingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return Task.FromResult(Console.ReadLine());
        }

        try
        {
            return Task.FromResult(ReadInteractiveLine(prompt, getWorkingDirectory, cancellationToken));
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
        CancellationToken cancellationToken)
    {
        var originalTreatControlCAsInput = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;

        try
        {
            var buffer = new StringBuilder();
            RenderLine(prompt, buffer);

            while (!cancellationToken.IsCancellationRequested)
            {
                var key = Console.ReadKey(intercept: true);

                if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key is ConsoleKey.C or ConsoleKey.D)
                {
                    if (key.Key == ConsoleKey.C)
                    {
                        buffer.Clear();
                        RenderLine(prompt, buffer);
                        continue;
                    }

                    if (buffer.Length == 0)
                    {
                        Console.WriteLine();
                        return null;
                    }
                }

                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return buffer.ToString();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Length--;
                        RenderLine(prompt, buffer);
                    }

                    continue;
                }

                if (key.Key == ConsoleKey.Tab)
                {
                    if (_pathCompletionService.TryComplete(buffer.ToString(), getWorkingDirectory, out var completion))
                    {
                        if (completion is not null && completion.UpdatedLine is not null)
                        {
                            buffer.Clear();
                            buffer.Append(completion.UpdatedLine);
                            RenderLine(prompt, buffer);
                        }
                        else if (completion is not null && completion.ShowCandidates)
                        {
                            Console.WriteLine();
                            foreach (var candidate in completion.Candidates)
                            {
                                Console.WriteLine(candidate);
                            }

                            RenderLine(prompt, buffer);
                        }
                    }

                    continue;
                }

                var character = key.KeyChar;
                if (character == '\0' || char.IsControl(character))
                {
                    continue;
                }

                buffer.Append(character);
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

    private void RenderLine(string prompt, StringBuilder buffer)
    {
        var currentLine = prompt + buffer;
        Console.Write("\r");
        Console.Write(currentLine);
        var trailingSpaces = Math.Max(0, _previousRenderLength - currentLine.Length);
        if (trailingSpaces > 0)
        {
            Console.Write(new string(' ', trailingSpaces));
            Console.Write("\r");
            Console.Write(currentLine);
        }

        _previousRenderLength = currentLine.Length;
    }

    private int _previousRenderLength;
}
