using System.Text;

namespace ReaperShell.Shell;

public sealed class ShellCommandLineParser
{
    private static readonly HashSet<string> RedirectionOperators = new(StringComparer.Ordinal)
    {
        ">",
        ">>",
        "2>",
        "2>>",
        "*>",
        "*>>"
    };

    public bool TryParse(string input, out CommandLine? commandLine, out string errorMessage)
    {
        commandLine = null;
        errorMessage = string.Empty;

        if (!TryTokenize(input, out var tokens, out errorMessage))
        {
            return false;
        }

        if (tokens.Count == 0)
        {
            commandLine = new CommandLine([]);
            return true;
        }

        var pipelines = new List<CommandPipeline>();
        var currentSegments = new List<CommandSegment>();
        var currentTokens = new List<string>();
        var currentRedirections = new List<CommandRedirection>();

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Kind == ShellCommandLineTokenKind.Word)
            {
                currentTokens.Add(token.Value);
                continue;
            }

            if (TryGetRedirectionKind(token.Value, out var redirectionKind))
            {
                if (!TryReadRedirectionTarget(tokens, ref index, out var targetPath, out errorMessage))
                {
                    return false;
                }

                currentRedirections.Add(new CommandRedirection(redirectionKind, targetPath));
                continue;
            }

            if (token.Value is "|" or "&&" or "||")
            {
                if (!TryFinalizeSegment(currentTokens, currentRedirections, out var segment, out errorMessage))
                {
                    return false;
                }

                currentSegments.Add(segment);
                currentTokens = [];
                currentRedirections = [];

                if (token.Value == "|")
                {
                    continue;
                }

                if (currentSegments.Count == 0)
                {
                    errorMessage = "Pipeline segment cannot be empty.";
                    return false;
                }

                pipelines.Add(new CommandPipeline(currentSegments.ToArray(), token.Value == "&&"
                    ? CommandChainOperator.AndAlso
                    : CommandChainOperator.OrElse));
                currentSegments = [];
                continue;
            }

            errorMessage = $"Unsupported shell operator: '{token.Value}'.";
            return false;
        }

        if (!TryFinalizeSegment(currentTokens, currentRedirections, out var finalSegment, out errorMessage))
        {
            return false;
        }

        currentSegments.Add(finalSegment);

        if (currentSegments.Count == 0)
        {
            errorMessage = "Pipeline segment cannot be empty.";
            return false;
        }

        pipelines.Add(new CommandPipeline(currentSegments.ToArray(), null));
        commandLine = new CommandLine(pipelines.ToArray());
        return true;
    }

    private static bool TryFinalizeSegment(
        List<string> currentTokens,
        List<CommandRedirection> currentRedirections,
        out CommandSegment segment,
        out string errorMessage)
    {
        if (currentTokens.Count == 0)
        {
            segment = null!;
            errorMessage = "Pipeline segment cannot be empty.";
            return false;
        }

        segment = new CommandSegment(currentTokens.ToArray(), currentRedirections.ToArray());
        errorMessage = string.Empty;
        return true;
    }

    private static bool TryReadRedirectionTarget(
        IReadOnlyList<ShellCommandLineToken> tokens,
        ref int index,
        out string targetPath,
        out string errorMessage)
    {
        targetPath = string.Empty;
        errorMessage = string.Empty;

        if (index + 1 >= tokens.Count)
        {
            errorMessage = $"Missing redirection target after '{tokens[index].Value}'.";
            return false;
        }

        var targetToken = tokens[index + 1];
        if (targetToken.Kind != ShellCommandLineTokenKind.Word)
        {
            errorMessage = $"Missing redirection target after '{tokens[index].Value}'.";
            return false;
        }

        targetPath = targetToken.Value;
        index++;
        return true;
    }

    private static bool TryGetRedirectionKind(string value, out CommandRedirectionKind kind)
    {
        kind = value switch
        {
            ">" => CommandRedirectionKind.StdoutOverwrite,
            ">>" => CommandRedirectionKind.StdoutAppend,
            "2>" => CommandRedirectionKind.StderrOverwrite,
            "2>>" => CommandRedirectionKind.StderrAppend,
            "*>" => CommandRedirectionKind.CombinedOverwrite,
            "*>>" => CommandRedirectionKind.CombinedAppend,
            _ => default
        };

        return RedirectionOperators.Contains(value);
    }

    private static bool TryTokenize(
        string input,
        out List<ShellCommandLineToken> tokens,
        out string errorMessage)
    {
        var tokenList = new List<ShellCommandLineToken>();
        tokens = tokenList;
        errorMessage = string.Empty;
        var current = new StringBuilder();
        var quote = '\0';

        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];
            if (quote != '\0')
            {
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
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                FlushWord();
                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (TryReadOperator(input, index, out var operatorText, out var consumed, out var invalidOperator))
            {
                FlushWord();
                if (invalidOperator is not null)
                {
                    errorMessage = $"Unsupported shell operator: '{invalidOperator}'.";
                    return false;
                }

                tokenList.Add(new ShellCommandLineToken(ShellCommandLineTokenKind.Operator, operatorText));
                index += consumed - 1;
                continue;
            }

            if (invalidOperator is not null)
            {
                FlushWord();
                errorMessage = $"Unsupported shell operator: '{invalidOperator}'.";
                return false;
            }

            current.Append(character);
        }

        if (quote != '\0')
        {
            errorMessage = "Unterminated quoted string.";
            return false;
        }

        FlushWord();
        return true;

        void FlushWord()
        {
            if (current.Length == 0)
            {
                return;
            }

            tokenList.Add(new ShellCommandLineToken(ShellCommandLineTokenKind.Word, current.ToString()));
            current.Clear();
        }
    }

    private static bool TryReadOperator(
        string input,
        int index,
        out string operatorText,
        out int consumed,
        out string? invalidOperator)
    {
        operatorText = string.Empty;
        consumed = 0;
        invalidOperator = null;

        var character = input[index];
        switch (character)
        {
            case '|':
                if (index + 1 < input.Length && input[index + 1] == '|')
                {
                    operatorText = "||";
                    consumed = 2;
                    return true;
                }

                operatorText = "|";
                consumed = 1;
                return true;

            case '&':
                if (index + 1 < input.Length && input[index + 1] == '&')
                {
                    operatorText = "&&";
                    consumed = 2;
                    return true;
                }

                invalidOperator = "&";
                return false;

            case '>':
                if (index + 1 < input.Length && input[index + 1] == '>')
                {
                    operatorText = ">>";
                    consumed = 2;
                    return true;
                }

                operatorText = ">";
                consumed = 1;
                return true;

            case '*':
                if (index + 1 < input.Length && input[index + 1] == '>')
                {
                    if (index + 2 < input.Length && input[index + 2] == '>')
                    {
                        operatorText = "*>>";
                        consumed = 3;
                        return true;
                    }

                    operatorText = "*>";
                    consumed = 2;
                    return true;
                }

                return false;

            case ';':
                invalidOperator = ";";
                return false;

            case '2':
                if (index + 1 < input.Length && input[index + 1] == '>')
                {
                    if (index + 2 < input.Length && input[index + 2] == '>')
                    {
                        operatorText = "2>>";
                        consumed = 3;
                        return true;
                    }

                    operatorText = "2>";
                    consumed = 2;
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private enum ShellCommandLineTokenKind
    {
        Word,
        Operator
    }

    private sealed record ShellCommandLineToken(ShellCommandLineTokenKind Kind, string Value);
}
