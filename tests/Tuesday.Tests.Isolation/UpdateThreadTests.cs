using Microsoft.EntityFrameworkCore;
using Tuesday.Application.Tenancy;
using Tuesday.Domain.Boards;
using Tuesday.Domain.Items;
using Tuesday.Domain.Tenancy;
using Tuesday.Domain.Workspaces;
using Tuesday.Infrastructure.Collaboration;
using Tuesday.Infrastructure.Identity;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Tests.Isolation;

/// <summary>
/// The update service is the only route into the thread, so it is tested through the same
/// interface the UI uses: sanitise on write, flatten replies, toggle likes idempotently,
/// and refuse an edit by anyone but the author.
/// </summary>
public sealed class UpdateThreadTests
{
    private sealed class TestFactory(DbContextOptions<TuesdayDbContext> options, ITenantContext tenant)
        : IDbContextFactory<TuesdayDbContext>
    {
        public TuesdayDbContext CreateDbContext() => new(options, tenant);
    }

    private sealed record Fixture(
        TestFactory Factory,
        Guid TenantId,
        Guid BoardId,
        Guid ItemId,
        Guid AuthorId,
        Guid OtherId);

    private static async Task<Fixture> SeedAsync()
    {
        var tenantContext = new TenantContext();
        var options = new DbContextOptionsBuilder<TuesdayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("n"))
            .Options;

        var factory = new TestFactory(options, tenantContext);
        var tenant = Tenant.Create("Tenant", "t-" + Guid.NewGuid().ToString("n")[..8]);
        var author = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();

        await using var db = factory.CreateDbContext();
        db.Tenants.Add(tenant);
        db.Memberships.Add(Membership.Create(tenant.Id, author, TenantRole.Admin));

        db.Users.Add(new ApplicationUser { Id = author, UserName = "author@t", Email = "author@t", DisplayName = "Ada Author" });
        db.Users.Add(new ApplicationUser { Id = other, UserName = "other@t", Email = "other@t", DisplayName = "Bo Other" });

        var workspace = Workspace.Create(tenant.Id, "W", WorkspaceVisibility.Open, author);
        var board = Board.Create(tenant.Id, workspace.Id, "B", BoardType.Main, author);
        db.Workspaces.Add(workspace);
        db.Boards.Add(board);

        var group = ItemGroup.Create(tenant.Id, board.Id, "G", "slate", 0);
        db.ItemGroups.Add(group);

        var item = Item.Create(tenant.Id, board.Id, group.Id, "Launch", 0);
        db.Items.Add(item);

        await db.SaveChangesAsync();
        tenantContext.Resolve(tenant.Id, author, TenantRole.Admin);

