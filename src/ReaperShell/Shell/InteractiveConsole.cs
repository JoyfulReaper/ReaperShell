namespace ReaperShell.Shell;

internal interface IInteractiveConsole
{
    bool IsInputRedirected { get; }

    bool KeyAvailable { get; }

    int CursorLeft { get; set; }

    int CursorTop { get; }

    int BufferWidth { get; }

    bool TreatControlCAsInput { get; set; }

    ConsoleKeyInfo ReadKey(bool intercept);

    string? ReadLine();

    void SetCursorPosition(int left, int top);

    void Write(string value);

    void WriteLine();

    void WriteLine(string value);
}

internal sealed class SystemInteractiveConsole : IInteractiveConsole
{
    public bool IsInputRedirected => Console.IsInputRedirected;

    public bool KeyAvailable => Console.KeyAvailable;

    public int CursorLeft
    {
        get => Console.CursorLeft;
        set => Console.CursorLeft = value;
    }

    public int CursorTop => Console.CursorTop;

    public int BufferWidth => Console.BufferWidth;

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

    public void SetCursorPosition(int left, int top)
    {
        Console.SetCursorPosition(left, top);
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
