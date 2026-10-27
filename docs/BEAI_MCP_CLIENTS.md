# Reality Bridge MCP Client 設定

RTS 內建的 BIM MCP 由現有 `BEAI/MCP` Adapter 提供。所有模型請求仍轉送到 `127.0.0.1:8765/api/v1/execute`，並在 Revit `ExternalEvent` 上執行。V0.1 僅公開五個唯讀工具；不建立第二套 Revit Bridge，也不提供模型寫入工具。

發行包會把 Adapter 與 production dependencies 安裝到 `%LOCALAPPDATA%\DevelopmentTools\App\MCP`。安裝程式會執行 `setup-mcp-clients.ps1`，為電腦上既有的 Codex、Claude Code、Claude Desktop 與 Antigravity 設定 `beai-reality-bridge`。設定 JSON 前會建立時間戳備份。

## 系統需求

- Revit 2024 與 RTS 外掛
- Node.js 22 或更新版本
- 至少一個支援 MCP 的本機 AI Client

安裝程式不會自動下載 Node.js 或 AI Client；缺少的軟體會記錄後略過。紀錄位於 `%LOCALAPPDATA%\DevelopmentTools\Logs\mcp-client-setup.log`。

## 手動設定

Codex CLI：

```powershell
codex mcp add beai-reality-bridge -- node "$env:LOCALAPPDATA\DevelopmentTools\App\MCP\dist\index.js" --stdio
```

Claude Code：

```powershell
claude mcp add beai-reality-bridge --scope user -- node "$env:LOCALAPPDATA\DevelopmentTools\App\MCP\dist\index.js" --stdio
```

驗證時先啟動 Revit 2024 並開啟模型，再呼叫 `system_health`、`get_active_document`、`list_levels`、`query_elements` 或 `get_element_parameters`。若 Revit 尚未啟動，Client 仍可列出工具，但模型請求會回傳 Bridge 不可用錯誤。
