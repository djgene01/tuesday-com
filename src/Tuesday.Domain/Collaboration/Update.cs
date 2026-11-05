using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Collaboration;

/// <summary>
/// One update on an item: the conversation itself. The body is stored twice on purpose —
/// sanitised HTML for display and plain text for search — and neither is ever trusted
/// as input to the other.
/// </summary>
public sealed class Update : ITenantOwned
{
    public const int MaxBodyLength = 20_000;

    private Update()
    {
    }

    private Update(
        Guid tenantId,
        Guid boardId,
        Guid itemId,
        Guid authorId,
        string bodyHtml,
        string bodyText,
        Guid? parentId)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        BoardId = boardId;
        ItemId = itemId;
        AuthorId = authorId;
        BodyHtml = bodyHtml;
        BodyText = bodyText;
        ParentId = parentId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid BoardId { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid AuthorId { get; private set; }

    /// <summary>Set on a reply; null on a top-level update.</summary>
    public Guid? ParentId { get; private set; }

    public string BodyHtml { get; private set; } = string.Empty;

    public string BodyText { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? EditedAt { get; private set; }

    /// <summary>Soft delete keeps the thread readable instead of leaving holes.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsReply => ParentId is not null;

    public static Update Create(
        Guid tenantId,
        Guid boardId,
        Guid itemId,
        Guid authorId,
        string bodyHtml,
        string bodyText,
        Guid? parentId = null)
    {
        if (tenantId == Guid.Empty || boardId == Guid.Empty || itemId == Guid.Empty)
        {
            throw new ArgumentException("An update needs a tenant, a board and an item.");
        }

        if (authorId == Guid.Empty)
        {
            throw new ArgumentException("An update needs an author.", nameof(authorId));
        }

        if (string.IsNullOrWhiteSpace(bodyText))
        {
            throw new ArgumentException("An update needs a body.", nameof(bodyText));
        }

        if (bodyHtml.Length > MaxBodyLength)
        {
            throw new ArgumentException(
                $"An update body cannot exceed {MaxBodyLength} characters.",
                nameof(bodyHtml));
        }

        return new Update(tenantId, boardId, itemId, authorId, bodyHtml, bodyText.Trim(), parentId);
    }

    public void Edit(string bodyHtml, string bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            throw new ArgumentException("An update needs a body.", nameof(bodyText));
        }

        if (bodyHtml.Length > MaxBodyLength)
        {
            throw new ArgumentException(
                $"An update body cannot exceed {MaxBodyLength} characters.",
                nameof(bodyHtml));
        }

        BodyHtml = bodyHtml;
        BodyText = bodyText.Trim();
        EditedAt = DateTimeOffset.UtcNow;
    }

    public void Delete()
    {
        DeletedAt = DateTimeOffset.UtcNow;
    }

    public void Restore()
    {
        DeletedAt = null;
    }

    /// <summary>Replies quote a top-level update; a reply to a reply is flattened.</summary>
    public Guid ThreadId => ParentId ?? Id;
}
