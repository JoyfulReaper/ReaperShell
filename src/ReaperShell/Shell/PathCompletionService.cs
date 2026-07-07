using System.Text;

namespace ReaperShell.Shell;

internal sealed class PathCompletionService
{
    public bool TryComplete(
        string input,
        Func<DirectoryInfo> getWorkingDirectory,
        out PathCompletionResult? result)
    {
        result = null;

        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        if (!TryGetCurrentToken(input, out var token))
        {
            return false;
        }

        var hasPriorContent = token.StartIndex > 0 &&
            input[..token.StartIndex].Any(character => !char.IsWhiteSpace(character));
        if (!hasPriorContent && !LooksLikePathToken(token.Fragment))
        {
            return false;
        }

        var workingDirectory = getWorkingDirectory();
        if (!TryGetCompletionCandidates(token.Fragment, workingDirectory, out var candidates))
        {
            return false;
        }

        var completion = BuildCompletion(input, token, candidates);
        result = completion;
        return true;
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

    private static bool LooksLikePathToken(string fragment)
    {
        return fragment.Length == 0 ||
               fragment.Contains(Path.DirectorySeparatorChar) ||
               fragment.Contains(Path.AltDirectorySeparatorChar) ||
               fragment.StartsWith('.') ||
               fragment.StartsWith('~') ||
               fragment.Contains(':');
    }

    private static bool TryGetCompletionCandidates(
        string fragment,
        DirectoryInfo workingDirectory,
        out IReadOnlyList<CompletionCandidate> candidates)
    {
        candidates = Array.Empty<CompletionCandidate>();

        var directoryPart = GetDirectoryPart(fragment);
        var namePrefix = fragment[directoryPart.Length..];
        var searchDirectory = ResolveSearchDirectory(directoryPart, workingDirectory);
        if (searchDirectory is null || !searchDirectory.Exists)
        {
            return false;
        }

        var comparison = PathComparison();
        var matches = new List<CompletionCandidate>();
        foreach (var entryPath in Directory.EnumerateFileSystemEntries(searchDirectory.FullName))
        {
            var entryName = Path.GetFileName(entryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var displayName = Directory.Exists(entryPath)
                ? entryName + Path.DirectorySeparatorChar
                : entryName;

            if (!displayName.StartsWith(namePrefix, comparison))
            {
                continue;
            }

            matches.Add(new CompletionCandidate(displayName));
        }

        if (matches.Count == 0)
        {
            return false;
        }

        candidates = matches
            .OrderBy(candidate => candidate.DisplayName, PathComparer(comparison))
            .ToArray();
        return true;
    }

    private static PathCompletionResult BuildCompletion(
        string input,
        TokenSpan token,
        IReadOnlyList<CompletionCandidate> candidates)
    {
        var directoryPart = GetDirectoryPart(token.Fragment);
        var namePrefix = token.Fragment[directoryPart.Length..];
        var comparison = PathComparison();

        if (candidates.Count == 1)
        {
            var completedFragment = NormalizePathSeparators(directoryPart + candidates[0].DisplayName);
            var completedToken = QuoteIfNeeded(completedFragment, token.IsQuoted);
            return new PathCompletionResult(
                input[..token.StartIndex] + completedToken,
                false,
                Array.Empty<string>());
        }

        var commonPrefix = FindCommonPrefix(candidates.Select(candidate => candidate.DisplayName), comparison);
        if (commonPrefix.Length > namePrefix.Length)
        {
            var completedFragment = NormalizePathSeparators(directoryPart + commonPrefix);
            var completedToken = QuoteIfNeeded(completedFragment, token.IsQuoted);
            return new PathCompletionResult(
                input[..token.StartIndex] + completedToken,
                false,
                Array.Empty<string>());
        }

        return new PathCompletionResult(
            null,
            true,
            candidates.Select(candidate => candidate.DisplayName).ToArray());
    }

    private static string QuoteIfNeeded(string value, bool alreadyQuoted)
    {
        if (alreadyQuoted || value.Any(char.IsWhiteSpace))
        {
            return $"\"{value}\"";
        }

        return value;
    }

    private static DirectoryInfo? ResolveSearchDirectory(string directoryPart, DirectoryInfo workingDirectory)
    {
        try
        {
            var directoryPath = string.IsNullOrEmpty(directoryPart)
                ? workingDirectory.FullName
                : Path.GetFullPath(directoryPart, workingDirectory.FullName);

            return new DirectoryInfo(directoryPath);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizePathSeparators(string path)
    {
        if (Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar)
        {
            return path;
        }

        return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static string GetDirectoryPart(string fragment)
    {
        var lastForwardSlash = fragment.LastIndexOf(Path.DirectorySeparatorChar);
        var lastAltForwardSlash = fragment.LastIndexOf(Path.AltDirectorySeparatorChar);
        var separatorIndex = Math.Max(lastForwardSlash, lastAltForwardSlash);
        return separatorIndex < 0 ? string.Empty : fragment[..(separatorIndex + 1)];
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

    private sealed record TokenSpan(int StartIndex, string Fragment, char QuoteChar)
    {
        public bool IsQuoted => QuoteChar != '\0';
    }

    private sealed record CompletionCandidate(string DisplayName);
}

internal sealed record PathCompletionResult(
    string? UpdatedLine,
    bool ShowCandidates,
    IReadOnlyList<string> Candidates);
