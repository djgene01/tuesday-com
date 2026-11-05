using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Infrastructure.Security;

/// <summary>
/// One Data Protection key ring element, as XML, stored where every replica can read it.
/// Without this each container generates its own ring at start-up, so a session created by
/// one replica is undecryptable by another and every roll signs users out.
/// </summary>
public sealed class DataProtectionKey
{
    public Guid Id { get; set; }

    public string FriendlyName { get; set; } = string.Empty;

    public string Xml { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Replaces the file-based key ring. Resolved as a singleton, so it opens a scope per call
/// instead of holding a DbContext for the life of the process.
/// </summary>
public sealed class DatabaseXmlRepository(IServiceScopeFactory scopeFactory) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TuesdayDbContext>();

        return database.DataProtectionKeys
            .AsNoTracking()
            .OrderBy(key => key.CreatedAt)
            .Select(key => XElement.Parse(key.Xml))
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TuesdayDbContext>();

        database.DataProtectionKeys.Add(new DataProtectionKey
        {
            Id = Guid.NewGuid(),
            FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? Guid.NewGuid().ToString("n") : friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
            CreatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            database.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Two replicas activating a key at the same moment race on the friendly name. The
            // loser's key adds nothing: the winner's key is already stored and both processes
            // re-read the ring on the next activation.
        }
    }
}

public static class DataProtectionExtensions
{
    /// <summary>
    /// Persists the key ring in the application database so every replica shares one ring.
    /// </summary>
    public static IDataProtectionBuilder PersistKeysToDatabase(this IDataProtectionBuilder builder)
    {
        builder.Services.AddSingleton<IXmlRepository, DatabaseXmlRepository>();
        builder.Services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>()
            .Configure<IXmlRepository>((options, repository) => options.XmlRepository = repository);

        return builder;
    }
}
