namespace ReaperShell.Abstractions;

public enum ShellColorMode
{
    Auto,
    Always,
    Never
}

public enum ShellTextColor
{
    Default,
    Green,
    Yellow,
    Red
}

public sealed class ShellContext
{
    public ShellContext(
        TextWriter @out,
        TextWriter error,
        DirectoryInfo workingDirectory,
        IServiceProvider? services,
        CancellationToken cancellationToken)
        : this(@out, error, TextReader.Null, workingDirectory, services, cancellationToken, ShellColorMode.Auto)
    {
    }

    public ShellContext(
        TextWriter @out,
        TextWriter error,
        TextReader input,
        DirectoryInfo workingDirectory,
        IServiceProvider? services,
        CancellationToken cancellationToken)
        : this(@out, error, input, workingDirectory, services, cancellationToken, ShellColorMode.Auto)
    {
    }

    public ShellContext(
        TextWriter @out,
        TextWriter error,
        DirectoryInfo workingDirectory,
        IServiceProvider? services,
        CancellationToken cancellationToken,
        ShellColorMode colorMode)
        : this(@out, error, TextReader.Null, workingDirectory, services, cancellationToken, colorMode)
    {
    }

    public ShellContext(
        TextWriter @out,
        TextWriter error,
        TextReader input,
        DirectoryInfo workingDirectory,
        IServiceProvider? services,
        CancellationToken cancellationToken,
        ShellColorMode colorMode = ShellColorMode.Auto)
    {
        Out = @out;
        Error = error;
        Input = input;
        WorkingDirectory = workingDirectory;
        Services = services;
        CancellationToken = cancellationToken;
        ColorMode = colorMode;
    }

    public TextWriter Out { get; }

    public TextWriter Error { get; }

    public TextReader Input { get; }

    public DirectoryInfo WorkingDirectory { get; set; }

    public IServiceProvider? Services { get; }

    public CancellationToken CancellationToken { get; }

    public ShellColorMode ColorMode { get; }

    public void WriteLine(string message)
    {
        WriteLine(Out, message, ShellTextColor.Default);
    }

    public void WriteLine(string message, ShellTextColor color)
    {
        WriteLine(Out, message, color);
    }

    public void WriteSuccessLine(string message)
    {
        WriteLine(Out, message, ShellTextColor.Green);
    }

    public void WriteWarningLine(string message)
    {
        WriteLine(Out, message, ShellTextColor.Yellow);
    }

    public void WriteErrorLine(string message)
    {
        WriteLine(Error, message, ShellTextColor.Red);
    }

    private void WriteLine(TextWriter writer, string message, ShellTextColor color)
    {
        if (!ShellColorPolicy.ShouldUseColor(
                ColorMode,
                IsRedirected(writer),
                IsNoColorSet(),
                IsConsoleWriter(writer)))
        {
            writer.WriteLine(message);
            return;
        }

        var consoleColor = color switch
        {
            ShellTextColor.Green => ConsoleColor.Green,
            ShellTextColor.Yellow => ConsoleColor.Yellow,
            ShellTextColor.Red => ConsoleColor.Red,
            _ => (ConsoleColor?)null
        };

        if (consoleColor is null)
        {
            writer.WriteLine(message);
            return;
        }

        lock (ConsoleColorGate)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = consoleColor.Value;
            try
            {
                writer.WriteLine(message);
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }
    }

    private static bool IsConsoleWriter(TextWriter writer)
    {
        return ReferenceEquals(writer, Console.Out) || ReferenceEquals(writer, Console.Error);
    }

    private static bool IsRedirected(TextWriter writer)
    {
        if (ReferenceEquals(writer, Console.Error))
        {
            return Console.IsErrorRedirected;
        }

        return Console.IsOutputRedirected;
    }

    private static bool IsNoColorSet()
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NO_COLOR"));
    }

    private static readonly object ConsoleColorGate = new();
}
