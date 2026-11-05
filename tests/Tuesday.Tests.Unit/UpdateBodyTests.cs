using Tuesday.Domain.Collaboration;
using Tuesday.Infrastructure.Collaboration;

namespace Tuesday.Tests.Unit;

/// <summary>
/// An update body is the only place a user can put markup into the product. These tests
/// pin the allow-list at the boundary: what survives, what is stripped, and what counts
/// as empty once the markup is gone.
/// </summary>
public sealed class UpdateBodyTests
{
    private readonly UpdateBodySanitizer _sanitizer = new();

    [Fact]
    public void Script_IsRemoved()
    {
        var result = _sanitizer.Sanitise("<p>Hello</p><script>alert('x')</script>");

        Assert.DoesNotContain("script", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Hello", result.Text);
    }

    [Fact]
    public void EventHandlers_AreRemoved()
    {
        var result = _sanitizer.Sanitise("<p onclick=\"steal()\">Hi</p>");

        Assert.DoesNotContain("onclick", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Hi", result.Text);
    }

    [Fact]
    public void JavascriptLinks_AreRemoved()
    {
        var result = _sanitizer.Sanitise("<a href=\"javascript:alert(1)\">click</a>");

        Assert.DoesNotContain("javascript:", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("click", result.Text);
    }

    [Fact]
    public void ImagesAndFrames_AreRemoved()
    {
        var result = _sanitizer.Sanitise("<img src=\"https://tracker/x.gif\"><iframe src=\"https://evil\"></iframe>Safe");

        Assert.DoesNotContain("<img", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Safe", result.Text);
    }

    [Fact]
    public void StyleAttributes_AreRemoved()
    {
        var result = _sanitizer.Sanitise("<p style=\"position:fixed;top:0\">Hi</p>");

        Assert.DoesNotContain("style", result.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllowedMarkup_Survives()
    {
        var result = _sanitizer.Sanitise(
            "<p>Ship <strong>now</strong></p><ul><li>one</li><li>two</li></ul><pre><code>x=1</code></pre>");

        Assert.Contains("<strong>", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<li>", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<pre>", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ship", result.Text);
        Assert.Contains("one", result.Text);
        Assert.Contains("two", result.Text);
    }

    [Fact]
    public void SafeLink_IsKept()
    {
        var result = _sanitizer.Sanitise("<a href=\"https://example.com/doc\" title=\"Doc\">doc</a>");

        Assert.Contains("href=\"https://example.com/doc\"", result.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p><br>")]
    [InlineData("<script>alert(1)</script>")]
    public void BodiesWithoutWords_AreEmpty(string? input)
    {
        var result = _sanitizer.Sanitise(input);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void PlainText_CollapsesWhitespace()
    {
        var result = _sanitizer.Sanitise("<p>one</p>\n\n<p>   two   </p>");

        Assert.Equal("one two", result.Text);
    }

    [Fact]
    public void Update_RequiresABody()
    {
        Assert.Throws<ArgumentException>(() => Update.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "<p></p>", "   "));
    }

    [Fact]
    public void Update_RejectsAnOversizedBody()
    {
        var body = new string('x', Update.MaxBodyLength + 1);

        Assert.Throws<ArgumentException>(() => Update.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            body, body));
    }

    [Fact]
    public void Reply_ThreadsUnderItsRoot()
    {
        var tenant = Guid.CreateVersion7();
        var board = Guid.CreateVersion7();
        var item = Guid.CreateVersion7();
        var author = Guid.CreateVersion7();

        var root = Update.Create(tenant, board, item, author, "<p>root</p>", "root");
        var reply = Update.Create(tenant, board, item, author, "<p>reply</p>", "reply", root.Id);

        Assert.True(reply.IsReply);
        Assert.Equal(root.Id, reply.ThreadId);
        Assert.Equal(root.Id, root.ThreadId);
    }

    [Fact]
    public void Delete_IsSoft_AndRestorable()
    {
        var update = Update.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "<p>x</p>", "x");

        update.Delete();
        Assert.NotNull(update.DeletedAt);

        update.Restore();
        Assert.Null(update.DeletedAt);
    }
}
