using System.Net;
using Tuesday.Infrastructure.Automations;
using Tuesday.Application.Boards;

namespace Tuesday.Tests.Unit;

/// <summary>
/// The SSRF policy and the page cursor are pure functions with sharp edges worth pinning:
/// one decides whether the server talks to an address at all, the other must survive a
/// round trip without being forgeable into a different page.
/// </summary>
public sealed class ApiHardeningTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.5.4")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    public void Private_and_link_local_addresses_are_never_public(string address)
    {
        Assert.False(OutboundUrlPolicy.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Public_addresses_are_allowed(string address)
    {
        Assert.True(OutboundUrlPolicy.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://[::1]/hook")]
    [InlineData("ftp://example.com/hook")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://user:secret@example.com/hook")]
    [InlineData("not-a-url")]
    public async Task Registration_refuses_unacceptable_urls(string url)
    {
        Assert.NotNull(await OutboundUrlPolicy.ValidateAsync(url));
    }

    [Fact]
    public async Task A_public_https_url_passes_the_policy()
    {
        Assert.Null(await OutboundUrlPolicy.ValidateAsync("https://example.com/hooks/tuesday"));
    }

    [Fact]
    public void Cursors_round_trip()
    {
        var encoded = PageCursor.Encode(4_294_967_295);

        Assert.True(PageCursor.TryDecode(encoded, out var decoded));
        Assert.Equal(4_294_967_295, decoded);

        // base64url: no padding, no '+' or '/'.
        Assert.DoesNotContain('=', encoded);
        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("abc")]
    public void A_forged_cursor_is_refused(string value)
    {
        Assert.False(PageCursor.TryDecode(value, out _));
    }

    [Fact]
    public void The_same_payload_hashes_to_the_same_idempotency_digest()
    {
        const string payload = """{"body":"hello"}""";

        Assert.Equal(IdempotencyService.HashOf(payload), IdempotencyService.HashOf(payload));
        Assert.NotEqual(IdempotencyService.HashOf(payload), IdempotencyService.HashOf("""{"body":"bye"}"""));
    }
}
