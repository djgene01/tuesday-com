using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tuesday.Domain.Collaboration;
using Tuesday.Infrastructure.Persistence;
using Tuesday.Infrastructure.Storage;

namespace Tuesday.Infrastructure.Collaboration;

public sealed record AttachmentRow(
    Guid Id,
    string FileName,
    string ContentType,
    long ByteSize,
    DateTimeOffset CreatedAt);

public sealed record AttachmentDownload(string FullPath, string FileName, string ContentType);

public sealed record AttachmentResult(bool Succeeded, Guid Id, string? Error)
{
    public static AttachmentResult Ok(Guid id) => new(true, id, null);

    public static AttachmentResult Fail(string error) => new(false, Guid.Empty, error);
}

/// <summary>
/// Files live under <see cref="StorageOptions.Root"/> with a generated key, never under the
/// name the browser sent. The database holds metadata only, and downloads are served through
/// a signed link the Web layer issues after its own permission check.
/// </summary>
public sealed class AttachmentService(
    IDbContextFactory<TuesdayDbContext> contextFactory,
    IOptions<StorageOptions> storage)
{
    public async Task<AttachmentResult> SaveAsync(
        Guid tenantId,
        Guid boardId,
        Guid itemId,
        Guid uploadedBy,
        string fileName,
        string contentType,
        long byteSize,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var settings = storage.Value;

        if (string.IsNullOrWhiteSpace(settings.Root))
        {
            return AttachmentResult.Fail("Attachment storage is not configured.");
        }

        var limit = Math.Min(settings.MaxUploadBytes, Attachment.MaxBytes);

        if (byteSize <= 0 || byteSize > limit)
        {
            var label = limit >= 1024 * 1024 ? $"{limit / (1024 * 1024)} MB" : $"{limit} bytes";
            return AttachmentResult.Fail($"Attachments must be between 1 byte and {label}.");
        }

        var id = Guid.CreateVersion7();
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 12)
        {
            extension = ".bin";
        }

        // Two levels of GUID fan-out keep any single directory small.
        var storageKey = $"{id:D}"[..2] + "/" + $"{id:D}"[2..4] + "/" + id.ToString("D") + extension;
        var fullPath = Path.GetFullPath(Path.Combine(settings.Root, storageKey));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var target = File.Create(fullPath))
        {
            await content.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        var written = new FileInfo(fullPath).Length;

        try
        {
            var attachment = Attachment.Create(
                tenantId, boardId, itemId, uploadedBy, fileName, contentType, written, storageKey);

            await using var dbContext = await contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            dbContext.Attachments.Add(attachment);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return AttachmentResult.Ok(attachment.Id);
        }
        catch
        {
            // A row that never landed must not leave an orphan file behind.
            File.Delete(fullPath);
            throw;
        }
    }

    public async Task<IReadOnlyList<AttachmentRow>> ListAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await dbContext.Attachments.AsNoTracking()
            .Where(attachment => attachment.ItemId == itemId && attachment.DeletedAt == null)
            .OrderBy(attachment => attachment.CreatedAt)
            .Select(attachment => new AttachmentRow(
                attachment.Id,
                attachment.FileName,
                attachment.ContentType,
                attachment.ByteSize,
                attachment.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AttachmentDownload?> OpenAsync(
        Guid attachmentId,
        Guid? boardId,
        Guid? itemId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var attachment = await dbContext.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == attachmentId && row.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (attachment is null)
        {
            return null;
        }

        // A signed link names the board and item it was issued for; a link reused elsewhere fails.
        if ((boardId is not null && attachment.BoardId != boardId)
            || (itemId is not null && attachment.ItemId != itemId))
        {
            return null;
        }

        // Results.File resolves a relative path against a different base than the write used,
        // so every path handed to the web layer is absolute.
        var fullPath = Path.GetFullPath(Path.Combine(storage.Value.Root, attachment.StorageKey));

        return File.Exists(fullPath)
            ? new AttachmentDownload(fullPath, attachment.FileName, attachment.ContentType)
            : null;
    }

    public async Task<bool> DeleteAsync(
        Guid attachmentId,
        Guid actorId,
        bool canModerate,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var attachment = await dbContext.Attachments
            .FirstOrDefaultAsync(row => row.Id == attachmentId, cancellationToken)
            .ConfigureAwait(false);

        if (attachment is null || (attachment.UploadedBy != actorId && !canModerate))
        {
            return false;
        }

        attachment.Delete();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
