using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using Tuesday.Application.Tenancy;
using Tuesday.Domain.Auditing;
using Tuesday.Domain.Automation;
using Tuesday.Domain.Boards;
using Tuesday.Domain.Collaboration;
using Tuesday.Domain.Views;
using Tuesday.Domain.Items;
using Tuesday.Domain.Tenancy;
using Tuesday.Domain.Workspaces;
using Tuesday.Infrastructure.Identity;

namespace Tuesday.Infrastructure.Persistence;

/// <summary>
/// One context for identity and domain data, so a user and their membership commit
/// together. Every tenant-owned entity gets a query filter in <see cref="OnModelCreating"/>.
/// </summary>
public sealed class TuesdayDbContext(
    DbContextOptions<TuesdayDbContext> options,
    ITenantContext tenantContext)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Board> Boards => Set<Board>();

    public DbSet<BoardGrant> BoardGrants => Set<BoardGrant>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<BoardColumn> BoardColumns => Set<BoardColumn>();

    public DbSet<ItemGroup> ItemGroups => Set<ItemGroup>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<SubitemBoard> SubitemBoards => Set<SubitemBoard>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<ItemColumnValue> ItemColumnValues => Set<ItemColumnValue>();

    public DbSet<BoardView> BoardViews => Set<BoardView>();

    public DbSet<ItemDependency> ItemDependencies => Set<ItemDependency>();

    public DbSet<Update> Updates => Set<Update>();

    public DbSet<UpdateLike> UpdateLikes => Set<UpdateLike>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<Automation> Automations => Set<Automation>();

    public DbSet<AutomationRun> AutomationRuns => Set<AutomationRun>();

    public DbSet<Webhook> Webhooks => Set<Webhook>();

    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    /// <summary>
    /// Read by every tenant filter. EF Core parameterises this per query, so the same
    /// compiled query serves every tenant without leaking one into another.
    /// </summary>
    public Guid? TenantFilterValue => tenantContext.TenantId;

    /// <summary>
    /// Refuses a write that would store a row under a tenant other than the resolved
    /// one. The query filter stops reads; this stops writes, which the filter cannot.
    /// </summary>
    private void EnforceTenantOnWrite()
    {
        if (TenantFilterValue is not { } tenantId)
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (entry.Entity.TenantId != tenantId)
            {
                throw new InvalidOperationException(
                    $"Cross-tenant write rejected: {entry.Metadata.ClrType.Name} belongs to " +
                    $"tenant {entry.Entity.TenantId} while the request is scoped to {tenantId}.");
            }
        }
    }

    public override int SaveChanges()
    {
        EnforceTenantOnWrite();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceTenantOnWrite();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // OpenIddict's client/token/scope stores live in this same context. Without this
        // call the entities are never mapped and the migration comes out empty.
        builder.UseOpenIddict();

        builder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();

            // The tenant is its own boundary; TenantId is computed from Id and must not
            // become a column.
            entity.Ignore(x => x.TenantId);
            entity.HasQueryFilter(tenant => TenantFilterValue != null && tenant.Id == TenantFilterValue);
        });

        builder.Entity<Membership>(entity =>
        {
            entity.ToTable("memberships");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        });

        builder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => new { x.TenantId, x.Name });
        });

        builder.Entity<Board>(entity =>
        {
            entity.ToTable("boards");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => new { x.TenantId, x.WorkspaceId });
        });

        builder.Entity<BoardGrant>(entity =>
        {
            entity.ToTable("board_grants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Level).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => new { x.BoardId, x.UserId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId });
        });

        builder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("audit_entries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Detail).HasMaxLength(2000);
            entity.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        builder.Entity<BoardColumn>(entity =>
        {
            entity.ToTable("board_columns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.SettingsJson).HasMaxLength(4000);
            entity.HasIndex(x => new { x.BoardId, x.Key }).IsUnique();
            entity.HasIndex(x => new { x.BoardId, x.Position });
        });

        builder.Entity<ItemGroup>(entity =>
        {
            entity.ToTable("item_groups");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Colour).HasMaxLength(40).IsRequired();
            entity.HasIndex(x => new { x.BoardId, x.Position });
        });

        builder.Entity<Item>(entity =>
        {
            entity.ToTable("items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Seq).UseIdentityByDefaultColumn();
            entity.HasIndex(x => new { x.BoardId, x.Seq }).IsUnique();
            entity.HasIndex(x => new { x.BoardId, x.Position });
            entity.HasIndex(x => new { x.GroupId, x.Position });

            // Sub-items are read per parent when the grid expands a row, and the parent must
            // live on the same board — enforced by the index, filtered by the query.
            entity.HasIndex(x => new { x.ParentItemId, x.Position });
        });

        builder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_keys");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Endpoint).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ResponseJson).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.Key }).IsUnique();
            entity.HasIndex(x => x.ExpiresAt);
        });

        builder.Entity<SubitemBoard>(entity =>
        {
            entity.ToTable("subitem_boards");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();

            // One implicit sub-item surface per parent board.
            entity.HasIndex(x => x.BoardId).IsUnique();
        });

        builder.Entity<ItemColumnValue>(entity =>
        {
            entity.ToTable("item_column_values");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.NumberValue).HasPrecision(18, 4);

            // PostgreSQL maps DateOnly to DATE natively, so the domain type needs no converter.
            entity.Property(x => x.DateValue).HasColumnType("date");
            entity.HasIndex(x => new { x.ItemId, x.ColumnId }).IsUnique();

            // Projection indexes: a filter or sort on a typed column must not scan.
            entity.HasIndex(x => new { x.ColumnId, x.NumberValue });
            entity.HasIndex(x => new { x.ColumnId, x.DateValue });
            entity.HasIndex(x => new { x.ColumnId, x.TextValue });
        });

        builder.Entity<BoardView>(entity =>
        {
            entity.ToTable("board_views");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.HasIndex(x => new { x.BoardId, x.Position });
        });

        builder.Entity<ItemDependency>(entity =>
        {
            entity.ToTable("item_dependencies");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ItemId, x.DependsOnItemId }).IsUnique();
            entity.HasIndex(x => x.BoardId);
        });

        builder.Entity<Update>(entity =>
        {
            entity.ToTable("updates");
            entity.HasKey(x => x.Id);

            // Both bodies are long text: an update is prose, not a column value. The plain
            // copy exists so search never has to strip markup at query time.
            entity.Property(x => x.BodyHtml).HasColumnType("text");
            entity.Property(x => x.BodyText).HasColumnType("text");

            entity.HasIndex(x => new { x.ItemId, x.CreatedAt });
            entity.HasIndex(x => new { x.BoardId, x.CreatedAt });
            entity.HasIndex(x => x.ParentId);
            entity.HasIndex(x => x.AuthorId);
        });

        builder.Entity<UpdateLike>(entity =>
        {
            entity.ToTable("update_likes");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UpdateId, x.UserId }).IsUnique();
        });

        builder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(300);
            entity.Property(x => x.Body).HasMaxLength(1000);
            entity.Property(x => x.LinkUrl).HasMaxLength(1000);
            entity.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
            entity.HasIndex(x => new { x.UserId, x.UpdateId });
        });

        builder.Entity<NotificationPreference>(entity =>
        {
            entity.ToTable("notification_preferences");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UserId).IsUnique();
        });

        builder.Entity<Attachment>(entity =>
        {
            entity.ToTable("attachments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(260);
            entity.Property(x => x.ContentType).HasMaxLength(120);
            entity.Property(x => x.StorageKey).HasMaxLength(400);
            entity.HasIndex(x => new { x.ItemId, x.CreatedAt });
            entity.HasIndex(x => x.StorageKey).IsUnique();
        });

        builder.Entity<Automation>(entity =>
        {
            entity.ToTable("automations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Condition).HasMaxLength(500);
            entity.Property(x => x.ActionValue).HasMaxLength(500);
            entity.HasIndex(x => new { x.BoardId, x.Trigger, x.Enabled });
        });

        builder.Entity<AutomationRun>(entity =>
        {
            entity.ToTable("automation_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Message).HasMaxLength(500);
            entity.HasIndex(x => new { x.AutomationId, x.CreatedAt });
        });

        builder.Entity<Webhook>(entity =>
        {
            entity.ToTable("webhooks");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Url).HasMaxLength(1000);
            entity.Property(x => x.Secret).HasMaxLength(200);
            entity.HasIndex(x => new { x.BoardId, x.Enabled });
        });

        builder.Entity<ApiToken>(entity =>
        {
            entity.ToTable("api_tokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.Prefix).HasMaxLength(20);
            entity.Property(x => x.Hash).HasMaxLength(128);
            entity.HasIndex(x => x.Hash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.RevokedAt });
        });

        ApplyTenantFilters(builder);
    }

    /// <summary>
    /// Applies a tenant filter to every entity implementing <see cref="ITenantOwned"/>.
    /// Reflection is deliberate: a new tenant-owned entity is protected the moment it
    /// implements the interface, with no per-entity registration to forget.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder builder)
    {
        var applyFilter = typeof(TuesdayDbContext)
            .GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned())
            {
                continue;
            }

            // The tenant is filtered on its own key, configured explicitly above.
            if (entityType.ClrType == typeof(Tenant))
            {
                continue;
            }

            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            applyFilter.MakeGenericMethod(entityType.ClrType).Invoke(this, [builder]);
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder builder)
        where TEntity : class, ITenantOwned
    {
        builder.Entity<TEntity>()
            .HasQueryFilter(entity => TenantFilterValue != null && entity.TenantId == TenantFilterValue);
    }
}
