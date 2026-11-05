using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tuesday.Infrastructure;
using Tuesday.Infrastructure.Persistence;

namespace Tuesday.Tests.Unit;

public sealed class InfrastructureRegistrationTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void AddInfrastructure_Throws_WhenConnectionStringIsMissing()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Redis:Configuration"] = "localhost:6379"
        });

        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructure(configuration));

        Assert.Contains("ConnectionStrings:Default", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddInfrastructure_Throws_WhenRedisIsMissing()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Server=localhost;Database=tuesday;User ID=x;Password=y"
        });

        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructure(configuration));

        Assert.Contains("Redis:Configuration", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddInfrastructure_RegistersConnectionFactory_WhenConfigurationIsComplete()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Server=localhost;Database=tuesday;User ID=x;Password=y",
            ["Redis:Configuration"] = "localhost:6379",
            ["Storage:Root"] = "data/files"
        });

        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IDbConnectionFactory));
    }
}
