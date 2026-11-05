using Microsoft.EntityFrameworkCore;
using Tuesday.Application.Tenancy;
using Tuesday.Domain.Auditing;
using Tuesday.Domain.Tenancy;
using Tuesday.Domain.Workspaces;
using Tuesday.Infrastructure.Persistence;
using Tuesday.Infrastructure.Queries;

namespace Tuesday.Tests.Isolation;

/// <summary>
/// Proves the isolation boundary from both sides: a filtered read never crosses a
/// tenant, and a write cannot be smuggled into another tenant. Each test gets its own
/// in-memory database so ordering never matters.
/// </summary>
public sealed class CrossTenantSuite
{
    private static DbContextOptions<TuesdayDbContext> Options(string database) =>
        new DbContextOptionsBuilder<TuesdayDbContext>()
            .UseInMemoryDatabase(database)
            .Options;

    private static TuesdayDbContext NewContext(string database, ITenantContext tenant) =>
        new(Options(database), tenant);

    /// <summary>Seeds two complete tenants with an unresolved context, as startup does.</summary>
    private static async Task<SeedIds> SeedAsync(string database)
    {
        await using var context = NewContext(database, new TenantContext());

        var alpha = Tenant.Create("Alpha", "alpha");
        var beta = Tenant.Create("Beta", "beta");
        context.Tenants.AddRange(alpha, beta);

        var alphaAdminId = Guid.CreateVersion7();
        var alphaMemberId = Guid.CreateVersion7();
        var alphaGuestId = Guid.CreateVersion7();
        var betaAdminId = Guid.CreateVersion7();

        context.Memberships.AddRange(
            Membership.Create(alpha.Id, alphaAdminId, TenantRole.Admin),
            Membership.Create(alpha.Id, alphaMemberId, TenantRole.Member),
            Membership.Create(alpha.Id, alphaGuestId, TenantRole.Guest),
            Membership.Create(beta.Id, betaAdminId, TenantRole.Admin));

        var alphaWorkspace = Workspace.Create(alpha.Id, "Alpha work", WorkspaceVisibility.Open, alphaAdminId);
        var alphaPrivateWorkspace = Workspace.Create(alpha.Id, "Alpha private", WorkspaceVisibility.Private, alphaAdminId);
        var betaWorkspace = Workspace.Create(beta.Id, "Beta work", WorkspaceVisibility.Open, betaAdminId);
        context.Workspaces.AddRange(alphaWorkspace, alphaPrivateWorkspace, betaWorkspace);

        var publicBoard = Board.Create(alpha.Id, alphaWorkspace.Id, "Alpha public", BoardType.Main, alphaAdminId);
        var privateBoard = Board.Create(alpha.Id, alphaWorkspace.Id, "Alpha private board", BoardType.Private, alphaAdminId);
        var secretBoard = Board.Create(alpha.Id, alphaPrivateWorkspace.Id, "Alpha secret", BoardType.Private, alphaAdminId);
        var betaBoard = Board.Create(beta.Id, betaWorkspace.Id, "Beta public", BoardType.Main, betaAdminId);
        context.Boards.AddRange(publicBoard, privateBoard, secretBoard, betaBoard);

        context.BoardGrants.Add(BoardGrant.Create(alpha.Id, publicBoard.Id, alphaGuestId, GrantLevel.View));

        context.AuditEntries.Add(AuditEntry.Record(alpha.Id, alphaAdminId, "tenant.created", "Tenant", alpha.Id.ToString()));
        context.AuditEntries.Add(AuditEntry.Record(beta.Id, betaAdminId, "tenant.created", "Tenant", beta.Id.ToString()));

        await context.SaveChangesAsync();

        return new SeedIds(
            alpha.Id, beta.Id, alphaAdminId, alphaMemberId, alphaGuestId,
            publicBoard.Id, privateBoard.Id, secretBoard.Id, betaBoard.Id);
    }

    private sealed record SeedIds(
        Guid AlphaTenant,
        Guid BetaTenant,
        Guid AlphaAdmin,
        Guid AlphaMember,
        Guid AlphaGuest,
        Guid PublicBoard,
        Guid PrivateBoard,
        Guid SecretBoard,
        Guid BetaBoard);

    private static ITenantContext Resolved(Guid tenantId, Guid userId, TenantRole role)
    {
        var context = new TenantContext();
        context.Resolve(tenantId, userId, role);
        return context;
    }

    [Fact]
    public async Task Boards_AreInvisible_AcrossTenants()
    {
        var database = Guid.NewGuid().ToString("n");
        var ids = await SeedAsync(database);

        await using var alpha = NewContext(database, Resolved(ids.AlphaTenant, ids.AlphaAdmin, TenantRole.Admin));

        var boards = await alpha.Boards.Select(board => board.Name).ToListAsync();

        Assert.DoesNotContain("Beta public", boards);
        Assert.Equal(3, boards.Count);
    }

