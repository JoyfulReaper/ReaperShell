namespace ReaperShell.Abstractions;

public sealed class ShellContext
{
    public ShellContext(
        TextWriter @out,
        TextWriter error,
        DirectoryInfo workingDirectory,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        Out = @out;
        Error = error;
        WorkingDirectory = workingDirectory;
        Services = services;
        CancellationToken = cancellationToken;
    }

    public TextWriter Out { get; }

    public TextWriter Error { get; }

    public DirectoryInfo WorkingDirectory { get; set; }

    public IServiceProvider? Services { get; }

    public CancellationToken CancellationToken { get; }

    public void WriteLine(string message)
    {
        Out.WriteLine(message);
    }

    public void WriteErrorLine(string message)
    {
        Error.WriteLine(message);
    }
}
