using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tuesday.Domain.Automation;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Automations;

public sealed record WebhookRow(Guid Id, string Url, bool Enabled, DateTimeOffset CreatedAt, DateTimeOffset? LastCalledAt, int? LastStatus);

/// <summary>
/// Sends signed webhook calls. The handshake is the standard challenge echo: the receiver
/// posts back <c>challenge</c> and we only keep the webhook if the echo matches, which proves
/// the URL is a real endpoint someone controls rather than a victim's address.
/// </summary>
public sealed class WebhookService(
    IDbContextFactory<TuesdayDbContext> contextFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<WebhookService> logger)
{
    public async Task<IReadOnlyList<WebhookRow>> ListAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await dbContext.Webhooks.AsNoTracking()
            .Where(webhook => webhook.BoardId == boardId)
            .OrderBy(webhook => webhook.CreatedAt)
            .Select(webhook => new WebhookRow(
                webhook.Id, webhook.Url, webhook.Enabled, webhook.CreatedAt, webhook.LastCalledAt, webhook.LastStatus))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Attempts per delivery before the failure counts towards the disable threshold.</summary>
    public const int MaxDeliveryAttempts = 5;

    public async Task<(bool Succeeded, Guid Id, string? Error)> RegisterAsync(
        Guid tenantId,
        Guid boardId,
        string url,
        Guid createdBy,
        CancellationToken cancellationToken = default)
    {
        // Checked before anything is stored, and again before every delivery below.
        if (await OutboundUrlPolicy.ValidateAsync(url, cancellationToken).ConfigureAwait(false) is { } blocked)
        {
            return (false, Guid.Empty, blocked);
        }

        Webhook webhook;

        try
        {
            webhook = Webhook.Create(tenantId, boardId, url, createdBy);
        }
        catch (ArgumentException exception)
        {
            return (false, Guid.Empty, exception.Message);
        }

        var verified = await VerifyHandshakeAsync(webhook, cancellationToken).ConfigureAwait(false);

        if (!verified)
        {
            return (false, Guid.Empty, "The endpoint did not echo the challenge; the webhook was not saved.");
        }

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        dbContext.Webhooks.Add(webhook);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (true, webhook.Id, null);
    }

    private static bool IsRetryable(int status) => status == 0 || status == 408 || status == 429 || status >= 500;

    private async Task<bool> VerifyHandshakeAsync(Webhook webhook, CancellationToken cancellationToken)
    {
        var challenge = Guid.NewGuid().ToString("n");

        try
        {
            var client = httpClientFactory.CreateClient("webhooks");
            using var response = await client
                .PostAsJsonAsync(webhook.Url, new { challenge, eventType = "webhook.verify" }, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);

            return body.TryGetProperty("challenge", out var echo)
                   && string.Equals(echo.GetString(), challenge, StringComparison.Ordinal);
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "Webhook handshake to {Url} failed", webhook.Url);
            return false;
        }
    }

    /// <summary>Re-enables a webhook that auto-disabled itself after repeated failures.</summary>
    public async Task<bool> SetEnabledAsync(
        Guid webhookId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var webhook = await dbContext.Webhooks
            .FirstOrDefaultAsync(row => row.Id == webhookId, cancellationToken)
            .ConfigureAwait(false);

        if (webhook is null)
        {
            return false;
        }

        webhook.SetEnabled(enabled);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>Fire-and-record: a webhook failure never rolls back the write that triggered it.</summary>
    public async Task DispatchAsync(
        Guid boardId,
        string eventType,
        object payload,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var webhooks = await dbContext.Webhooks
            .Where(webhook => webhook.BoardId == boardId && webhook.Enabled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (webhooks.Count == 0)
        {
            return;
        }

        var body = JsonSerializer.Serialize(new { eventType, boardId, payload, sentAt = DateTimeOffset.UtcNow });
        var client = httpClientFactory.CreateClient("webhooks");

        foreach (var webhook in webhooks)
        {
            // Re-checked here, not just at registration: DNS can be re-pointed afterwards.
            if (await OutboundUrlPolicy.ValidateAsync(webhook.Url, cancellationToken).ConfigureAwait(false) is { } blocked)
            {
                logger.LogWarning("Webhook {Id} blocked by URL policy: {Reason}", webhook.Id, blocked);
                webhook.RecordFailure(0);
                continue;
            }

            var status = 0;
            var delivered = false;

            for (var attempt = 1; attempt <= MaxDeliveryAttempts && !delivered; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
                    {
                        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
                    };

                    request.Headers.Add("X-Tuesday-Signature", Webhook.Sign(webhook.Secret, body));
                    request.Headers.Add("X-Tuesday-Event", eventType);
                    request.Headers.Add("X-Tuesday-Attempt", attempt.ToString(System.Globalization.CultureInfo.InvariantCulture));

                    using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    status = (int)response.StatusCode;

                    if (response.IsSuccessStatusCode)
                    {
                        webhook.RecordSuccess(status);
                        delivered = true;
                        break;
                    }

                    if (!IsRetryable(status))
                    {
                        break;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    status = 0;
                    logger.LogInformation(exception, "Webhook attempt {Attempt} to {Url} failed", attempt, webhook.Url);
                }

                if (attempt < MaxDeliveryAttempts)
                {
                    // Short, bounded backoff: a dispatch runs inside the request that triggered
                    // it, so it must not hold that request open for long.
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken).ConfigureAwait(false);
                }
            }

            if (delivered)
            {
                continue;
            }

            if (webhook.RecordFailure(status))
            {
                logger.LogWarning(
                    "Webhook {Id} disabled after {Failures} consecutive failed deliveries",
                    webhook.Id,
                    Webhook.DisableAfterFailures);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
