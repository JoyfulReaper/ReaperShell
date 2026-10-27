using System.Runtime.CompilerServices;

namespace ReaperShell.BuiltIns;

internal static class TextReaderExtensions
{
    public static async IAsyncEnumerable<string> ReadLinesAsync(
        this TextReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                yield break;
            }

            yield return line;
        }
    }

    public static async Task<string[]> ReadAllLinesAsync(
        this TextReader reader,
        CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        await foreach (var line in reader.ReadLinesAsync(cancellationToken))
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }
}
