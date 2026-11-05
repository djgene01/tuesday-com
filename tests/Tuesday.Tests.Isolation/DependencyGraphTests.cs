using Microsoft.EntityFrameworkCore;
using Tuesday.Application.Tenancy;
using Tuesday.Domain.Boards;
using Tuesday.Domain.Items;
using Tuesday.Domain.Tenancy;
using Tuesday.Domain.Workspaces;
using Tuesday.Infrastructure.Persistence;
using Tuesday.Infrastructure.Views;

namespace Tuesday.Tests.Isolation;

/// <summary>
/// A dependency graph that closes a loop makes a Gantt wrong rather than ugly, so the
/// refusal is tested directly: transitive cycles, the direct pair, and self-reference.
/// </summary>
public sealed class DependencyGraphTests
{
    private sealed class TestFactory(DbContextOptions<TuesdayDbContext> options, ITenantContext tenant)
        : IDbContextFactory<TuesdayDbContext>
    {
        public TuesdayDbContext CreateDbContext() => new(options, tenant);
    }

    private static async Task<(TestFactory Factory, Guid BoardId, Guid A, Guid B, Guid C, Guid TenantId)> SeedAsync()
    {
        var tenantContext = new TenantContext();
        var options = new DbContextOptionsBuilder<TuesdayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("n"))
            .Options;

        var factory = new TestFactory(options, tenantContext);
        var tenant = Tenant.Create("Tenant", "tenant-" + Guid.NewGuid().ToString("n")[..8]);
        var owner = Guid.CreateVersion7();

        await using var db = factory.CreateDbContext();
        db.Tenants.Add(tenant);
        db.Memberships.Add(Membership.Create(tenant.Id, owner, TenantRole.Admin));

        var workspace = Workspace.Create(tenant.Id, "W", WorkspaceVisibility.Open, owner);
        var board = Board.Create(tenant.Id, workspace.Id, "B", BoardType.Main, owner);
        db.Workspaces.Add(workspace);
        db.Boards.Add(board);

        var group = ItemGroup.Create(tenant.Id, board.Id, "G", "slate", 0);
        db.ItemGroups.Add(group);

        var a = Item.Create(tenant.Id, board.Id, group.Id, "A", 0);
        var b = Item.Create(tenant.Id, board.Id, group.Id, "B", 1);
        var c = Item.Create(tenant.Id, board.Id, group.Id, "C", 2);
        db.Items.AddRange(a, b, c);

        await db.SaveChangesAsync();
        tenantContext.Resolve(tenant.Id, owner, TenantRole.Admin);

        return (factory, board.Id, a.Id, b.Id, c.Id, tenant.Id);
    }

    [Fact]
    public async Task Chain_IsAccepted_ThenTheClosingEdgeIsRefused()
    {
        var (factory, boardId, a, b, c, tenantId) = await SeedAsync();
        var service = new DependencyService(factory);

        Assert.True((await service.AddAsync(boardId, b, a, tenantId)).Succeeded);
        Assert.True((await service.AddAsync(boardId, c, b, tenantId)).Succeeded);

        // A depends on C would close A -> C -> B -> A.
        var closing = await service.AddAsync(boardId, a, c, tenantId);

        Assert.False(closing.Succeeded);
        Assert.Contains("cycle", closing.Error, StringComparison.OrdinalIgnoreCase);

        var stored = await service.ListAsync(boardId);
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task DirectPair_IsRefusedInTheReverseDirection()
    {
        var (factory, boardId, a, b, _, tenantId) = await SeedAsync();
        var service = new DependencyService(factory);

        Assert.True((await service.AddAsync(boardId, b, a, tenantId)).Succeeded);

        var reversed = await service.AddAsync(boardId, a, b, tenantId);

        Assert.False(reversed.Succeeded);
    }

    [Fact]
    public async Task SelfDependency_IsRefused()
    {
        var (factory, boardId, a, _, _, tenantId) = await SeedAsync();
        var service = new DependencyService(factory);

        var result = await service.AddAsync(boardId, a, a, tenantId);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AddingTheSameEdgeTwice_IsIdempotent()
    {
        var (factory, boardId, a, b, _, tenantId) = await SeedAsync();
        var service = new DependencyService(factory);

        Assert.True((await service.AddAsync(boardId, b, a, tenantId)).Succeeded);
        Assert.True((await service.AddAsync(boardId, b, a, tenantId)).Succeeded);

        Assert.Single(await service.ListAsync(boardId));
    }
}
