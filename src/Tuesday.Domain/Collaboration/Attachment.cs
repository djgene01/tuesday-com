using Tuesday.Domain.Tenancy;

namespace Tuesday.Domain.Collaboration;

/// <summary>
/// Metadata for a file held in object storage. The bytes are never in the database and the
/// key is never handed to a browser directly: downloads go through a signed, expiring URL.
/// </summary>
public sealed class Attachment : ITenantOwned
{
    public const long MaxBytes = 25L * 1024 * 1024;

    private Attachment()
    {
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid BoardId { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid UploadedBy { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = "application/octet-stream";

    public long ByteSize { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public static Attachment Create(
        Guid tenantId,
        Guid boardId,
        Guid itemId,
        Guid uploadedBy,
        string fileName,
        string contentType,
        long byteSize,
        string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        if (byteSize <= 0 || byteSize > MaxBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(byteSize), "Attachments must be between 1 byte and 25 MB.");
        }

        return new Attachment
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            BoardId = boardId,
            ItemId = itemId,
            UploadedBy = uploadedBy,
            FileName = SafeName(fileName),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            ByteSize = byteSize,
            StorageKey = storageKey,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>Strips any path a browser may have sent, so a name cannot escape its folder.</summary>
    private static string SafeName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(name) || name is "." or ".." ? "file" : name;
    }

    public void Delete() => DeletedAt ??= DateTimeOffset.UtcNow;
}
