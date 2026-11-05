using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tuesday.Domain.Auditing;
using Tuesday.Domain.Automation;
using Tuesday.Domain.Boards;
using Tuesday.Domain.Collaboration;
using Tuesday.Domain.Items;
using Tuesday.Domain.Tenancy;
using Tuesday.Domain.Views;
using Tuesday.Domain.Workspaces;
using Tuesday.Infrastructure.Identity;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Tenancy;

/// <summary>
/// Creates the demo tenant and one account per role so a fresh clone can be signed into
/// as each role. Development only, and only when the database has no tenants.
/// </summary>
public sealed class DevelopmentSeeder(
    TuesdayDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ILogger<DevelopmentSeeder> logger)
{
    public const string AdminEmail = "admin@tuesday.local";
    public const string MemberEmail = "member@tuesday.local";
    public const string ViewerEmail = "viewer@tuesday.local";
    public const string GuestEmail = "guest@tuesday.local";

    public async Task SeedAsync(string password, CancellationToken cancellationToken = default)
    {
        // Startup runs before any tenant is resolved, so this one probe bypasses the
        // filter on purpose: it is asking whether the installation is empty.
        if (await dbContext.Tenants.IgnoreQueryFilters().AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var tenant = Tenant.Create("Tuesday Demo", "demo");
        dbContext.Tenants.Add(tenant);

        var admin = await EnsureUserAsync(AdminEmail, "Ada Admin", password).ConfigureAwait(false);
        var member = await EnsureUserAsync(MemberEmail, "Mo Member", password).ConfigureAwait(false);
        var viewer = await EnsureUserAsync(ViewerEmail, "Vic Viewer", password).ConfigureAwait(false);
        var guest = await EnsureUserAsync(GuestEmail, "Gia Guest", password).ConfigureAwait(false);

        dbContext.Memberships.AddRange(
            Membership.Create(tenant.Id, admin.Id, TenantRole.Admin),
            Membership.Create(tenant.Id, member.Id, TenantRole.Member),
            Membership.Create(tenant.Id, viewer.Id, TenantRole.Viewer),
            Membership.Create(tenant.Id, guest.Id, TenantRole.Guest));

        var workspace = Workspace.Create(tenant.Id, "Marketing", WorkspaceVisibility.Open, admin.Id);
        var privateWorkspace = Workspace.Create(tenant.Id, "Leadership", WorkspaceVisibility.Private, admin.Id);
        dbContext.Workspaces.AddRange(workspace, privateWorkspace);

        var campaignBoard = Board.Create(tenant.Id, workspace.Id, "Campaign calendar", BoardType.Main, admin.Id);
        var launchBoard = Board.Create(tenant.Id, workspace.Id, "Launch plan", BoardType.Private, admin.Id);
        var budgetBoard = Board.Create(tenant.Id, privateWorkspace.Id, "FY27 budget", BoardType.Private, admin.Id);
        dbContext.Boards.AddRange(campaignBoard, launchBoard, budgetBoard);

        // The guest holds exactly one grant. Every other board must 404 for them.
        dbContext.BoardGrants.Add(BoardGrant.Create(tenant.Id, campaignBoard.Id, guest.Id, GrantLevel.View));

        SeedBoardContent(dbContext, tenant, campaignBoard, [admin, member, viewer, guest]);

        dbContext.AuditEntries.Add(AuditEntry.Record(
            tenant.Id,
            admin.Id,
            "tenant.created",
            nameof(Tenant),
            tenant.Id.ToString(),
            "Development seed"));

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Seeded tenant {TenantSlug} with 4 roles, 2 workspaces and 3 boards.",
            tenant.Slug);
    }

    /// <summary>
    /// Prepares an empty installation for its first sign-in. Refuses to touch an instance that
    /// already has a tenant, so calling it on every start is safe.
    /// </summary>
    public async Task<bool> BootstrapAdminAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (await dbContext.Tenants.IgnoreQueryFilters().AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var tenant = Tenant.Create("Workspace", "default");
        dbContext.Tenants.Add(tenant);

        var admin = await EnsureUserAsync(email, "Administrator", password).ConfigureAwait(false);

        dbContext.Memberships.Add(Membership.Create(tenant.Id, admin.Id, TenantRole.Admin));

        var workspace = Workspace.Create(tenant.Id, "My workspace", WorkspaceVisibility.Open, admin.Id);
        dbContext.Workspaces.Add(workspace);

        dbContext.AuditEntries.Add(AuditEntry.Record(
            tenant.Id,
            admin.Id,
            "tenant.created",
            nameof(Tenant),
            tenant.Id.ToString(),
            "First admin bootstrap"));

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Bootstrapped tenant {Slug} with first admin {Email}.", tenant.Slug, email);

        return true;
    }

    /// <summary>
    /// Gives the demo board a realistic shape: eight columns, three groups and 500 items
    /// with four populated columns each.
    /// </summary>
    private static void SeedBoardContent(
        TuesdayDbContext dbContext,
        Tenant tenant,
        Board board,
        IReadOnlyList<ApplicationUser> people)
    {
        var columns = new[]
        {
            BoardColumn.Create(tenant.Id, board.Id, "task", "Task", ColumnType.Text, 0),
            BoardColumn.Create(tenant.Id, board.Id, "owner", "Owner", ColumnType.People, 1),
            BoardColumn.Create(tenant.Id, board.Id, "status", "Status", ColumnType.Status, 2,
                """{"labels":["Not started","Working on it","Stuck","Done"]}"""),
            BoardColumn.Create(tenant.Id, board.Id, "due", "Due date", ColumnType.Date, 3),
            BoardColumn.Create(tenant.Id, board.Id, "effort", "Effort (days)", ColumnType.Number, 4),
            BoardColumn.Create(tenant.Id, board.Id, "channel", "Channel", ColumnType.Dropdown, 5,
                """{"options":["Email","Social","Event","Paid"]}"""),
            BoardColumn.Create(tenant.Id, board.Id, "approved", "Approved", ColumnType.Checkbox, 6),
            BoardColumn.Create(tenant.Id, board.Id, "notes", "Notes", ColumnType.LongText, 7),
            BoardColumn.Create(tenant.Id, board.Id, "end", "End date", ColumnType.Date, 8),
            BoardColumn.Create(tenant.Id, board.Id, "waits_on", "Waits on", ColumnType.Dependency, 9)
        };

        dbContext.BoardColumns.AddRange(columns);

        // The sub-item surface starts with its own columns, independent of the parent's.
        var subitemBoard = SubitemBoard.Create(tenant.Id, board.Id);
        dbContext.SubitemBoards.Add(subitemBoard);

        var subitemColumns = new[]
        {
            BoardColumn.Create(tenant.Id, subitemBoard.Id, "owner", "Owner", ColumnType.People, 0),
            BoardColumn.Create(tenant.Id, subitemBoard.Id, "status", "Status", ColumnType.Status, 1,
                """{"labels":["Not started","Working on it","Stuck","Done"]}"""),
            BoardColumn.Create(tenant.Id, subitemBoard.Id, "due", "Due date", ColumnType.Date, 2)
        };

        dbContext.BoardColumns.AddRange(subitemColumns);

        // Views are projections, so seeding four of them adds no item data at all.
        dbContext.BoardViews.AddRange(
            BoardView.Create(tenant.Id, board.Id, "Main table", ViewType.Grid, 0),
            BoardView.Create(tenant.Id, board.Id, "Status board", ViewType.Kanban, 1,
                $$"""{"statusColumnId":"{{columns[2].Id}}","hideEmptyLanes":true}"""),
            BoardView.Create(tenant.Id, board.Id, "Campaign calendar", ViewType.Calendar, 2,
                $$"""{"dateColumnId":"{{columns[3].Id}}"}"""),
            BoardView.Create(tenant.Id, board.Id, "Campaign timeline", ViewType.Timeline, 3,
                $$"""{"startColumnId":"{{columns[3].Id}}","endColumnId":"{{columns[8].Id}}"}"""),
            BoardView.Create(tenant.Id, board.Id, "Launch gantt", ViewType.Gantt, 4,
                $$"""{"startColumnId":"{{columns[3].Id}}","endColumnId":"{{columns[8].Id}}"}"""));

        var groups = new[]
        {
            ItemGroup.Create(tenant.Id, board.Id, "Backlog", "slate", 0),
            ItemGroup.Create(tenant.Id, board.Id, "In progress", "amber", 1),
            ItemGroup.Create(tenant.Id, board.Id, "Done", "teal", 2)
        };

        dbContext.ItemGroups.AddRange(groups);

        string[] statuses = ["Not started", "Working on it", "Stuck", "Done"];
        string[] channels = ["Email", "Social", "Event", "Paid"];
        var start = new DateOnly(2026, 11, 3);

        var created = new List<Item>();

        for (var index = 0; index < 500; index++)
        {
            var group = groups[index % groups.Length];
            var item = Item.Create(tenant.Id, board.Id, group.Id, $"Task {index + 1:D3}", index);
            dbContext.Items.Add(item);

            var owner = people[index % people.Count];

            dbContext.ItemColumnValues.AddRange(
                Cell(dbContext, tenant, item, columns[1], owner.Id.ToString(), text: owner.DisplayName),
                Cell(dbContext, tenant, item, columns[2], statuses[index % statuses.Length], text: statuses[index % statuses.Length]),
                Cell(dbContext, tenant, item, columns[3], start.AddDays(index % 90).ToString("yyyy-MM-dd"), date: start.AddDays(index % 90)),
                Cell(dbContext, tenant, item, columns[4], ((index % 13) + 1).ToString(), number: (index % 13) + 1),
                Cell(dbContext, tenant, item, columns[5], channels[index % channels.Length], text: channels[index % channels.Length]),
                Cell(
                    dbContext,
                    tenant,
                    item,
                    columns[8],
                    start.AddDays((index % 90) + (index % 13) + 1).ToString("yyyy-MM-dd"),
                    date: start.AddDays((index % 90) + (index % 13) + 1)));

            created.Add(item);
        }

        // A handful of sub-items on the first rows so a fresh install demonstrates nesting
        // without inflating the seeded board.
        string[][] subItemNames =
        [
            ["Draft the brief", "Collect references", "Get sign-off"],
            ["Book the venue", "Confirm the speaker"],
            ["Write the landing copy", "Review with legal"]
        ];

        for (var index = 0; index < subItemNames.Length; index++)
        {
            var parent = created[index];

            for (var sub = 0; sub < subItemNames[index].Length; sub++)
            {
                var child = Item.Create(
                    tenant.Id,
                    board.Id,
                    parent.GroupId,
                    subItemNames[index][sub],
                    sub,
                    parent.Id);

                dbContext.Items.Add(child);

                dbContext.ItemColumnValues.AddRange(
                    Cell(dbContext, tenant, child, subitemColumns[0], people[sub % people.Count].Id.ToString(), text: people[sub % people.Count].DisplayName),
                    Cell(dbContext, tenant, child, subitemColumns[1], statuses[(index + sub) % statuses.Length], text: statuses[(index + sub) % statuses.Length]));
            }
        }

        // A sparse chain of edges per group gives Gantt real arrows without a dense graph.
        for (var index = 3; index < 60; index += 3)
        {
            dbContext.ItemDependencies.Add(ItemDependency.Create(
                tenant.Id, board.Id, created[index].Id, created[index - 3].Id));
        }

        // A stand-up thread on the first few items, so the panel has a conversation from seed
        // instead of an empty state that hides layout problems.
        var authors = people.Select(person => person.Id).ToArray();

        // Two rules that between them show both halves of the engine: a matching condition
        // with a side effect, and a condition that will not match unless someone types Stuck.
        dbContext.Automations.Add(Automation.Create(
            tenant.Id,
            board.Id,
            "Tell the team when work is done",
            AutomationTrigger.CellChanged,
            "status=Done",
            AutomationAction.NotifyOwner,
            "This item is done — please review before the stand-up.",
            people[0].Id));

        dbContext.Automations.Add(Automation.Create(
            tenant.Id,
            board.Id,
            "Date a stuck item today",
            AutomationTrigger.CellChanged,
            "status=Stuck",
            AutomationAction.SetDate,
            "due",
            people[0].Id));

        for (var index = 0; index < 6; index++)
        {
            var item = created[index];
            var author = authors[index % authors.Length];
            var body = $"<p>Stand-up: <strong>{item.Name}</strong> is on track for this sprint.</p>";

            var root = Update.Create(tenant.Id, board.Id, item.Id, author, body, $"Stand-up: {item.Name} is on track for this sprint.");
            dbContext.Updates.Add(root);

            var reply = Update.Create(
                tenant.Id,
                board.Id,
                item.Id,
                authors[(index + 1) % authors.Length],
                "<p>Noted — I will pick up the review today.</p>",
                "Noted — I will pick up the review today.",
                root.Id);
            dbContext.Updates.Add(reply);

            dbContext.UpdateLikes.Add(UpdateLike.Create(tenant.Id, root.Id, authors[(index + 2) % authors.Length]));
        }

        static ItemColumnValue Cell(
            TuesdayDbContext dbContext,
            Tenant tenant,
            Item item,
            BoardColumn column,
            string json,
            string? text = null,
            decimal? number = null,
            DateOnly? date = null)
        {
            var cell = ItemColumnValue.Create(tenant.Id, item.Id, column.Id);
            cell.Set(json, text, number, date, null);
            return cell;
        }
    }

    private async Task<ApplicationUser> EnsureUserAsync(string email, string displayName, string password)
    {
        var existing = await userManager.FindByEmailAsync(email).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName
        };

        var result = await userManager.CreateAsync(user, password).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not seed {email}: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }

        return user;
    }
}
