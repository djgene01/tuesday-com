using System.Data.Common;

namespace Tuesday.Infrastructure.Persistence;

/// <summary>
/// Opens raw connections for read paths and health probes that must not go through the
/// EF Core pipeline. The return type is the ADO.NET abstraction on purpose: switching the
/// EF provider must not ripple into callers (that is exactly what the MySQL to PostgreSQL
/// move cost last time).
/// </summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default);
}
