using Tuesday.Domain.Automation;

namespace Tuesday.Tests.Unit;

/// <summary>
/// The condition grammar is the whole safety story for automations: it must match exactly
/// what a person wrote, and a malformed condition must never match everything.
/// </summary>
public sealed class AutomationRuleTests
{
    private static IReadOnlyDictionary<string, string?> Values(params (string Key, string? Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static Automation Rule(string condition) => Automation.Create(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "rule",
        AutomationTrigger.CellChanged,
        condition,
        AutomationAction.NotifyOwner,
        "message",
        Guid.CreateVersion7());

    [Fact]
    public void EmptyCondition_AlwaysMatches()
    {
        Assert.True(Rule("").Matches(Values(("status", "Done"))));
        Assert.True(Rule("   ").Matches(Values()));
    }

    [Fact]
    public void Equality_IsCaseInsensitiveAndTrimmed()
    {
        var rule = Rule("status=Done");

        Assert.True(rule.Matches(Values(("status", "done"))));
        Assert.True(rule.Matches(Values(("status", "  Done "))));
        Assert.False(rule.Matches(Values(("status", "Working on it"))));
        Assert.False(rule.Matches(Values()));
    }

    [Fact]
    public void Inequality_UsesTheSameGrammar()
    {
        var rule = Rule("status!=Done");

        Assert.True(rule.Matches(Values(("status", "Stuck"))));
        Assert.False(rule.Matches(Values(("status", "Done"))));

        // A missing value is not "not equal": the rule cannot act on something it cannot see.
        Assert.False(rule.Matches(Values()));
    }

    [Fact]
    public void Contains_MatchesAnyValue()
    {
        var rule = Rule("contains:urgent");

        Assert.True(rule.Matches(Values(("notes", "This is URGENT"))));
        Assert.False(rule.Matches(Values(("notes", "later"))));
    }

    [Theory]
    [InlineData("status")]
    [InlineData("garbage text")]
    [InlineData("=Done")]
    public void MalformedCondition_NeverMatches(string condition)
    {
        Assert.False(Rule(condition).Matches(Values(("status", "Done"))));
    }

    [Fact]
    public void WebhookSignature_IsStableAndPrefixed()
    {
        var first = Webhook.Sign("s3cret", "{\"a\":1}");
        var second = Webhook.Sign("s3cret", "{\"a\":1}");
        var other = Webhook.Sign("other", "{\"a\":1}");

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.StartsWith("sha256=", first);
        Assert.Equal(71, first.Length); // prefix + 64 hex characters
    }

    [Fact]
    public void Webhook_RejectsANonAbsoluteUrl()
    {
        Assert.Throws<ArgumentException>(() => Webhook.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "not-a-url", Guid.CreateVersion7()));

        Assert.Throws<ArgumentException>(() => Webhook.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "ftp://example.com/hook", Guid.CreateVersion7()));
    }

    [Fact]
    public void ApiToken_StoresOnlyAHash_AndReturnsThePlaintextOnce()
    {
        var (token, plaintext) = ApiToken.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), "CI");

        Assert.StartsWith("tue_", plaintext);
        Assert.Equal(ApiToken.HashOf(plaintext), token.Hash);
        Assert.NotEqual(plaintext, token.Hash);
        Assert.StartsWith(plaintext[..12], token.Prefix);
        Assert.True(token.IsActive);

        token.Revoke();
        Assert.False(token.IsActive);
    }
}
