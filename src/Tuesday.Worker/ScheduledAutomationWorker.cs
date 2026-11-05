using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tuesday.Application.Tenancy;
using Tuesday.Domain.Boards;
using Tuesday.Domain.Tenancy;
using Tuesday.Infrastructure.Automations;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Worker;

/// <summary>
/// The scheduled half of the automation engine: items whose date column has passed and that
/// are still not Done fire <see cref="Tuesday.Domain.Automation.AutomationTrigger.DatePassed"/> once a
/// day. Tenancy is resolved per tenant inside its own scope, because the worker has no request
/// to borrow one from and the global filters would otherwise hide every row.
/// </summary>
public sealed class ScheduledAutomationWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<ScheduledAutomationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue("Automation:ScanIntervalSeconds", 60);
        var interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 15, 3600));

        // Give the database and the schema a moment before the first sweep.
        await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled automation sweep failed; retrying next tick.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        List<Guid> tenantIds;

        using (var discovery = scopes.CreateScope())
        {
            var factory = discovery.ServiceProvider.GetRequiredService<IDbContextFactory<TuesdayDbContext>>();
            await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            tenantIds = await db.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(tenant => tenant.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var fired = 0;

        foreach (var tenantId in tenantIds)
        {
            using var scope = scopes.CreateScope();
            var provider = scope.ServiceProvider;

            var setter = provider.GetRequiredService<ITenantContextSetter>();
            var factory = provider.GetRequiredService<IDbContextFactory<TuesdayDbContext>>();
            var automations = provider.GetRequiredService<AutomationService>();

            await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var admin = await db.Memberships.IgnoreQueryFilters().AsNoTracking()
                .Where(membership => membership.TenantId == tenantId && membership.Role == TenantRole.Admin)
                .Select(membership => (Guid?)membership.UserId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (admin is null)
            {
                continue;
            }

            setter.Resolve(tenantId, admin.Value, TenantRole.Admin);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var midnight = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            var boards = await db.Boards.AsNoTracking()
                .Where(board => board.TenantId == tenantId)
                .Select(board => board.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var hasRule = await db.Automations.AsNoTracking()
                .AnyAsync(automation => boards.Contains(automation.BoardId)
                                        && automation.Enabled
                                        && automation.Trigger == Tuesday.Domain.Automation.AutomationTrigger.DatePassed,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!hasRule)
            {
                continue;
            }

            var overdue = await (from value in db.ItemColumnValues.AsNoTracking()
                                 join column in db.BoardColumns.AsNoTracking() on value.ColumnId equals column.Id
                                 where column.Type == ColumnType.Date
                                       && value.DateValue != null
                                       && value.DateValue < today
                                       && boards.Contains(column.BoardId)
                                 select new { value.ItemId, column.BoardId })
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var candidate in overdue)
            {
                // One run per item per day: a minute-by-minute sweep must not become spam.
                var alreadyRan = await db.AutomationRuns.AsNoTracking()
                    .AnyAsync(run => run.ItemId == candidate.ItemId && run.CreatedAt >= midnight, cancellationToken)
                    .ConfigureAwait(false);

                if (alreadyRan)
                {
                    continue;
                }

                fired += await automations
                    .FireAsync(
                        candidate.BoardId,
                        candidate.ItemId,
                        Tuesday.Domain.Automation.AutomationTrigger.DatePassed,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (fired > 0)
        {
            logger.LogInformation("Scheduled automations fired {Count} action(s).", fired);
        }

        return fired;
    }
}