        return new Fixture(factory, tenant.Id, board.Id, item.Id, author, other);
    }

    private static UpdateService Service(Fixture fixture) =>
        new(fixture.Factory, new UpdateBodySanitizer());

    [Fact]
    public async Task Post_StoresSanitisedHtmlAndPlainText()
    {
        var fixture = await SeedAsync();
        var service = Service(fixture);

        var result = await service.PostAsync(
            fixture.BoardId,
            fixture.ItemId,
            fixture.AuthorId,
            "<p>Shipping <strong>today</strong></p><script>alert(1)</script>");

        Assert.True(result.Succeeded, result.Error);

        await using var db = fixture.Factory.CreateDbContext();
        var stored = await db.Updates.SingleAsync();

        Assert.Contains("<strong>", stored.BodyHtml);
        Assert.DoesNotContain("script", stored.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Shipping today", stored.BodyText);
        Assert.Equal(fixture.AuthorId, stored.AuthorId);
    }

    [Fact]
    public async Task Post_WithoutWords_IsRefused()
    {
        var fixture = await SeedAsync();
        var service = Service(fixture);

        var result = await service.PostAsync(fixture.BoardId, fixture.ItemId, fixture.AuthorId, "<p></p><br>");

        Assert.False(result.Succeeded);
        Assert.Contains("words", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_ToAnItemOnAnotherBoard_IsRefused()
    {
        var fixture = await SeedAsync();
        var service = Service(fixture);

        var result = await service.PostAsync(
            Guid.CreateVersion7(),
            fixture.ItemId,
            fixture.AuthorId,
            "hello");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Reply_ToAReply_JoinsTheSameThread()
    {
        var fixture = await SeedAsync();
        var service = Service(fixture);

        var root = await service.PostAsync(fixture.BoardId, fixture.ItemId, fixture.AuthorId, "root");
        var firstReply = await service.ReplyAsync(root.Id, fixture.OtherId, "reply one");
        var secondReply = await service.ReplyAsync(firstReply.Id, fixture.AuthorId, "reply two");

        Assert.True(secondReply.Succeeded, secondReply.Error);

        var thread = await service.ThreadAsync(fixture.ItemId, fixture.AuthorId);

        var only = Assert.Single(thread);
        Assert.Equal(2, only.Replies.Count);
        Assert.Equal(["reply one", "reply two"], only.Replies.Select(reply => reply.Text));
    }

    [Fact]
    public async Task Thread_CarriesAuthorNamesAndLikeState()
    {
        var fixture = await SeedAsync();
        var service = Service(fixture);

        var root = await service.PostAsync(fixture.BoardId, fixture.ItemId, fixture.AuthorId, "hello");
        await service.ReplyAsync(root.Id, fixture.OtherId, "a reply");
        Assert.True(await service.ToggleLikeAsync(root.Id, fixture.OtherId));

        var thread = await service.ThreadAsync(fixture.ItemId, fixture.OtherId);

        var node = Assert.Single(thread);
        Assert.Equal("Ada Author", node.AuthorName);
        Assert.Equal(1, node.LikeCount);
        Assert.True(node.LikedByMe);
        Assert.Equal("Bo Other", Assert.Single(node.Replies).AuthorName);

        var anonymous = await service.ThreadAsync(fixture.ItemId, null);
        Assert.False(Assert.Single(anonymous).LikedByMe);
    }

    [Fact]
    public async Task Like_TogglesOffOnTheSecondCall()
    {
        var fixture = await SeedAsync();
        var service = Service(fixture);
        var root = await service.PostAsync(fixture.BoardId, fixture.ItemId, fixture.AuthorId, "hello");

        Assert.True(await service.ToggleLikeAsync(root.Id, fixture.OtherId));
        Assert.False(await service.ToggleLikeAsync(root.Id, fixture.OtherId));

        var thread = await service.ThreadAsync(fixture.ItemId, fixture.OtherId);
        Assert.Equal(0, Assert.Single(thread).LikeCount);
    }

    [Fact]
    public async Task Edit_IsRefusedForAnyoneButTheAuthor()
    {
        var fixture = await SeedAsync();
        var service = Service(fixture);
        var root = await service.PostAsync(fixture.BoardId, fixture.ItemId, fixture.AuthorId, "hello");

        var byOther = await service.EditAsync(root.Id, fixture.OtherId, "hijacked");
        Assert.False(byOther.Succeeded);

        var byAuthor = await service.EditAsync(root.Id, fixture.AuthorId, "edited");
        Assert.True(byAuthor.Succeeded, byAuthor.Error);

        var thread = await service.ThreadAsync(fixture.ItemId, fixture.AuthorId);
        var node = Assert.Single(thread);
        Assert.Equal("edited", node.Text);
        Assert.NotNull(node.EditedAt);
    }

    [Fact]
    public async Task Delete_IsSoftAndModeratorsMayDeleteOthers()
    {
        var fixture = await SeedAsync();
        var service = Service(fixture);
        var root = await service.PostAsync(fixture.BoardId, fixture.ItemId, fixture.AuthorId, "hello");

        Assert.False(await service.DeleteAsync(root.Id, fixture.OtherId, canModerate: false));
        Assert.True(await service.DeleteAsync(root.Id, fixture.OtherId, canModerate: true));

        var thread = await service.ThreadAsync(fixture.ItemId, fixture.AuthorId);
        Assert.True(Assert.Single(thread).IsDeleted);

        await using var db = fixture.Factory.CreateDbContext();
        Assert.True(await db.Updates.AnyAsync());
    }
}
