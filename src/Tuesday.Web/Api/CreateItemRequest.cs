namespace Tuesday.Web.Api;

/// <summary>Body for POST /api/v1/boards/{boardId}/items.</summary>
public sealed record CreateItemRequest(string Name, Guid? GroupId = null);
