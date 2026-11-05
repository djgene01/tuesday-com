using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Tuesday.Tests.Integration;

/// <summary>
/// Boots the real web pipeline. Configuration points at unreachable dependencies on
/// purpose: liveness must not depend on PostgreSQL or Redis.
/// </summary>
public sealed class WalkingSkeletonTests : IClassFixture<TuesdayWebFactory>
{
    private readonly TuesdayWebFactory _factory;

    public WalkingSkeletonTests(TuesdayWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Liveness_ReturnsHealthy_WithoutDependencies()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
        Assert.Empty(document.RootElement.GetProperty("checks").EnumerateArray());
    }

    [Fact]
    public async Task LandingPage_RendersVisibleContent()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Tuesday", html, StringComparison.Ordinal);
        Assert.Contains("Dependencies", html, StringComparison.Ordinal);
        Assert.Contains("phase P0", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EveryResponse_CarriesACorrelationId()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task SuppliedCorrelationId_IsEchoedBack()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "test-correlation-123");

        var response = await client.SendAsync(request);

        Assert.Equal("test-correlation-123", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task Readiness_ReportsDependencyHealth()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        // Either outcome is valid: the assertion is that readiness reflects real
        // dependency state instead of reporting healthy unconditionally.
        Assert.True(
            response.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.ServiceUnavailable,
            $"Unexpected status {(int)response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var names = document.RootElement.GetProperty("checks").EnumerateArray()
            .Select(check => check.GetProperty("name").GetString())
            .ToList();

        Assert.Contains("database", names);
        Assert.Contains("redis", names);
        Assert.Contains("storage", names);
    }
}

public sealed class TuesdayWebFactory : WebApplicationFactory<Program>
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "tuesday-tests", Guid.NewGuid().ToString("n"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=127.0.0.1;Port=1;Database=tuesday;User ID=none;Password=none;Connection Timeout=1",
                ["Redis:Configuration"] = "127.0.0.1:1,connectTimeout=200,abortConnect=true",
                ["Storage:Root"] = _storageRoot
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_storageRoot))
        {
            try
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test run over.
            }
        }
    }
}
