using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tuesday.Application.Collaboration;
using Tuesday.Application.Tenancy;
using Tuesday.Domain.Boards;
using Tuesday.Domain.Collaboration;
using Tuesday.Domain.Items;
using Tuesday.Domain.Tenancy;
using Tuesday.Domain.Workspaces;
using Tuesday.Infrastructure.Collaboration;
using Tuesday.Infrastructure.Identity;
using Tuesday.Infrastructure.Persistence;
using Tuesday.Infrastructure.Storage;

namespace Tuesday.Tests.Isolation;

/// <summary>
/// Notifications, search and attachments through the same interfaces the UI uses: who gets
/// told, what search can see, and what a stored file is allowed to be called.
/// </summary>
public sealed class CollaborationServicesTests
{
    private sealed class TestFactory(DbContextOptions<TuesdayDbContext> options, ITenantContext tenant)
        : IDbContextFactory<TuesdayDbContext>
    {
        public TuesdayDbContext CreateDbContext() => new(options, tenant);
    }

    private sealed class QuietEmail : IEmailSender
    {
        public List<string> Sent { get; } = [];

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            Sent.Add(to);
            return Task.CompletedTask;
        }
    }

    private sealed record World(
        TestFactory Factory,
        Guid TenantId,
        Guid BoardId,
        Guid ItemId,
        Guid AdminId,
        Guid MemberId,
        ITenantContext Tenant);

    private static async Task<World> SeedAsync()
    {
        var tenant = new TenantContext();
        var options = new DbContextOptionsBuilder<TuesdayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("n"))
            .Options;

        var factory = new TestFactory(options, tenant);
        var organisation = Tenant.Create("Tenant", "t-" + Guid.NewGuid().ToString("n")[..8]);
        var admin = Guid.CreateVersion7();
        var member = Guid.CreateVersion7();

        await using var db = factory.CreateDbContext();
        db.Tenants.Add(organisation);
        db.Memberships.Add(Membership.Create(organisation.Id, admin, TenantRole.Admin));
        db.Memberships.Add(Membership.Create(organisation.Id, member, TenantRole.Member));
        db.Users.Add(new ApplicationUser { Id = admin, UserName = "a@t", Email = "a@t", DisplayName = "Ada Admin" });
        db.Users.Add(new ApplicationUser { Id = member, UserName = "m@t", Email = "m@t", DisplayName = "Mo Member" });

        var workspace = Workspace.Create(organisation.Id, "W", WorkspaceVisibility.Open, admin);
        var board = Board.Create(organisation.Id, workspace.Id, "Campaign", BoardType.Main, admin);
        db.Workspaces.Add(workspace);
        db.Boards.Add(board);

        var hidden = Board.Create(organisation.Id, workspace.Id, "Leadership", BoardType.Main, admin);
        db.Boards.Add(hidden);

        var group = ItemGroup.Create(organisation.Id, board.Id, "G", "slate", 0);
        db.ItemGroups.Add(group);

        var item = Item.Create(organisation.Id, board.Id, group.Id, "Launch plan", 0);
        db.Items.Add(item);

        await db.SaveChangesAsync();
        tenant.Resolve(organisation.Id, admin, TenantRole.Admin);

        return new World(factory, organisation.Id, board.Id, item.Id, admin, member, tenant);
    }

    private static NotificationService Inbox(World world, QuietEmail? email = null)
    {
        var options = Options.Create(new EmailOptions { Enabled = false });
        return new NotificationService(
            world.Factory,
            email ?? new QuietEmail(),
            options,
            NullLogger<NotificationService>.Instance);
    }

    [Theory]
    [InlineData("ping @Mo Member please", "Mo Member", true)]
    [InlineData("ping @mo member please", "Mo Member", true)]
    [InlineData("ping @Mo Members please", "Mo Member", false)]
    [InlineData("email me at me@Mo Member.com", "Mo Member", false)]
    [InlineData("no mention here", "Mo Member", false)]
    public void ContainsMention_MatchesWholeNamesOnly(string text, string name, bool expected)
    {
        Assert.Equal(expected, NotificationService.ContainsMention(text, name));
    }

    [Fact]
    public async Task Mention_NotifiesTheMentionedPersonButNotTheAuthor()
    {
        var world = await SeedAsync();
        var inbox = Inbox(world);

        var count = await inbox.FanOutUpdateAsync(
            world.TenantId, world.BoardId, world.ItemId, Guid.CreateVersion7(),
            world.AdminId, "@Mo Member please review", isReply: false, threadRootId: null);

        Assert.Equal(1, count);

        var rows = await inbox.ListAsync(world.MemberId);
        var row = Assert.Single(rows);

        Assert.Equal(NotificationKind.Mention, row.Kind);
        Assert.Contains("mentioned you", row.Title);
        Assert.StartsWith($"/board/{world.BoardId:D}/item/{world.ItemId:D}", row.LinkUrl);

        Assert.Empty(await inbox.ListAsync(world.AdminId));
        Assert.Equal(1, await inbox.UnreadCountAsync(world.MemberId));
    }

    [Fact]
    public async Task Reply_NotifiesEveryParticipantExceptTheActor()
    {
        var world = await SeedAsync();
        var inbox = Inbox(world);

        // Admin and member both wrote in the thread; a reply by the admin must tell only the member.
        var root = Guid.CreateVersion7();

        await using (var db = world.Factory.CreateDbContext())
        {
            db.Updates.Add(Update.Create(world.TenantId, world.BoardId, world.ItemId, world.AdminId, "<p>root</p>", "root"));
            db.Updates.Add(Update.Create(world.TenantId, world.BoardId, world.ItemId, world.MemberId, "<p>me too</p>", "me too", root));
            await db.SaveChangesAsync();
        }

        var count = await inbox.FanOutUpdateAsync(
            world.TenantId, world.BoardId, world.ItemId, Guid.CreateVersion7(),
            world.AdminId, "a reply", isReply: true, threadRootId: root);

        Assert.Equal(1, count); // only Mo Member; the actor is excluded
        Assert.Contains(await inbox.ListAsync(world.MemberId), row => row.Kind == NotificationKind.Reply);
    }

    [Fact]
    public async Task MarkAllRead_ClearsTheUnreadCount()
    {
        var world = await SeedAsync();
        var inbox = Inbox(world);

        await inbox.FanOutUpdateAsync(world.TenantId, world.BoardId, world.ItemId, Guid.CreateVersion7(),
            world.AdminId, "@Mo Member one", isReply: false, threadRootId: null);
        await inbox.FanOutUpdateAsync(world.TenantId, world.BoardId, world.ItemId, Guid.CreateVersion7(),
            world.AdminId, "@Mo Member two", isReply: false, threadRootId: null);

        Assert.Equal(2, await inbox.UnreadCountAsync(world.MemberId));

        await inbox.MarkAllReadAsync(world.MemberId);

        Assert.Equal(0, await inbox.UnreadCountAsync(world.MemberId));
        Assert.All(await inbox.ListAsync(world.MemberId), row => Assert.True(row.IsRead));
    }

    [Fact]
    public async Task Search_FindsItemsAndUpdates_ButOnlyOnVisibleBoards()
    {
        var world = await SeedAsync();
        var search = new SearchService(world.Factory);

        await using (var db = world.Factory.CreateDbContext())
        {
            db.Updates.Add(Update.Create(world.TenantId, world.BoardId, world.ItemId, world.AdminId,
                "<p>the launch is delayed</p>", "the launch is delayed"));

            var hiddenGroup = ItemGroup.Create(world.TenantId,
                db.Boards.First(board => board.Name == "Leadership").Id, "G", "slate", 0);
            db.ItemGroups.Add(hiddenGroup);
            db.Items.Add(Item.Create(world.TenantId, hiddenGroup.BoardId, hiddenGroup.Id, "Launch secret", 0));
            await db.SaveChangesAsync();
        }

        var hits = await search.SearchAsync([world.BoardId], "Launch");

        Assert.Contains(hits, hit => hit.Kind == "item" && hit.Title == "Launch plan");
        Assert.Contains(hits, hit => hit.Kind == "update");
        Assert.DoesNotContain(hits, hit => hit.Title == "Launch secret");

        var none = await search.SearchAsync([], "Launch");
        Assert.Empty(none);
    }

    [Fact]
    public async Task Attachment_StripsPathsFromTheStoredName()
    {
        var attachment = Attachment.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "../../etc/passwd", "text/plain", 10, "aa/bb/file.txt");

        Assert.Equal("passwd", attachment.FileName);
    }

    [Fact]
    public async Task Attachment_RefusesAnOversizedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuesday-attach-" + Guid.NewGuid().ToString("n"));
        var service = new AttachmentService(
            new TestFactory(
                new DbContextOptionsBuilder<TuesdayDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("n")).Options,
                new TenantContext()),
            Options.Create(new StorageOptions { Root = root, MaxUploadBytes = 1024 }));

        var result = await service.SaveAsync(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "big.bin", "application/octet-stream", 2048, new MemoryStream(new byte[10]));

        Assert.False(result.Succeeded);
        Assert.Contains("1024 bytes", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(root));
    }
}
