namespace Tuesday.Web.Api;

/// <summary>Body of <c>POST /api/v1/boards/{boardId}/webhooks</c>.</summary>
public sealed class WebhookRequest
{
    public string? Url { get; init; }
}
