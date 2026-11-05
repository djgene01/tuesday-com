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

    public async Task<(bool Succeeded, Guid Id, string? Error)> RegisterAsync(
        Guid tenantId,
        Guid boardId,
        string url,
        Guid createdBy,
        CancellationToken cancellationToken = default)
    {
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
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
                };

                request.Headers.Add("X-Tuesday-Signature", Webhook.Sign(webhook.Secret, body));
                request.Headers.Add("X-Tuesday-Event", eventType);

                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                webhook.Record((int)response.StatusCode);
            }
            catch (Exception exception)
            {
                webhook.Record(0);
                logger.LogWarning(exception, "Webhook dispatch to {Url} failed", webhook.Url);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
