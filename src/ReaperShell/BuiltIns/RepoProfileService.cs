namespace ReaperShell.BuiltIns;

internal static class RepoProfileService
{
    internal static string GetProfilePath(string stateDirectory)
    {
        return Path.Combine(stateDirectory, "profile.rsh");
    }

    internal static string GetRepoLoadLine(string repoName)
    {
        return $"repo load {repoName}";
    }

    internal static bool HasRepoLoadLine(string profilePath, string repoName)
    {
        if (!File.Exists(profilePath))
        {
            return false;
        }

        var loadLine = GetRepoLoadLine(repoName);
        foreach (var line in File.ReadLines(profilePath))
        {
            if (string.Equals(line.TrimEnd(), loadLine, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static async Task<bool> AppendRepoLoadLineAsync(
        string profilePath,
        string repoName,
        CancellationToken cancellationToken)
    {
        var loadLine = GetRepoLoadLine(repoName);
        if (HasRepoLoadLine(profilePath, repoName))
        {
            return false;
        }

        var profileDirectory = Path.GetDirectoryName(profilePath);
        if (!string.IsNullOrWhiteSpace(profileDirectory))
        {
            Directory.CreateDirectory(profileDirectory);
        }

        var existingText = File.Exists(profilePath)
            ? await File.ReadAllTextAsync(profilePath, cancellationToken)
            : string.Empty;

        if (existingText.Length == 0)
        {
            await File.WriteAllTextAsync(profilePath, loadLine + Environment.NewLine, cancellationToken);
            return true;
        }

        var separator = existingText.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? string.Empty
            : Environment.NewLine;
        await File.WriteAllTextAsync(
            profilePath,
            existingText + separator + loadLine + Environment.NewLine,
            cancellationToken);
        return true;
    }

    internal static async Task<bool> RemoveRepoLoadLineAsync(
        string profilePath,
        string repoName,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(profilePath))
        {
            return false;
        }

        var loadLine = GetRepoLoadLine(repoName);
        var lines = await File.ReadAllLinesAsync(profilePath, cancellationToken);
        var remainingLines = lines.Where(line => !string.Equals(line.TrimEnd(), loadLine, StringComparison.Ordinal)).ToArray();
        if (remainingLines.Length == lines.Length)
        {
            return false;
        }

        await File.WriteAllTextAsync(
            profilePath,
            string.Join(Environment.NewLine, remainingLines) +
            (remainingLines.Length > 0 ? Environment.NewLine : string.Empty),
            cancellationToken);
        return true;
    }
}
