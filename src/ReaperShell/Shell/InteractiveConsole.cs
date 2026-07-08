namespace ReaperShell.Shell;

internal interface IInteractiveConsole
{
    bool IsInputRedirected { get; }

    bool KeyAvailable { get; }

    bool TreatControlCAsInput { get; set; }

    ConsoleKeyInfo ReadKey(bool intercept);

    string? ReadLine();

    void Write(string value);

    void WriteLine();

    void WriteLine(string value);
}

internal sealed class SystemInteractiveConsole : IInteractiveConsole
{
    public bool IsInputRedirected => Console.IsInputRedirected;

    public bool KeyAvailable => Console.KeyAvailable;

    public bool TreatControlCAsInput
    {
        get => Console.TreatControlCAsInput;
        set => Console.TreatControlCAsInput = value;
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        return Console.ReadKey(intercept);
    }

    public string? ReadLine()
    {
        return Console.ReadLine();
    }

    public void Write(string value)
    {
        Console.Write(value);
    }

    public void WriteLine()
    {
        Console.WriteLine();
    }

    public void WriteLine(string value)
    {
        Console.WriteLine(value);
    }
}
