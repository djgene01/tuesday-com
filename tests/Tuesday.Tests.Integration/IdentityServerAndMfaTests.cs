using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Tuesday.Infrastructure.Identity;

namespace Tuesday.Tests.Integration;

/// <summary>
/// The plan's P1 scope asks for a second factor and an OAuth server. These tests hold the
/// two together: a code the app would produce must verify, and an enrolled account must not
/// be able to obtain a password-grant token that skips the factor.
/// </summary>
public sealed class IdentityServerAndMfaTests
{
    [Fact]
    public async Task Authenticator_key_round_trips_a_generated_code()
    {
        await using var factory = new TuesdayWebFactory();
        using var scope = factory.Services.CreateScope();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync("guest@tuesday.local");

        Assert.NotNull(user);

        await users.ResetAuthenticatorKeyAsync(user);
        var key = await users.GetAuthenticatorKeyAsync(user);

        Assert.False(string.IsNullOrEmpty(key));

        // The same generator the authenticator app runs, applied to the shared key.
        var code = Totp(key!, DateTimeOffset.UtcNow);

        Assert.True(await users.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, code));
    }

    [Fact]
    public async Task A_wrong_code_is_refused()
    {
        await using var factory = new TuesdayWebFactory();
        using var scope = factory.Services.CreateScope();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync("guest@tuesday.local");

        Assert.NotNull(user);

        await users.ResetAuthenticatorKeyAsync(user);

        Assert.False(await users.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, "000000"));
    }

    [Fact]
    public async Task A_two_factor_account_cannot_take_a_password_grant_token()
    {
        await using var factory = new TuesdayWebFactory();
        using var scope = factory.Services.CreateScope();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync("guest@tuesday.local");

        Assert.NotNull(user);

        await users.SetTwoFactorEnabledAsync(user, true);

        try
        {
            var client = factory.CreateClient();

            var response = await client.PostAsync(
                "/connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["username"] = "guest@tuesday.local",
                    ["password"] = "Tuesday!Demo1",
                    ["client_id"] = "tuesday-cli",
                    ["client_secret"] = "tuesday_dev_client_secret",
                    ["scope"] = "boards"
                }));

            // Refused explicitly rather than silently downgraded to password-only.
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("invalid_grant", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await users.SetTwoFactorEnabledAsync(user, false);
        }
    }

    /// <summary>
    /// RFC 6238, six digits, 30-second step, HMAC-SHA1 - exactly what an authenticator app
    /// computes from the shared key the enrolment page shows.
    /// </summary>
    private static string Totp(string base32Key, DateTimeOffset now)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var bytes = new List<byte>();

        foreach (var character in base32Key.Replace(" ", string.Empty).ToUpperInvariant())
        {
            var index = alphabet.IndexOf(character, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            value = (value << 5) | index;
            bits += 5;

            if (bits >= 8)
            {
                bytes.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        var counter = now.ToUnixTimeSeconds() / 30;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        using var hmac = new System.Security.Cryptography.HMACSHA1(bytes.ToArray());
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
