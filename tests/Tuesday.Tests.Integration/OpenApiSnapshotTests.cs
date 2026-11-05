namespace Tuesday.Tests.Integration;

/// <summary>
/// Pins the public v1 contract. A field renamed or removed in a handler changes the served
/// document and fails this test, which is the point: v1 clients must not break silently.
/// Regenerate the snapshot deliberately when the change is intended.
/// </summary>
public sealed class OpenApiSnapshotTests : IClassFixture<TuesdayWebFactory>
{
    private readonly TuesdayWebFactory _factory;

    public OpenApiSnapshotTests(TuesdayWebFactory factory) => _factory = factory;

    [Fact]
    public async Task The_served_contract_matches_the_committed_snapshot()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var live = Normalise(await response.Content.ReadAsStringAsync());
        var expected = Normalise(await File.ReadAllTextAsync(SnapshotPath()));

        Assert.Equal(expected, live);
    }

    /// <summary>
    /// The served document carries a <c>servers</c> entry derived from the request host, which
    /// differs between a local run and the test host. Everything else must match exactly.
    /// </summary>
    private static string Normalise(string json)
    {
        var document = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        document.Remove("servers");

        return document.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string SnapshotPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "openapi", "v1.json");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("docs/openapi/v1.json was not found above the test output directory.");
    }
}
