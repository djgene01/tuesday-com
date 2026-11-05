namespace Tuesday.Infrastructure.Storage;

/// <summary>
/// Attachment storage root. A filesystem volume at launch; an S3-compatible endpoint
/// follows the same abstraction when needed.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Root { get; set; } = string.Empty;

    public long MaxUploadBytes { get; set; } = 25 * 1024 * 1024;
}
