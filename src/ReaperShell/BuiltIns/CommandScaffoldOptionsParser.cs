using ReaperShell.Abstractions;
using ReaperShell.Shell;

namespace ReaperShell.BuiltIns;

internal sealed class CommandScaffoldOptionsParser
{
    public bool TryParseNewCommandArgs(
        ShellContext context,
        IReadOnlyList<string> args,
        out NewCommandOptions options)
    {
        options = default!;
        if (args.Count < 3)
        {
            context.WriteErrorLine(CommandCommandUsage.New);
            return false;
        }

        var repoName = args[1];
        var commandName = args[2];
        if (!TryValidateCommandName(commandName, context, out var validatedCommandName))
        {
            return false;
        }

        var template = ScaffoldTemplate.Basic;
        var language = ScaffoldLanguage.CSharp;

        for (var index = 3; index < args.Count; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--template", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    context.WriteErrorLine("Missing value for --template.");
                    return false;
                }

                if (!TryParseTemplate(args[++index], out template))
                {
                    context.WriteErrorLine($"Unknown template: {args[index]}");
                    return false;
                }

                continue;
            }

            if (string.Equals(arg, "--language", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Count || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    context.WriteErrorLine("Missing value for --language.");
                    return false;
                }

                if (!TryParseLanguage(args[++index], out language))
                {
                    context.WriteErrorLine($"Unknown language: {args[index]}");
                    return false;
                }

                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal) && arg is not "-")
            {
                context.WriteErrorLine($"Unknown option: {arg}");
                return false;
            }

            context.WriteErrorLine($"Unexpected argument: {arg}");
            return false;
        }

        options = new NewCommandOptions(repoName, validatedCommandName, template, language);
        return true;
    }

    private static bool TryValidateCommandName(
        string candidate,
        ShellContext context,
        out string commandName)
    {
        commandName = candidate;
        if (!ShellNameValidator.IsLowerKebabCaseName(candidate))
        {
            context.WriteErrorLine("Command names must start with a lowercase letter and use lowercase kebab-case.");
            return false;
        }

        return true;
    }

    private static bool TryParseTemplate(string value, out ScaffoldTemplate template)
    {
        switch (value.ToLowerInvariant())
        {
            case "basic":
                template = ScaffoldTemplate.Basic;
                return true;
            case "file":
                template = ScaffoldTemplate.File;
                return true;
            case "process":
                template = ScaffoldTemplate.Process;
                return true;
            default:
                template = ScaffoldTemplate.Basic;
                return false;
        }
    }

    private static bool TryParseLanguage(string value, out ScaffoldLanguage language)
    {
        switch (value.ToLowerInvariant())
        {
            case "csharp":
            case "cs":
            case "c#":
                language = ScaffoldLanguage.CSharp;
                return true;
            case "fsharp":
            case "fs":
            case "f#":
                language = ScaffoldLanguage.FSharp;
                return true;
            case "vb":
            case "vbnet":
            case "visualbasic":
            case "visual-basic":
                language = ScaffoldLanguage.VisualBasic;
                return true;
            default:
                language = ScaffoldLanguage.CSharp;
                return false;
        }
    }
}
