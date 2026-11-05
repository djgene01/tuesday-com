using Tuesday.Domain.Tenancy;

namespace Tuesday.Tests.Unit;

public sealed class InviteTests
{
    [Fact]
    public void A_token_is_prefixed_random_and_stored_only_as_a_hash()
    {
        var (invite, token) = Invite.Create(
            Guid.CreateVersion7(), "Person@Example.com", TenantRole.Member, Guid.CreateVersion7());

        Assert.StartsWith(InviteToken.Prefix, token);
        Assert.True(InviteToken.LooksWellFormed(token));
        Assert.Equal(InviteToken.HashOf(token), invite.TokenHash);
        Assert.DoesNotContain(token, invite.TokenHash);
        Assert.Equal("person@example.com", invite.Email);
        Assert.Equal(TenantRole.Member, invite.Role);
    }

    [Fact]
    public void Two_invitations_never_share_a_token()
    {
        var tenant = Guid.CreateVersion7();
        var a = Invite.Create(tenant, "a@example.com", TenantRole.Viewer, Guid.CreateVersion7());
        var b = Invite.Create(tenant, "b@example.com", TenantRole.Viewer, Guid.CreateVersion7());

        Assert.NotEqual(a.Token, b.Token);
        Assert.NotEqual(a.Invite.TokenHash, b.Invite.TokenHash);
    }

    [Fact]
    public void A_token_is_not_mistaken_for_a_well_formed_one()
    {
        Assert.False(InviteToken.LooksWellFormed(null));
        Assert.False(InviteToken.LooksWellFormed(""));
        Assert.False(InviteToken.LooksWellFormed("tue_inv_short"));
        Assert.False(InviteToken.LooksWellFormed("not-a-token-" + new string('x', 40)));
    }

    [Fact]
    public void An_invitation_expires_after_seven_days()
    {
        var issued = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        var (invite, _) = Invite.Create(
            Guid.CreateVersion7(), "x@example.com", TenantRole.Member, Guid.CreateVersion7(), issued);

        Assert.True(invite.IsUsable(issued.AddDays(6)));
        Assert.False(invite.IsUsable(issued.AddDays(7)));
        Assert.False(invite.IsUsable(issued.AddDays(8)));
    }

    [Fact]
    public void An_invitation_is_single_use()
    {
        var (invite, _) = Invite.Create(
            Guid.CreateVersion7(), "x@example.com", TenantRole.Member, Guid.CreateVersion7());
        var userId = Guid.CreateVersion7();

        Assert.True(invite.Accept(userId));
        Assert.NotNull(invite.AcceptedAt);
        Assert.Equal(userId, invite.AcceptedBy);
        Assert.False(invite.Accept(userId));
        Assert.False(invite.IsUsable());
    }

    [Fact]
    public void A_used_invitation_cannot_be_accepted_after_it_expires()
    {
        var issued = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        var (invite, _) = Invite.Create(
            Guid.CreateVersion7(), "x@example.com", TenantRole.Member, Guid.CreateVersion7(), issued);

        Assert.False(invite.Accept(Guid.CreateVersion7(), issued.AddDays(30)));
        Assert.Null(invite.AcceptedAt);
    }

    [Fact]
    public void An_invitation_needs_a_tenant_and_an_email()
    {
        Assert.Throws<ArgumentException>(() => Invite.Create(
            Guid.Empty, "x@example.com", TenantRole.Member, Guid.CreateVersion7()));
        Assert.Throws<ArgumentException>(() => Invite.Create(
            Guid.CreateVersion7(), "not-an-email", TenantRole.Member, Guid.CreateVersion7()));
    }
}
