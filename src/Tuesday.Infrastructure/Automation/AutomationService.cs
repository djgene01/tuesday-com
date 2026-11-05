using Microsoft.EntityFrameworkCore;
using Tuesday.Domain.Automation;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Automations;

public sealed record AutomationRow(
    Guid Id,
    string Name,
    AutomationTrigger Trigger,
    string Condition,
    AutomationAction Action,
    string ActionValue,
    bool Enabled);

public sealed record AutomationRunRow(
    Guid Id,
    Guid AutomationId,
    string Name,
    Guid? ItemId,
    AutomationRunStatus Status,
    string Message,
    DateTimeOffset CreatedAt);

/// <summary>
/// The automation engine: a trigger fires, the condition is evaluated against the item's
/// values, and exactly one action runs. Every attempt is recorded, including the skipped
/// ones, so "nothing happened" is answerable.
/// </summary>
public sealed class AutomationService(
    IDbContextFactory<TuesdayDbContext> contextFactory,
    Tuesday.Infrastructure.Values.ColumnValueWriter values,
    Tuesday.Infrastructure.Collaboration.NotificationService notifications)
{
    public async Task<IReadOnlyList<AutomationRow>> ListAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await dbContext.Automations.AsNoTracking()
            .Where(automation => automation.BoardId == boardId)
            .OrderBy(automation => automation.CreatedAt)
            .Select(automation => new AutomationRow(
                automation.Id,
                automation.Name,
                automation.Trigger,
                automation.Condition,
                automation.Action,
                automation.ActionValue,
                automation.Enabled))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(bool Succeeded, Guid Id, string? Error)> CreateAsync(
        Guid tenantId,
        Guid boardId,
        string name,
        AutomationTrigger trigger,
        string? condition,
        AutomationAction action,
        string? actionValue,
        Guid createdBy,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var count = await dbContext.Automations.CountAsync(automation => automation.BoardId == boardId, cancellationToken)
            .ConfigureAwait(false);

        if (count >= Automation.MaxPerBoard)
        {
            return (false, Guid.Empty, $"A board can have at most {Automation.MaxPerBoard} automations.");
        }

        var automation = Automation.Create(tenantId, boardId, name, trigger, condition, action, actionValue, createdBy);

        dbContext.Automations.Add(automation);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (true, automation.Id, null);
    }

    public async Task<bool> SetEnabledAsync(Guid automationId, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var automation = await dbContext.Automations
            .FirstOrDefaultAsync(candidate => candidate.Id == automationId, cancellationToken)
            .ConfigureAwait(false);

        if (automation is null)
        {
            return false;
        }

        automation.SetEnabled(enabled);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<AutomationRunRow>> RecentRunsAsync(
        Guid boardId,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var automations = dbContext.Automations.AsNoTracking().Where(automation => automation.BoardId == boardId);

        return await (from run in dbContext.AutomationRuns.AsNoTracking()
                      join automation in automations on run.AutomationId equals automation.Id
                      orderby run.CreatedAt descending
                      select new AutomationRunRow(
                          run.Id,
                          run.AutomationId,
                          automation.Name,
                          run.ItemId,
                          run.Status,
                          run.Message,
                          run.CreatedAt))
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs every enabled automation for this trigger on this board. A failing automation is
    /// recorded and does not stop the next one.
    /// </summary>
    public async Task<int> FireAsync(
        Guid boardId,
        Guid itemId,
        AutomationTrigger trigger,
        Guid? changedColumnId = null,
        string? changedValue = null,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var item = await dbContext.Items.AsNoTracking()
            .Where(candidate => candidate.Id == itemId && candidate.BoardId == boardId)
            .Select(candidate => new { candidate.Id, candidate.TenantId, candidate.Name })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return 0;
        }

        var automations = await dbContext.Automations.AsNoTracking()
            .Where(automation => automation.BoardId == boardId
                                 && automation.Enabled
                                 && automation.Trigger == trigger)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (automations.Count == 0)
        {
            return 0;
        }

        var columnValues = await LoadValuesAsync(dbContext, itemId, cancellationToken).ConfigureAwait(false);

        if (changedColumnId is { } columnId)
        {
            var changedKey = await dbContext.BoardColumns.AsNoTracking()
                .Where(column => column.Id == columnId && column.BoardId == boardId)
                .Select(column => column.Key)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (changedKey is not null)
            {
                // The condition sees the post-write value, which is what a person means by
                // "when the status becomes Done".
                columnValues[changedKey] = changedValue;
            }
        }

        var executed = 0;

        foreach (var automation in automations)
        {
            if (!automation.Matches(columnValues))
            {
                dbContext.AutomationRuns.Add(AutomationRun.Create(
                    item.TenantId, automation.Id, itemId, AutomationRunStatus.Skipped, "Condition did not match."));
                continue;
            }

            try
            {
                var message = await ExecuteAsync(dbContext, automation, item.TenantId, itemId, cancellationToken)
                    .ConfigureAwait(false);

                dbContext.AutomationRuns.Add(AutomationRun.Create(
                    item.TenantId, automation.Id, itemId, AutomationRunStatus.Succeeded, message));

                executed++;
            }
            catch (Exception exception)
            {
                dbContext.AutomationRuns.Add(AutomationRun.Create(
                    item.TenantId, automation.Id, itemId, AutomationRunStatus.Failed, exception.Message));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return executed;
    }

    private async Task<string> ExecuteAsync(
        TuesdayDbContext dbContext,
        Automation automation,
        Guid tenantId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        switch (automation.Action)
        {
            case AutomationAction.SetStatus:
            {
                // "status=Done": the column is named on the left, the value written on the right.
                var parts = automation.ActionValue.Split('=', 2);

                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                {
                    throw new InvalidOperationException("SetStatus expects 'columnKey=value'.");
                }

                var column = await FindColumnAsync(dbContext, automation.BoardId, parts[0].Trim(), cancellationToken)
                    .ConfigureAwait(false);

                if (column is null)
                {
                    throw new InvalidOperationException($"No column with key '{parts[0].Trim()}'.");
                }

                var write = await values.SetAsync(itemId, column.Id, parts[1].Trim(), cancellationToken)
                    .ConfigureAwait(false);

                if (!write.Succeeded)
                {
                    throw new InvalidOperationException(write.Error ?? "The status write was refused.");
                }

                return $"Set {column.Title} to '{parts[1].Trim()}'.";
            }

            case AutomationAction.SetDate:
            {
                var column = await FindColumnAsync(dbContext, automation.BoardId, automation.ActionValue, cancellationToken)
                    .ConfigureAwait(false);

                if (column is null)
                {
                    throw new InvalidOperationException($"No date column with key '{automation.ActionValue}'.");
                }

                var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
                var write = await values.SetAsync(itemId, column.Id, today, cancellationToken).ConfigureAwait(false);

                if (!write.Succeeded)
                {
                    throw new InvalidOperationException(write.Error ?? "The date write was refused.");
                }

                return $"Set {column.Title} to {today}.";
            }

            case AutomationAction.NotifyOwner:
            {
                // The owner is the person named in the item's people column, matched by name.
                var owner = await dbContext.ItemColumnValues.AsNoTracking()
                    .Where(value => value.ItemId == itemId && value.TextValue != null)
                    .Join(
                        dbContext.BoardColumns.AsNoTracking().Where(column => column.Type == Tuesday.Domain.Boards.ColumnType.People),
                        value => value.ColumnId,
                        column => column.Id,
                        (value, _) => value.TextValue)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                var userId = await notifications.FindByDisplayNameAsync(owner, cancellationToken).ConfigureAwait(false);

                if (userId is null)
                {
                    return "No owner to notify.";
                }

                await notifications.NotifyAsync(
                        tenantId,
                        userId.Value,
                        Tuesday.Domain.Collaboration.NotificationKind.Automation,
                        automation.Name,
                        automation.ActionValue,
                        $"/board/{automation.BoardId:D}/item/{itemId:D}",
                        automation.BoardId,
                        itemId,
                        cancellationToken)
                    .ConfigureAwait(false);

                return $"Notified {owner}.";
            }

            case AutomationAction.PostUpdate:
            {
                await notifications.FanOutUpdateAsync(
                        tenantId,
                        automation.BoardId,
                        itemId,
                        Guid.Empty,
                        automation.CreatedBy,
                        automation.ActionValue,
                        isReply: false,
                        threadRootId: null,
                        cancellationToken)
                    .ConfigureAwait(false);

                return "Delivered the configured message.";
            }

            default:
                throw new NotSupportedException($"Action {automation.Action} has no executor.");
        }
    }

    private static async Task<Dictionary<string, string?>> LoadValuesAsync(
        TuesdayDbContext dbContext,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.ItemColumnValues.AsNoTracking()
            .Where(value => value.ItemId == itemId)
            .Join(
                dbContext.BoardColumns.AsNoTracking(),
                value => value.ColumnId,
                column => column.Id,
                (value, column) => new { column.Key, value.TextValue, value.NumberValue, value.DateValue })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            values[row.Key] = row.TextValue
                ?? row.NumberValue?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ?? row.DateValue?.ToString("yyyy-MM-dd");
        }

        return values;
    }

    private static async Task<Tuesday.Domain.Boards.BoardColumn?> FindColumnAsync(
        TuesdayDbContext dbContext,
        Guid boardId,
        string keyOrTitle,
        CancellationToken cancellationToken)
    {
        var columns = await dbContext.BoardColumns.AsNoTracking()
            .Where(column => column.BoardId == boardId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return columns.FirstOrDefault(column =>
            string.Equals(column.Key, keyOrTitle, StringComparison.OrdinalIgnoreCase)
            || string.Equals(column.Title, keyOrTitle, StringComparison.OrdinalIgnoreCase));
    }
}
