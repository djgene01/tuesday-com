---
name: tuesday-mcp
description: Use when asked to read or change work items in a Tuesday instance - boards, items, updates - through the bundled MCP server instead of the browser.
---

# Tuesday work OS over MCP

The platform ships a Model Context Protocol server (`src/Tuesday.Mcp`) that speaks JSON-RPC over
stdio, exactly like the MCP servers Claude Code already loads. It talks to the public REST API with
an API token, never to the database.

## Connect

The repository root has `.mcp.json`:

```json
{
  "mcpServers": {
    "tuesday": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["src/Tuesday.Mcp/bin/Release/net10.0/Tuesday.Mcp.dll"],
      "env": {
        "TUESDAY_BASE_URL": "http://127.0.0.1:5205",
        "TUESDAY_API_TOKEN": "${TUESDAY_API_TOKEN}"
      }
    }
  }
}
```

```bash
dotnet build Tuesday.slnx -c Release            # produces the DLL the config points at
export TUESDAY_API_TOKEN=tue_...                # Settings -> API tokens in the app
claude                                          # /mcp should list "tuesday" and four tools
```

On Windows use `127.0.0.1`, not `localhost`: the app and the MCP client may resolve `localhost` to
different stacks, and the Docker setup only publishes IPv4.

## Tools

| Tool | Arguments | Use it for |
|---|---|---|
| `tuesday_list_boards` | — | the entry point; every other call needs a board id from here |
| `tuesday_list_items` | `board_id`, `take` (≤500), `cursor` | reading items and their cell values |
| `tuesday_create_item` | `board_id`, `name`, optional `group_id` | adding work; omit the group to use the first one |
| `tuesday_post_update` | `board_id`, `item_id`, `body` | commenting, which also notifies followers |

Every tool returns MCP `content[0].text` containing JSON. Check `isError` before parsing: a refusal
from the platform arrives as `isError: true` with the HTTP status in the text, and the server stays
alive for the next call.

## Working rules

- **Resolve ids first.** Call `tuesday_list_boards`, pick the board by name, then
  `tuesday_list_items`. Never invent a GUID; ids are version-7 and look nothing alike.
- **Page, don't guess.** `tuesday_list_items` returns `nextCursor` and `hasMore`. Follow the cursor
  until `hasMore` is false before concluding an item is missing. `take` defaults to 50 and is capped
  at 500.
- **Cell values are keyed by column id**, not title. Map them through the board's columns before
  reporting names to the user, or quote the column id explicitly.
- **Writes are attributable.** `tuesday_create_item` and `tuesday_post_update` act as the token owner.
  Say whose token you are using if the user asks who made the change.
- **There is no cell-edit tool.** If the user asks to set a status or a date, say so plainly: the MCP
  surface is create + comment + read today. Do not pretend a write happened.
- **Errors are information.** `401` means the token is missing, revoked, or does not start with
  `tue_`; `403` means the token's owner cannot see or write that board; `404` usually means the board
  id came from another instance.

## Smoke test without Claude

```bash
printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' \
  '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"tuesday_list_boards","arguments":{}}}' \
  | TUESDAY_API_TOKEN="$TUESDAY_API_TOKEN" \
    dotnet src/Tuesday.Mcp/bin/Release/net10.0/Tuesday.Mcp.dll
```

Three JSON lines come back, one per request. stdout carries protocol only; diagnostics go to stderr.
