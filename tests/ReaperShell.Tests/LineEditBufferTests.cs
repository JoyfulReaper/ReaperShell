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

    [Theory]
    [InlineData("abc", 1, "a", true)]
    [InlineData("abc", 0, "", true)]
    [InlineData("abc", 3, "abc", false)]
    [InlineData("", 0, "", false)]
    public void DeleteToEndPreservesCursor(string text, int cursor, string expected, bool changed)
    {
        var buffer = CreateBuffer(text);
        while (buffer.CursorIndex > cursor)
        {
            buffer.MoveLeft();
        }

        Assert.Equal(changed, buffer.DeleteToEnd());
        Assert.Equal(expected, buffer.Text);
        Assert.Equal(cursor, buffer.CursorIndex);
    }

    [Theory]
    [InlineData("abc", 1, "bc", true)]
    [InlineData("abc", 3, "", true)]
    [InlineData("abc", 0, "abc", false)]
    [InlineData("", 0, "", false)]
    public void DeleteToStartPreservesSuffix(string text, int cursor, string expected, bool changed)
    {
        var buffer = CreateBuffer(text);
        while (buffer.CursorIndex > cursor)
        {
            buffer.MoveLeft();
        }

        Assert.Equal(changed, buffer.DeleteToStart());
        Assert.Equal(expected, buffer.Text);
        Assert.Equal(0, buffer.CursorIndex);
    }

    [Theory]
    [InlineData("one two", 7, "one ", 4)]
    [InlineData("one   two", 9, "one   ", 6)]
    [InlineData("one   two", 6, "two", 0)]
    [InlineData("one two   ", 10, "one ", 4)]
    [InlineData("one two three", 6, "one o three", 4)]
    [InlineData("one two", 0, "one two", 0)]
    [InlineData("   ", 3, "", 0)]
    [InlineData("one\t two\t ", 10, "one\t ", 5)]
    [InlineData("one 'two-three'", 15, "one ", 4)]
    [InlineData("", 0, "", 0)]
    public void DeletePreviousWordUsesWhitespaceBoundaries(string text, int cursor, string expected, int expectedCursor)
    {
        var buffer = CreateBuffer(text);
        while (buffer.CursorIndex > cursor)
        {
            buffer.MoveLeft();
        }

        Assert.Equal(cursor > 0, buffer.DeletePreviousWord());
        Assert.Equal(expected, buffer.Text);
        Assert.Equal(expectedCursor, buffer.CursorIndex);
    }

    private static LineEditBuffer CreateBuffer(string text)
    {
        var buffer = new LineEditBuffer();
        buffer.Replace(text);
        return buffer;
    }
}
