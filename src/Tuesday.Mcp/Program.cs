using System.Text;
using System.Text.Json.Nodes;
using Tuesday.Mcp;

// MCP stdio server. stdout carries JSON-RPC only; diagnostics go to stderr.
var baseUrl = Environment.GetEnvironmentVariable("TUESDAY_BASE_URL") ?? "http://127.0.0.1:5205";
var token = Environment.GetEnvironmentVariable("TUESDAY_API_TOKEN");

if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine(
        "TUESDAY_API_TOKEN is not set. Create a token in the app (Settings → API tokens) and pass it "
        + "in the MCP server's env block.");
    return 2;
}

using var api = new HttpTuesdayApi(baseUrl, token);
var protocol = new McpProtocol(api);

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
var output = Console.OpenStandardOutput();

while (await input.ReadLineAsync() is { } line)
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    JsonNode? message;

    try
    {
        message = JsonNode.Parse(line);
    }
    catch (Exception exception)
    {
        await WriteAsync(output, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = null,
            ["error"] = new JsonObject { ["code"] = -32700, ["message"] = $"Parse error: {exception.Message}" }
        });

        continue;
    }

    var response = await protocol.HandleAsync(message, CancellationToken.None);

    // Notifications have no id and must not be answered.
    if (response is not null)
    {
        await WriteAsync(output, response);
    }
}

return 0;

static async Task WriteAsync(Stream output, JsonNode response)
{
    var bytes = Encoding.UTF8.GetBytes(response.ToJsonString() + "\n");
    await output.WriteAsync(bytes);
    await output.FlushAsync();
}
