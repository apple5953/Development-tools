# BEAI Reality Bridge V0.1 Testing

## Prerequisites

- Autodesk Revit 2024 is installed.
- `DevelopmentTools.addin` points to the current Debug or Release output.
- Node.js 22 or later is installed.
- A real RVT model is open for document, level, element and parameter tests.

## Start

1. Build: `dotnet build DevelopmentTools.sln -c Debug`.
2. Restart Revit 2024 so the new DLL is loaded.
3. Open a real RVT model.
4. In the `Development tools` Ribbon, click `查詢狀態`.
5. Start MCP: `powershell -ExecutionPolicy Bypass -File .\scripts\start-beai-mcp.ps1`.

The endpoint is `http://127.0.0.1:3000/mcp`. It is deliberately not bound to the company LAN.

## MCP Inspector

List tools from the current Inspector CLI:

```powershell
npx @modelcontextprotocol/inspector --cli http://127.0.0.1:3000/mcp --transport http --method tools/list
```

For the visual Inspector, run `npx @modelcontextprotocol/inspector`, choose `Streamable HTTP`, enter `http://127.0.0.1:3000/mcp`, then connect.

Execute these tools in order:

1. `system_health`
2. `get_active_document`
3. `list_levels`
4. `query_elements` with `{ "category": "Doors", "level": "1F", "limit": 100 }`
5. `get_element_parameters` with a real Element ID returned by step 4.

## Direct Smoke Test

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-beai-v01.ps1 -Category Doors -Level 1F
powershell -ExecutionPolicy Bypass -File .\scripts\test-beai-v01.ps1 -ElementIds 123456
```

## Failure Tests

Test Revit without a model, Revit closed, MCP stopped, Bridge OFF, invalid category, invalid Element ID and timeout. Every case must return a stable error without crashing Revit.

The kill switch is `DevelopmentTools.Addin\Config\feature-flags.json`. Set `externalAccess` to `false`; the bridge reloads it within approximately two seconds.
