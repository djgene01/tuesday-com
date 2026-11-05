namespace Tuesday.Web.Api;

/// <summary>Body of <c>POST /api/v1/boards/{boardId}/items/{itemId}/updates</c>.</summary>
public sealed class UpdateRequest
{
    public string? Body { get; init; }
}
