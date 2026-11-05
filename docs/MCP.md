# MCP server

`src/Tuesday.Mcp` exposes the platform to Claude Code (or any MCP client) as four tools over stdio.

It is deliberately a **client of the public REST API**, not a second application: it holds no
database connection, reuses the API token model (tenant, role, rate limit), and cannot do anything a
token owner could not do with `curl`.

## Protocol

- JSON-RPC 2.0, newline-delimited, over stdin/stdout.
- `initialize` → protocol version `2025-06-18`, `tools` capability, server name `tuesday`.
- `tools/list`, `tools/call`, `ping`; notifications (`notifications/initialized`) are accepted and
  never answered.
- Unknown methods return JSON-RPC `-32601`; a refused platform call returns a **tool result** with
  `isError: true`, so the model can read the HTTP status and try something else.

## Tools

| Tool | Maps to |
|---|---|
| `tuesday_list_boards` | `GET /api/v1/boards` |
| `tuesday_list_items` | `GET /api/v1/boards/{id}/items?take=&cursor=` |
| `tuesday_create_item` | `POST /api/v1/boards/{id}/items` |
| `tuesday_post_update` | `POST /api/v1/boards/{id}/items/{itemId}/updates` |

`POST /api/v1/boards/{boardId}/items` was added with this feature. Body:
`{ "name": "...", "groupId": "optional-guid" }`; omitting the group puts the item in the board's
first group. It requires edit rights and returns `201` with the new id.

## Configuration

Repository root `.mcp.json` points at the Release build:

```bash
dotnet build Tuesday.slnx -c Release
export TUESDAY_API_TOKEN=tue_...
```

| Variable | Default | Meaning |
|---|---|---|
| `TUESDAY_BASE_URL` | `http://127.0.0.1:5205` | base URL of a running app |
| `TUESDAY_API_TOKEN` | — (required) | API token; it must start with `tue_`, as issued by the app |

The server exits with code `2` and a stderr message when the token is absent, rather than starting a
tool surface that can only fail.

## Verified

- `initialize`, `tools/list` and `tools/call` driven over real stdio against a running instance.
- `tuesday_list_boards` returned the three seeded boards.
- `tuesday_create_item` created `MCP smoke item` in the first group; `tuesday_post_update` added one
  update to it; both confirmed in the database, then removed.
- A `401` (before the token prefix was fixed) arrived as a readable tool error and the session
  continued.

## Limits

- No cell-edit tool: setting a status or date still needs the UI or the API.
- No search, no sub-item or group administration, no file attachments.
- The server implements the protocol directly; no third-party MCP library is vendored or referenced.
