using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tuesday.Mcp;

/// <summary>What the MCP server needs from the platform. Kept narrow so the protocol is testable.</summary>
public interface ITuesdayApi
{
    Task<JsonNode> ListBoardsAsync(CancellationToken cancellationToken);

    Task<JsonNode> ListItemsAsync(string boardId, int take, string? cursor, CancellationToken cancellationToken);

    Task<JsonNode> CreateItemAsync(string boardId, string name, string? groupId, CancellationToken cancellationToken);

    Task<JsonNode> PostUpdateAsync(string boardId, string itemId, string body, CancellationToken cancellationToken);
}

/// <summary>Talks to the public REST API, never to the database.</summary>
public sealed class HttpTuesdayApi : ITuesdayApi, IDisposable
{
    private readonly HttpClient client;

    public HttpTuesdayApi(string baseUrl, string token)
    {
        client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(60)
        };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<JsonNode> ListBoardsAsync(CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, "api/v1/boards", null, cancellationToken);

    public Task<JsonNode> ListItemsAsync(string boardId, int take, string? cursor, CancellationToken cancellationToken)
    {
        var url = "api/v1/boards/" + Uri.EscapeDataString(boardId)
            + "/items?take=" + Math.Clamp(take, 1, 500).ToString(CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            url += "&cursor=" + Uri.EscapeDataString(cursor);
        }

        return SendAsync(HttpMethod.Get, url, null, cancellationToken);
    }

    public Task<JsonNode> CreateItemAsync(string boardId, string name, string? groupId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject { ["name"] = name };

        if (!string.IsNullOrWhiteSpace(groupId))
        {
            payload["groupId"] = groupId;
        }

        return SendAsync(
            HttpMethod.Post,
            $"api/v1/boards/{Uri.EscapeDataString(boardId)}/items",
            payload,
            cancellationToken);
    }

    public Task<JsonNode> PostUpdateAsync(string boardId, string itemId, string body, CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Post,
            $"api/v1/boards/{Uri.EscapeDataString(boardId)}/items/{Uri.EscapeDataString(itemId)}/updates",
            new JsonObject { ["body"] = body },
            cancellationToken);

    private async Task<JsonNode> SendAsync(
        HttpMethod method,
        string url,
        JsonNode? payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = string.IsNullOrWhiteSpace(text) ? response.ReasonPhrase : Shorten(text);

            throw new TuesdayApiException(
                $"{(int)response.StatusCode} {response.StatusCode}: {detail}",
                response.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(text) ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject { ["raw"] = text };
        }
    }

    private static string Shorten(string text) =>
        text.Length <= 400 ? text : text[..400] + "…";

    public void Dispose() => client.Dispose();
}

public sealed class TuesdayApiException(string message, HttpStatusCode status) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}
