using System.Text.Json.Nodes;
using Tuesday.Mcp;

namespace Tuesday.Tests.Unit;

public sealed class McpProtocolTests
{
    private sealed class FakeApi : ITuesdayApi
    {
        public List<string> Calls { get; } = [];

        public Task<JsonNode> ListBoardsAsync(CancellationToken cancellationToken)
        {
            Calls.Add("boards");
            return Task.FromResult<JsonNode>(new JsonArray(new JsonObject { ["name"] = "Campaign calendar" }));
        }

        public Task<JsonNode> ListItemsAsync(string boardId, int take, string? cursor, CancellationToken cancellationToken)
        {
            Calls.Add($"items:{boardId}:{take}:{cursor}");
            return Task.FromResult<JsonNode>(new JsonObject { ["items"] = new JsonArray(), ["hasMore"] = false });
        }

        public Task<JsonNode> CreateItemAsync(string boardId, string name, string? groupId, CancellationToken cancellationToken)
        {
            Calls.Add($"create:{boardId}:{name}:{groupId}");
            return Task.FromResult<JsonNode>(new JsonObject { ["id"] = "new-item" });
        }

        public Task<JsonNode> PostUpdateAsync(string boardId, string itemId, string body, CancellationToken cancellationToken)
        {
            Calls.Add($"update:{boardId}:{itemId}:{body}");
            return Task.FromResult<JsonNode>(new JsonObject { ["id"] = "update-1" });
        }
    }

    private static JsonObject Request(string method, JsonNode? parameters = null, int id = 1) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
        ["params"] = parameters
    };

    [Fact]
    public async Task Initialize_announces_the_tools_capability_and_protocol_version()
    {
        var protocol = new McpProtocol(new FakeApi());

        var response = await protocol.HandleAsync(Request("initialize"), CancellationToken.None);
        var result = response!["result"]!;

        Assert.Equal("2.0", response["jsonrpc"]!.GetValue<string>());
        Assert.Equal(McpProtocol.ProtocolVersion, result["protocolVersion"]!.GetValue<string>());
        Assert.NotNull(result["capabilities"]!["tools"]);
        Assert.Equal("tuesday", result["serverInfo"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Notifications_are_not_answered()
    {
        var protocol = new McpProtocol(new FakeApi());
        var note = Request("notifications/initialized");
        note.Remove("id");

        Assert.Null(await protocol.HandleAsync(note, CancellationToken.None));
    }

    [Fact]
    public async Task Tools_list_describes_every_tool_with_an_object_schema()
    {
        var protocol = new McpProtocol(new FakeApi());

        var response = await protocol.HandleAsync(Request("tools/list"), CancellationToken.None);
        var tools = response!["result"]!["tools"]!.AsArray();

        Assert.Equal(4, tools.Count);
        Assert.All(tools, tool => Assert.Equal("object", tool!["inputSchema"]!["type"]!.GetValue<string>()));
        Assert.Contains(tools, tool => tool!["name"]!.GetValue<string>() == "tuesday_list_items");
    }

    [Fact]
    public async Task List_items_passes_paging_through_and_wraps_the_result_as_text()
    {
        var api = new FakeApi();
        var protocol = new McpProtocol(api);

        var response = await protocol.HandleAsync(
            Request("tools/call", new JsonObject
            {
                ["name"] = "tuesday_list_items",
                ["arguments"] = new JsonObject { ["board_id"] = "board-1", ["take"] = 10, ["cursor"] = "abc" }
            }),
            CancellationToken.None);

        var result = response!["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        Assert.Equal("text", result["content"]![0]!["type"]!.GetValue<string>());
        Assert.Equal(["items:board-1:10:abc"], api.Calls);
    }

    [Fact]
    public async Task A_missing_argument_becomes_a_tool_error_not_a_crash()
    {
        var protocol = new McpProtocol(new FakeApi());

        var response = await protocol.HandleAsync(
            Request("tools/call", new JsonObject
            {
                ["name"] = "tuesday_post_update",
                ["arguments"] = new JsonObject { ["board_id"] = "b", ["item_id"] = "i" }
            }),
            CancellationToken.None);

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("'body' is required", result["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_unknown_method_is_a_json_rpc_error_not_a_tool_error()
    {
        var protocol = new McpProtocol(new FakeApi());

        var response = await protocol.HandleAsync(Request("resources/list"), CancellationToken.None);

        Assert.Null(response!["result"]);
        Assert.Equal(-32601, response["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_platform_refusal_is_reported_to_the_model_without_killing_the_server()
    {
        var protocol = new McpProtocol(new RefusingApi());

        var response = await protocol.HandleAsync(
            Request("tools/call", new JsonObject
            {
                ["name"] = "tuesday_create_item",
                ["arguments"] = new JsonObject { ["board_id"] = "b", ["name"] = "x" }
            }),
            CancellationToken.None);

        var result = response!["result"]!;
        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("403 Forbidden", result["content"]![0]!["text"]!.GetValue<string>());
    }

    private sealed class RefusingApi : ITuesdayApi
    {
        public Task<JsonNode> ListBoardsAsync(CancellationToken cancellationToken) => throw Refused();

        public Task<JsonNode> ListItemsAsync(string boardId, int take, string? cursor, CancellationToken cancellationToken) =>
            throw Refused();

        public Task<JsonNode> CreateItemAsync(string boardId, string name, string? groupId, CancellationToken cancellationToken) =>
            throw Refused();

        public Task<JsonNode> PostUpdateAsync(string boardId, string itemId, string body, CancellationToken cancellationToken) =>
            throw Refused();

        private static TuesdayApiException Refused() =>
            new("403 Forbidden: the token cannot write here", System.Net.HttpStatusCode.Forbidden);
    }
}
