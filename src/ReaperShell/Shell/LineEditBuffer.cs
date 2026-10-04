using System.Text;

namespace ReaperShell.Shell;

internal sealed class LineEditBuffer
{
    private readonly StringBuilder _buffer = new();

    public int CursorIndex { get; private set; }

    public int Length => _buffer.Length;

    public string Text => _buffer.ToString();

    public void Insert(char character)
    {
        _buffer.Insert(CursorIndex, character);
        CursorIndex++;
    }

    public bool MoveLeft()
    {
        if (CursorIndex == 0)
        {
            return false;
        }

        CursorIndex--;
        return true;
    }

    public bool MoveRight()
    {
        if (CursorIndex >= _buffer.Length)
        {
            return false;
        }

        CursorIndex++;
        return true;
    }

    public bool MoveHome()
    {
        if (CursorIndex == 0)
        {
            return false;
        }

        CursorIndex = 0;
        return true;
    }

    public bool MoveEnd()
    {
        if (CursorIndex == _buffer.Length)
        {
            return false;
        }

        CursorIndex = _buffer.Length;
        return true;
    }

    public bool Backspace()
    {
        if (CursorIndex == 0)
        {
            return false;
        }

        _buffer.Remove(CursorIndex - 1, 1);
        CursorIndex--;
        return true;
    }

    public bool Delete()
    {
        if (CursorIndex >= _buffer.Length)
        {
            return false;
        }

        _buffer.Remove(CursorIndex, 1);
        return true;
    }

    public bool DeleteToEnd()
    {
        if (CursorIndex == _buffer.Length)
        {
            return false;
        }

        _buffer.Remove(CursorIndex, _buffer.Length - CursorIndex);
        return true;
    }

    public bool DeleteToStart()
    {
        if (CursorIndex == 0)
        {
            return false;
        }

        _buffer.Remove(0, CursorIndex);
        CursorIndex = 0;
        return true;
    }

    public bool DeletePreviousWord()
    {
        var start = CursorIndex;
        while (start > 0 && char.IsWhiteSpace(_buffer[start - 1]))
        {
            start--;
        }

        while (start > 0 && !char.IsWhiteSpace(_buffer[start - 1]))
        {
            start--;
        }

        if (start == CursorIndex)
        {
            return false;
        }

        _buffer.Remove(start, CursorIndex - start);
        CursorIndex = start;
        return true;
    }

    public void Replace(string text)
    {
        _buffer.Clear();
        _buffer.Append(text);
        CursorIndex = _buffer.Length;
    }

    public void Clear()
    {
        _buffer.Clear();
        CursorIndex = 0;
    }
}
