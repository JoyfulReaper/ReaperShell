using System.Text;

namespace ReaperShell.Shell;

public sealed class CommandParser
{
    public IReadOnlyList<string> Parse(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';

        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];

            if (quote == '\0')
            {
                if (char.IsWhiteSpace(character))
                {
                    FlushCurrentToken(tokens, current);
                    continue;
                }

                if (character is '"' or '\'')
                {
                    quote = character;
                    continue;
                }

                current.Append(character);
                continue;
            }

            if (character == '\\' && index + 1 < input.Length && input[index + 1] == quote)
            {
                current.Append(quote);
                index++;
                continue;
            }

            if (character == quote)
            {
                quote = '\0';
                continue;
            }

            current.Append(character);
        }

        FlushCurrentToken(tokens, current);
        return tokens;
    }

    private static void FlushCurrentToken(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }
}
