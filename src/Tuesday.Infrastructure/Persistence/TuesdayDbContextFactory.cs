using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tuesday.Application.Tenancy;

namespace Tuesday.Infrastructure.Persistence;

/// <summary>
/// Design-time only: <c>dotnet ef</c> needs to construct the context without a request,
/// so it gets an unresolved tenant and a connection string from the environment.
/// </summary>
public sealed class TuesdayDbContextFactory : IDesignTimeDbContextFactory<TuesdayDbContext>
{
    public TuesdayDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TUESDAY_CONNECTION")
            ?? "Host=127.0.0.1;Port=5433;Database=tuesday;Username=tuesday;Password=tuesday_dev_password";

        var options = new DbContextOptionsBuilder<TuesdayDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TuesdayDbContext(options, new TenantContext());
    }
}
