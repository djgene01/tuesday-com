namespace Tuesday.Tests.Integration;

/// <summary>
/// The 404 path has regressed twice: once to a bare status with no body, and once to the
/// browser's own error page. These assertions pin the shape that actually works — a real 404
/// status *and* a rendered page — so a future status-code change cannot silently empty it.
/// </summary>
public sealed class NotFoundPageTests : IClassFixture<TuesdayWebFactory>
{
    private readonly TuesdayWebFactory _factory;

    public NotFoundPageTests(TuesdayWebFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/nope")]
    [InlineData("/board/not-a-real-page")]
    [InlineData("/a/deeply/nested/missing/path")]
    public async Task UnknownPath_Returns404WithRenderedPage(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(html), $"{path} returned an empty body");
        Assert.Contains("Not found", html, StringComparison.Ordinal);
        Assert.Contains("Back to your workspace", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitNotFoundRoute_AlsoRenders()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/not-found");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Not found", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiWithoutToken_KeepsRawStatusAndNoHtml()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/boards");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("<html", body, StringComparison.OrdinalIgnoreCase);
    }
}
