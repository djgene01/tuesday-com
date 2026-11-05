using Microsoft.EntityFrameworkCore;
using Tuesday.Application.Tenancy;
using Tuesday.Domain.Boards;
using Tuesday.Domain.Items;
using Tuesday.Domain.Tenancy;
using Tuesday.Domain.Workspaces;
using Tuesday.Infrastructure.Persistence;
using Tuesday.Infrastructure.Values;

namespace Tuesday.Tests.Isolation;

/// <summary>
/// A cell is written as canonical JSON plus typed projections. These tests pin both: if
/// the writer ever sets one without the other, a filter would disagree with the grid.
/// </summary>
public sealed class ProjectionConsistencyTests
{
    private sealed class TestFactory(DbContextOptions<TuesdayDbContext> options, ITenantContext tenant)
        : IDbContextFactory<TuesdayDbContext>
    {
        public TuesdayDbContext CreateDbContext() => new(options, tenant);
    }

    private static async Task<(TestFactory Factory, Guid ItemId, Dictionary<string, Guid> Columns)> SeedAsync()
    {
        var tenantContext = new TenantContext();

        var options = new DbContextOptionsBuilder<TuesdayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("n"))
            .Options;

        var factory = new TestFactory(options, tenantContext);

        var tenant = Tenant.Create("Tenant", "tenant-" + Guid.NewGuid().ToString("n")[..8]);
        var ownerId = Guid.CreateVersion7();

        await using var db = factory.CreateDbContext();
        db.Tenants.Add(tenant);
        db.Memberships.Add(Membership.Create(tenant.Id, ownerId, TenantRole.Admin));

        var workspace = Workspace.Create(tenant.Id, "Work", WorkspaceVisibility.Open, ownerId);
        var board = Board.Create(tenant.Id, workspace.Id, "Board", BoardType.Main, ownerId);
        db.Workspaces.Add(workspace);
        db.Boards.Add(board);

        var group = ItemGroup.Create(tenant.Id, board.Id, "Group", "slate", 0);
        db.ItemGroups.Add(group);

        var item = Item.Create(tenant.Id, board.Id, group.Id, "Item", 0);
        db.Items.Add(item);

        var columns = new Dictionary<string, Guid>();
        var types = new[]
        {
            ("text", ColumnType.Text),
            ("number", ColumnType.Number),
            ("date", ColumnType.Date),
            ("check", ColumnType.Checkbox)
        };

        var position = 0;
        foreach (var (key, type) in types)
        {
            var column = BoardColumn.Create(tenant.Id, board.Id, key, key, type, position++);
            db.BoardColumns.Add(column);
            columns[key] = column.Id;
        }

        await db.SaveChangesAsync();

        tenantContext.Resolve(tenant.Id, ownerId, TenantRole.Admin);
        return (factory, item.Id, columns);
    }

    [Theory]
    [InlineData("42.5", "42.5")]
    [InlineData("1,000", "1000")]
    public async Task Number_KeepsJsonAndProjectionInStep(string raw, string expectedText)
    {
        var (factory, itemId, columns) = await SeedAsync();
        var writer = new ColumnValueWriter(factory);

        var result = await writer.SetAsync(itemId, columns["number"], raw);
        Assert.True(result.Succeeded, result.Error);

        await using var db = factory.CreateDbContext();
        var cell = await db.ItemColumnValues.SingleAsync(c => c.ItemId == itemId && c.ColumnId == columns["number"]);

        Assert.NotNull(cell.ValueJson);
        Assert.Equal(decimal.Parse(expectedText, System.Globalization.CultureInfo.InvariantCulture), cell.NumberValue);
        Assert.Equal(expectedText, cell.TextValue);
    }

    [Fact]
    public async Task Date_StoresIsoJsonAndDateProjection()
    {
        var (factory, itemId, columns) = await SeedAsync();
        var writer = new ColumnValueWriter(factory);

        var result = await writer.SetAsync(itemId, columns["date"], "2026-11-03");
        Assert.True(result.Succeeded, result.Error);

        await using var db = factory.CreateDbContext();
        var cell = await db.ItemColumnValues.SingleAsync(c => c.ItemId == itemId && c.ColumnId == columns["date"]);

        Assert.Equal("\"2026-11-03\"", cell.ValueJson);
        Assert.Equal(new DateOnly(2026, 11, 3), cell.DateValue);
        Assert.Null(cell.NumberValue);
    }

    [Fact]
    public async Task InvalidNumber_IsRejectedAndWritesNothing()
    {
        var (factory, itemId, columns) = await SeedAsync();
        var writer = new ColumnValueWriter(factory);

        var result = await writer.SetAsync(itemId, columns["number"], "not a number");

        Assert.False(result.Succeeded);
        Assert.Equal("Enter a number.", result.Error);

        await using var db = factory.CreateDbContext();
        Assert.False(await db.ItemColumnValues.AnyAsync(c => c.ColumnId == columns["number"]));
    }

    [Fact]
    public async Task ClearingACell_RemovesEveryProjection()
    {
        var (factory, itemId, columns) = await SeedAsync();
        var writer = new ColumnValueWriter(factory);

        Assert.True((await writer.SetAsync(itemId, columns["number"], "7")).Succeeded);
        Assert.True((await writer.SetAsync(itemId, columns["number"], "")).Succeeded);

        await using var db = factory.CreateDbContext();
        var cell = await db.ItemColumnValues.SingleAsync(c => c.ItemId == itemId && c.ColumnId == columns["number"]);

        Assert.Null(cell.ValueJson);
        Assert.Null(cell.NumberValue);
        Assert.Null(cell.TextValue);
    }

    [Fact]
    public async Task ColumnLimit_RefusesTheFiveHundredAndFirstColumn()
    {
        var (factory, _, _) = await SeedAsync();

        await using var db = factory.CreateDbContext();
        var board = await db.Boards.SingleAsync();

        // Fill to the limit directly; the service is what must refuse the next one.
        for (var index = 0; index < BoardColumn.MaxColumnsPerBoard; index++)
        {
            db.BoardColumns.Add(BoardColumn.Create(
                board.TenantId, board.Id, $"bulk_{index}", $"Bulk {index}", ColumnType.Text, 100 + index));
        }

        await db.SaveChangesAsync();

        var service = new BoardColumnService(factory);
        var result = await service.AddAsync(board.Id, "Overflow", ColumnType.Text, board.TenantId);

        Assert.False(result.Succeeded);
        Assert.Contains("500", result.Error, StringComparison.Ordinal);
    }
}
