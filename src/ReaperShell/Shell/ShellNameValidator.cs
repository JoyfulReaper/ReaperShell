namespace ReaperShell.Shell;

public static class ShellNameValidator
{
    public static bool IsLowerKebabCaseName(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate[0] is < 'a' or > 'z')
        {
            return false;
        }

        if (candidate.Contains("--", StringComparison.Ordinal) ||
            candidate.Contains('.') ||
            candidate.Contains(Path.DirectorySeparatorChar) ||
            candidate.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        for (var index = 0; index < candidate.Length; index++)
        {
            var character = candidate[index];
            if (character is >= 'a' and <= 'z' || character is >= '0' and <= '9')
            {
                continue;
            }

            if (character == '-' && index > 0 && index < candidate.Length - 1)
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