    [Fact]
    public async Task Memberships_AreInvisible_AcrossTenants()
    {
        var database = Guid.NewGuid().ToString("n");
        var ids = await SeedAsync(database);

        await using var beta = NewContext(database, Resolved(ids.BetaTenant, ids.AlphaAdmin, TenantRole.Admin));

        var memberships = await beta.Memberships.ToListAsync();

        Assert.Single(memberships);
        Assert.Equal(ids.BetaTenant, memberships[0].TenantId);
    }

    [Fact]
    public async Task UnresolvedTenant_SeesNothing()
    {
        var database = Guid.NewGuid().ToString("n");
        await SeedAsync(database);

        await using var anonymous = NewContext(database, new TenantContext());

        Assert.Empty(await anonymous.Boards.ToListAsync());
        Assert.Empty(await anonymous.Workspaces.ToListAsync());
        Assert.Empty(await anonymous.AuditEntries.ToListAsync());
    }

    [Fact]
    public async Task CrossTenantWrite_IsRejected()
    {
        var database = Guid.NewGuid().ToString("n");
        var ids = await SeedAsync(database);

        await using var alpha = NewContext(database, Resolved(ids.AlphaTenant, ids.AlphaAdmin, TenantRole.Admin));

        // A board built for Beta, saved while the request is scoped to Alpha.
        var intruder = Board.Create(ids.BetaTenant, ids.SecretBoard, "Smuggled", BoardType.Main, ids.AlphaAdmin);
        alpha.Boards.Add(intruder);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => alpha.SaveChangesAsync());

        Assert.Contains("Cross-tenant write rejected", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditEntries_AreTenantScoped()
    {
        var database = Guid.NewGuid().ToString("n");
        var ids = await SeedAsync(database);

        await using var alpha = NewContext(database, Resolved(ids.AlphaTenant, ids.AlphaAdmin, TenantRole.Admin));

        var entries = await alpha.AuditEntries.ToListAsync();

        Assert.All(entries, entry => Assert.Equal(ids.AlphaTenant, entry.TenantId));
        Assert.Single(entries);
    }

    [Fact]
    public async Task Guest_SeesOnlyGrantedBoard()
    {
        var database = Guid.NewGuid().ToString("n");
        var ids = await SeedAsync(database);

        await using var alpha = NewContext(database, Resolved(ids.AlphaTenant, ids.AlphaGuest, TenantRole.Guest));
        var queries = new WorkspaceQueries(alpha);

        var cards = await queries.GetVisibleBoardsAsync(ids.AlphaGuest, TenantRole.Guest);

        Assert.Single(cards);
        Assert.Equal("Alpha public", cards[0].Name);
        Assert.True(cards[0].IsGranted);
    }

    [Fact]
    public async Task Guest_UngrantedBoard_IsNotFound_NotForbidden()
    {
        var database = Guid.NewGuid().ToString("n");
        var ids = await SeedAsync(database);

        await using var alpha = NewContext(database, Resolved(ids.AlphaTenant, ids.AlphaGuest, TenantRole.Guest));
        var queries = new WorkspaceQueries(alpha);

        Assert.Null(await queries.FindBoardAsync(ids.PrivateBoard, ids.AlphaGuest, TenantRole.Guest));
        Assert.Null(await queries.FindBoardAsync(ids.SecretBoard, ids.AlphaGuest, TenantRole.Guest));
        Assert.NotNull(await queries.FindBoardAsync(ids.PublicBoard, ids.AlphaGuest, TenantRole.Guest));
    }

    [Fact]
    public async Task PrivateBoard_IsHiddenFromMember_AndVisibleToAdmin()
    {
        var database = Guid.NewGuid().ToString("n");
        var ids = await SeedAsync(database);

        await using var asMember = NewContext(database, Resolved(ids.AlphaTenant, ids.AlphaMember, TenantRole.Member));
        var memberView = new WorkspaceQueries(asMember);
        Assert.Null(await memberView.FindBoardAsync(ids.PrivateBoard, ids.AlphaMember, TenantRole.Member));

        await using var asAdmin = NewContext(database, Resolved(ids.AlphaTenant, ids.AlphaAdmin, TenantRole.Admin));
        var adminView = new WorkspaceQueries(asAdmin);
        Assert.NotNull(await adminView.FindBoardAsync(ids.PrivateBoard, ids.AlphaAdmin, TenantRole.Admin));
    }

    [Fact]
    public async Task ForeignTenantBoard_IsNotFound_EvenForTenantAdmin()
    {
        var database = Guid.NewGuid().ToString("n");
        var ids = await SeedAsync(database);

        await using var alpha = NewContext(database, Resolved(ids.AlphaTenant, ids.AlphaAdmin, TenantRole.Admin));
        var queries = new WorkspaceQueries(alpha);

        Assert.Null(await queries.FindBoardAsync(ids.BetaBoard, ids.AlphaAdmin, TenantRole.Admin));
    }
}
