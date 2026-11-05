using System.Text.RegularExpressions;

namespace Tuesday.Tests.Integration;

/// <summary>
/// The CSP is only worth having if it is both present and satisfiable by the page it guards.
/// This pins the policy shape and checks that the inline import map carries the same nonce,
/// because a framework change that drops the nonce would silently break enhanced navigation.
/// </summary>
public sealed class ContentSecurityPolicyTests : IClassFixture<TuesdayWebFactory>
{
    private readonly TuesdayWebFactory _factory;

    public ContentSecurityPolicyTests(TuesdayWebFactory factory) => _factory = factory;

    [Fact]
    public async Task The_shell_sends_a_nonce_policy_and_the_page_uses_that_nonce()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var values));

        // Blazor sends its own frame-ancestors policy, so there are two headers and the browser
        // enforces their intersection. Assert across both rather than pretending there is one.
        var all = values.ToArray();
        var policy = string.Join("; ", all);

        Assert.Contains("default-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("script-src 'self' 'nonce-", policy, StringComparison.Ordinal);
        Assert.Contains("style-src 'self' 'unsafe-inline'", policy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self' ws: wss:", policy, StringComparison.Ordinal);

        // Read the nonce from the page, not from header ordering: the framework may add a second
        // policy header, and the header a browser applies to a script is the one carrying its nonce.
        var nonceMatch = Regex.Match(html, "nonce=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        Assert.True(nonceMatch.Success, "the rendered shell has no nonce attribute");

        var nonce = nonceMatch.Groups[1].Value;

        Assert.False(string.IsNullOrWhiteSpace(nonce));

        // Every nonce-bearing policy must allow the nonce the page actually uses, otherwise the
        // browser blocks the bootstrap even though the middleware looked correct.
        foreach (var header in all.Where(v => v.Contains("'nonce-", StringComparison.Ordinal)))
        {
            Assert.True(header.Contains("'nonce-" + nonce + "'", StringComparison.Ordinal),
                $"page nonce {nonce} not in policy. headers: {string.Join(" || ", all)}");
        }

        // The bootstrap script and the import map must both carry it, or the browser blocks them.
        Assert.Matches("nonce=\"" + Regex.Escape(nonce) + "\"", html);
        // ImportMap renders the attribute with a capital N; HTML attribute names are
        // case-insensitive, and so is this match.
        Assert.Matches("(?i)type=\"importmap\"[^>]*nonce=\"" + Regex.Escape(nonce) + "\"", html);
    }

    [Fact]
    public async Task Each_response_gets_a_fresh_nonce()
    {
        using var client = _factory.CreateClient();

        var first = await client.GetAsync("/login");
        var second = await client.GetAsync("/login");

        var firstNonce = await Nonce(first);
        var secondNonce = await Nonce(second);

        Assert.NotEqual(firstNonce, secondNonce);
    }

    private static async Task<string> Nonce(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();

        return Regex.Match(html, "nonce=\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
    }
}
