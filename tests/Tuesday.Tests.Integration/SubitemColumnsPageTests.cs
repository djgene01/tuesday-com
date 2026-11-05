namespace Tuesday.Tests.Integration;

/// <summary>
/// The sub-item column editor is a board surface: it must be behind the same capability gate
/// as the board it belongs to, and it must not be reachable anonymously.
/// </summary>
public sealed class SubitemColumnsPageTests : IClassFixture<TuesdayWebFactory>
{
    private readonly TuesdayWebFactory _factory;

    public SubitemColumnsPageTests(TuesdayWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_request_is_sent_to_the_login_page()
    {
        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync($"/board/{Guid.CreateVersion7()}/subitems");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
    }
}
