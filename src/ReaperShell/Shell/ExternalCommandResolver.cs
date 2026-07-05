namespace ReaperShell.Shell;

public static class ExternalCommandResolver
{
    public static bool TryResolveExecutable(string commandName, out string executablePath)
    {
        executablePath = string.Empty;

        if (string.IsNullOrWhiteSpace(commandName) ||
            commandName.Contains(Path.DirectorySeparatorChar) ||
            commandName.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var candidateFileNames = GetCandidateFileNames(commandName);
        foreach (var pathEntry in pathEntries)
        {
            foreach (var candidateFileName in candidateFileNames)
            {
                var candidatePath = Path.Combine(pathEntry, candidateFileName);
                if (File.Exists(candidatePath))
                {
                    executablePath = candidatePath;
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<string> GetCandidateFileNames(string commandName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [commandName];
        }

        var candidateFileNames = new List<string> { commandName };
        if (!Path.HasExtension(commandName))
        {
            var pathext = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var extension in pathext)
            {
                var normalizedExtension = extension.StartsWith('.') ? extension : "." + extension;
                candidateFileNames.Add(commandName + normalizedExtension);
            }
        }

        return candidateFileNames;
    }
}
