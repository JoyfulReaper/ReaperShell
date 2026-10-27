using ReaperShell.Abstractions;

namespace ReaperShell.Shell;

internal sealed class CommandCompletionService
{
    private static readonly IReadOnlyDictionary<string, string[]> Subcommands = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["repo"] =
        [
            "add",
            "list",
            "prune-duplicates",
            "trust",
            "untrust",
            "status",
            "branches",
            "sync",
            "switch",
            "pull",
            "build",
            "load",
            "unload",
            "reload",
            "new",
            "remove",
            "commit",
            "push",
            "publish",
            "save",
            "build-all",
            "load-all",
            "reload-all",
            "autosync",
            "watch",
            "unwatch",
            "watch-list"
        ],
        ["command"] = ["templates", "list", "new", "remove", "delete", "rm"],
        ["alias"] = ["set", "remove", "clear", "show"],
        ["hook"] = ["list", "add", "remove", "clear", "events"],
        ["ritual"] = ["list", "run", "path", "new"],
        ["curse"] =
        [
            "status",
            "inspect",
            "journal",
            "poke",
            "enable",
            "disable",
            "exorcise",
            "quiet",
            "listen",
            "chatter",
            "set-failure-rate"
        ],
        ["pray"] = ["status", "hard"],
        ["fortune"] = ["read", "status"],
        ["env"] = ["get", "set", "unset"]
    };

    private readonly CommandParser _commandParser = new();

    public bool TryComplete(
        string input,
        Func<IReadOnlyList<CommandDescriptor>> getCommands,
        Func<IReadOnlyList<string>> getAliases,
        out CommandCompletionResult? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (!TryGetCurrentToken(input, out var token))
        {
            return false;
        }

        var tokensBeforeCurrent = _commandParser.Parse(input[..token.StartIndex]);
        if (tokensBeforeCurrent.Count == 0)
        {
            if (token.Fragment.Length == 0)
            {
                return false;
            }

            return TryCompleteNames(
                token,
                getCommands().Select(command => command.Name).Concat(getAliases()),
                input,
                out result);
        }

        if (tokensBeforeCurrent.Count == 1 && TryGetSubcommands(tokensBeforeCurrent[0], out var subcommands))
        {
            return TryCompleteNames(token, subcommands, input, out result);
        }

        return false;
    }

    private static bool TryCompleteNames(
        TokenSpan token,
        IEnumerable<string> names,
        string input,
        out CommandCompletionResult? result)
    {
        result = null;

        var comparison = PathComparison();
        var candidates = names
            .Where(name => name.StartsWith(token.Fragment, comparison))
            .Distinct(PathComparer(comparison))
            .OrderBy(name => name, PathComparer(comparison))
            .ToArray();

        if (candidates.Length == 0)
        {
            return false;
        }

        if (candidates.Length == 1)
        {
            result = new CommandCompletionResult(
                input[..token.StartIndex] + candidates[0],
                false,
                Array.Empty<string>());
            return true;
        }

        var commonPrefix = FindCommonPrefix(candidates, comparison);
        if (commonPrefix.Length > token.Fragment.Length)
        {
            result = new CommandCompletionResult(
                input[..token.StartIndex] + commonPrefix,
                false,
                Array.Empty<string>());
            return true;
        }

        result = new CommandCompletionResult(
            null,
            true,
            candidates);
        return true;
    }

    private static bool TryGetSubcommands(string commandName, out IReadOnlyList<string> subcommands)
    {
        if (Subcommands.TryGetValue(commandName, out var knownSubcommands))
        {
            subcommands = knownSubcommands;
            return true;
        }

        subcommands = Array.Empty<string>();
        return false;
    }

    private static bool TryGetCurrentToken(string input, out TokenSpan token)
    {
        token = new TokenSpan(0, string.Empty, '\0');

        var currentQuote = '\0';
        var tokenStart = -1;
        var tokenQuote = '\0';

        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];

            if (currentQuote == '\0')
            {
                if (char.IsWhiteSpace(character))
                {
                    tokenStart = -1;
                    tokenQuote = '\0';
                    continue;
                }

                if (character is '"' or '\'')
                {
                    if (tokenStart == -1)
                    {
                        tokenStart = index;
                        tokenQuote = character;
                    }

                    currentQuote = character;
                    continue;
                }

                if (tokenStart == -1)
                {
                    tokenStart = index;
                    tokenQuote = '\0';
                }

                continue;
            }

            if (character == '\\' && index + 1 < input.Length && input[index + 1] == currentQuote)
            {
                index++;
                continue;
            }

            if (character == currentQuote)
            {
                currentQuote = '\0';
            }
        }

        if (tokenStart < 0)
        {
            if (input.Any(character => !char.IsWhiteSpace(character)))
            {
                token = new TokenSpan(input.Length, string.Empty, '\0');
                return true;
            }

            return false;
        }

        var rawToken = input[tokenStart..];
        var fragment = tokenQuote == '\0' ? rawToken : rawToken[1..];
        if (tokenQuote != '\0' && fragment.Length > 0 && fragment[^1] == tokenQuote)
        {
            fragment = fragment[..^1];
        }

        token = new TokenSpan(tokenStart, fragment, tokenQuote);
        return true;
    }

    private static StringComparison PathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static StringComparer PathComparer(StringComparison comparison)
    {
        return comparison == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static string FindCommonPrefix(IEnumerable<string> values, StringComparison comparison)
    {
        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return string.Empty;
        }

        var prefix = enumerator.Current;
        while (enumerator.MoveNext())
        {
            prefix = FindCommonPrefix(prefix, enumerator.Current, comparison);
            if (prefix.Length == 0)
            {
                break;
            }
        }

        return prefix;
    }

    private static string FindCommonPrefix(string left, string right, StringComparison comparison)
    {
        var length = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < length && string.Compare(left, index, right, index, 1, comparison) == 0)
        {
            index++;
        }

        return left[..index];
    }

    private sealed record TokenSpan(int StartIndex, string Fragment, char QuoteChar);
}

internal sealed record CommandCompletionResult(
    string? UpdatedLine,
    bool ShowCandidates,
    IReadOnlyList<string> Candidates) : ILineCompletionResult;
