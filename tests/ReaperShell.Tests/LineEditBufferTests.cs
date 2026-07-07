using ReaperShell.Shell;
using Xunit;

namespace ReaperShell.Tests;

public sealed class LineEditBufferTests
{
    [Fact]
    public void InsertsCharactersAtCursor()
    {
        var buffer = CreateBuffer("abc");
        buffer.MoveLeft();
        buffer.MoveLeft();
        buffer.Insert('X');

        Assert.Equal("aXbc", buffer.Text);
        Assert.Equal(2, buffer.CursorIndex);
    }

    [Fact]
    public void LeftArrowStopsAtBeginning()
    {
        var buffer = CreateBuffer("abc");

        Assert.True(buffer.MoveLeft());
        Assert.True(buffer.MoveLeft());
        Assert.True(buffer.MoveLeft());
        Assert.False(buffer.MoveLeft());
        Assert.Equal(0, buffer.CursorIndex);
    }

    [Fact]
    public void RightArrowStopsAtEnd()
    {
        var buffer = CreateBuffer("abc");
        buffer.MoveHome();

        Assert.True(buffer.MoveRight());
        Assert.True(buffer.MoveRight());
        Assert.True(buffer.MoveRight());
        Assert.False(buffer.MoveRight());
        Assert.Equal(3, buffer.CursorIndex);
    }

    [Fact]
    public void HomeMovesToStart()
    {
        var buffer = CreateBuffer("abc");

        Assert.True(buffer.MoveHome());
        Assert.Equal(0, buffer.CursorIndex);
    }

    [Fact]
    public void EndMovesToEnd()
    {
        var buffer = CreateBuffer("abc");
        buffer.MoveHome();

        Assert.True(buffer.MoveEnd());
        Assert.Equal(3, buffer.CursorIndex);
    }

    [Fact]
    public void BackspaceDeletesBeforeCursor()
    {
        var buffer = CreateBuffer("abc");
        buffer.MoveLeft();

        Assert.True(buffer.Backspace());
        Assert.Equal("ac", buffer.Text);
        Assert.Equal(1, buffer.CursorIndex);
    }

    [Fact]
    public void DeleteDeletesAtCursor()
    {
        var buffer = CreateBuffer("abc");
        buffer.MoveHome();
        buffer.MoveRight();

        Assert.True(buffer.Delete());
        Assert.Equal("ac", buffer.Text);
        Assert.Equal(1, buffer.CursorIndex);
    }

    [Fact]
    public void ReplacePlacesCursorAtEnd()
    {
        var buffer = CreateBuffer("draft");

        buffer.Replace("repo status iis-tools");

        Assert.Equal("repo status iis-tools", buffer.Text);
        Assert.Equal(buffer.Text.Length, buffer.CursorIndex);
    }

    private static LineEditBuffer CreateBuffer(string text)
    {
        var buffer = new LineEditBuffer();
        buffer.Replace(text);
        return buffer;
    }
}
