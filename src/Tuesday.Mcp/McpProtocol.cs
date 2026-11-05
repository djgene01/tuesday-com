using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tuesday.Mcp;

/// <summary>
/// Model Context Protocol over stdio, JSON-RPC 2.0, newline-delimited.
/// One request in, at most one response out; notifications get no reply.
/// </summary>
public sealed class McpProtocol(ITuesdayApi api)
{
    public const string ProtocolVersion = "2025-06-18";

    private static readonly JsonObject EmptySchema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
        ["additionalProperties"] = false
    };

    /// <summary>Returns null for notifications, which must not be answered.</summary>
    public async Task<JsonNode?> HandleAsync(JsonNode? message, CancellationToken cancellationToken)
    {
        if (message is not JsonObject request)
        {
            return Error(null, -32600, "Invalid Request");
        }

        var id = request["id"]?.DeepClone();
        var method = request["method"]?.GetValue<string>();
        var hasId = request.ContainsKey("id");

        if (string.IsNullOrEmpty(method))
        {
            return hasId ? Error(id, -32600, "Invalid Request") : null;
        }

        if (!hasId)
        {
            // notifications/initialized, notifications/cancelled, and anything else one-way.
            return null;
        }

        try
        {
            return method switch
            {
                "initialize" => Result(id, Initialize()),
                "ping" => Result(id, new JsonObject()),
                "tools/list" => Result(id, new JsonObject { ["tools"] = Tools() }),
                "tools/call" => Result(id, await CallToolAsync(request["params"] as JsonObject, cancellationToken)),
                _ => Error(id, -32601, $"Method not found: {method}")
            };
        }
        catch (TuesdayApiException exception)
        {
            return Result(id, ToolError($"Tuesday refused the call: {exception.Message}"));
        }
        catch (Exception exception)
        {
            return Result(id, ToolError(exception.Message));
        }
    }

    private static JsonObject Initialize() => new()
    {
        ["protocolVersion"] = ProtocolVersion,
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
        ["serverInfo"] = new JsonObject
        {
            ["name"] = "tuesday",
            ["title"] = "Tuesday work OS",
            ["version"] = typeof(McpProtocol).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"
        }
    };

    private static JsonArray Tools() =>
    [
        Tool(
            "tuesday_list_boards",
            "List the boards the authenticated token can see. Start here: every other tool needs a board id.",
            EmptySchema),
        Tool(
            "tuesday_list_items",
            "Read a page of items from one board, with each cell's stored text keyed by column id. "
            + "Use nextCursor with the same board to continue; stop when hasMore is false.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["board_id"] = new JsonObject { ["type"] = "string", ["description"] = "Board GUID." },
                    ["take"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 1,
                        ["maximum"] = 500,
                        ["description"] = "Page size, default 50."
                    },
                    ["cursor"] = new JsonObject { ["type"] = "string", ["description"] = "nextCursor from the previous page." }
                },
                ["required"] = new JsonArray("board_id"),
                ["additionalProperties"] = false
            }),
        Tool(
            "tuesday_create_item",
            "Add an item to a board. Omit group_id to use the board's first group.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["board_id"] = new JsonObject { ["type"] = "string" },
                    ["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 500 },
                    ["group_id"] = new JsonObject { ["type"] = "string", ["description"] = "Optional group GUID." }
                },
                ["required"] = new JsonArray("board_id", "name"),
                ["additionalProperties"] = false
            }),
        Tool(
            "tuesday_post_update",
            "Post an update (a comment) on an item. This is the write path that also notifies followers.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["board_id"] = new JsonObject { ["type"] = "string" },
                    ["item_id"] = new JsonObject { ["type"] = "string" },
                    ["body"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 4000 }
                },
                ["required"] = new JsonArray("board_id", "item_id", "body"),
                ["additionalProperties"] = false
            })
    ];

    private async Task<JsonObject> CallToolAsync(JsonObject? parameters, CancellationToken cancellationToken)
    {
        var name = parameters?["name"]?.GetValue<string>();
        var arguments = parameters?["arguments"] as JsonObject ?? new JsonObject();

        if (string.IsNullOrEmpty(name))
        {
            return ToolError("A tools/call needs a tool name.");
        }

        var result = name switch
        {
            "tuesday_list_boards" => await api.ListBoardsAsync(cancellationToken),
            "tuesday_list_items" => await api.ListItemsAsync(
                Required(arguments, "board_id"),
                arguments["take"]?.GetValue<int>() ?? 50,
                arguments["cursor"]?.GetValue<string>(),
                cancellationToken),
            "tuesday_create_item" => await api.CreateItemAsync(
                Required(arguments, "board_id"),
                Required(arguments, "name"),
                arguments["group_id"]?.GetValue<string>(),
                cancellationToken),
            "tuesday_post_update" => await api.PostUpdateAsync(
                Required(arguments, "board_id"),
                Required(arguments, "item_id"),
                Required(arguments, "body"),
                cancellationToken),
            _ => null
        };

        if (result is null)
        {
            return ToolError($"Unknown tool: {name}");
        }

        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = result.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            }),
            ["isError"] = false
        };
    }

    private static string Required(JsonObject arguments, string property)
    {
        var value = arguments[property]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"'{property}' is required.");
        }

        return value;
    }

    private static JsonObject Tool(string name, string description, JsonObject schema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = schema
    };

    private static JsonObject ToolError(string message) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = message }),
        ["isError"] = true
    };

    private static JsonObject Result(JsonNode? id, JsonNode result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = result
    };

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
    };
}
