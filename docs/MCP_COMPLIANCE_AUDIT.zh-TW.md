# BEAI MCP／Reality Bridge 合規稽核

稽核日期：2026-10-20。適用範圍：Revit 2024、.NET Framework 4.8、BEAI Reality Bridge V0.1 與本機 MCP Server。

## 結論

程式架構符合根目錄 `AGENTS.md` 的安全邊界。MCP 是 AI 廠商中立的本機 Client Adapter；`BEAI.QueryHost.EmbeddedBridge` 是 DevelopmentTools 啟動的唯一 Reality Bridge。外部請求先進入固定工具白名單，再經 loopback HTTP、既有請求佇列及 Revit `ExternalEvent`，最後才在 Revit UI 執行緒讀取模型。

此結論代表程式碼、合約、CI 與本機建置合規。Revit 2024 真實模型的完整 A–D 驗收仍須留下模型、Element ID、結果與日誌證據；在完成前不得把 `docs/BEAI_V0_1_REPORT.md` 的整體狀態改成 PASS。Revit 2025／2026 未驗證。

`RibbonCatalog` 仍保留對另行安裝之 `Horizun.Revit` BIM MCP 外掛的相容按鈕。該 DLL 不在本儲存庫、不由 DevelopmentTools 建置或打包，因此不屬於本次 BEAI MCP 合規證明。若電腦另行啟用該外掛，其寫入、Python 或其他能力必須依該產品本身的安全審查處理；不可把它視為 BEAI Reality Bridge V0.1 的已核准工具，也不可由新 Agent 依賴它建立平行入口。

## 強制邊界

| 要求 | 實作與證據 |
|---|---|
| 不建立第二套 Bridge | 主外掛只有一個 `BEAI.QueryHost.csproj` 參考及一個 `EmbeddedBridge.Start`。舊 Adapter 不在執行路徑。 |
| AI 廠商中立 | MCP 合約只描述結構化工具，不含 ChatGPT、Claude 或特定模型 SDK。 |
| loopback only | MCP 固定監聽 `127.0.0.1:3000`；Reality Bridge 固定監聽 `127.0.0.1:8765`。Gateway 拒絕非 loopback 及非 `/api/v1/execute` 位址。 |
| 明確工具白名單 | 只註冊 `system_health`、`get_active_document`、`list_levels`、`query_elements`、`get_element_parameters`。Gateway 另有執行期 allowlist。 |
| ExternalEvent | HTTP Host 只負責接收與排隊；`BeaiQueryExternalEventHandler` 在 Revit UI 執行緒呼叫 Dispatcher。 |
| V0.1 唯讀 | 五個 MCP 工具均標示 read-only；Reality Bridge 沒有 `new Transaction` 或 `TransactionMode.Manual`；寫入 HTTP 方法被拒絕。 |
| 不讓 LLM 執行任意命令 | 沒有任意類別、反射、腳本或 Transaction 工具；未知工具回傳 `BEAI_NOT_SUPPORTED`。 |
| 保留舊端點 | 舊唯讀 HTTP 路由保留。規則重載、Worker 重啟與回退路由仍存在，但預設 `actions=false` 時回傳 403。 |
| 負載限制 | Bridge 對已知 Content-Length 與 chunked body 都強制 1 MB 上限；元素及參數批次另有限制。 |
| CI 防回歸 | 每個 PR 都執行 `scripts/validate-beai-mcp.py`、TypeScript build 與 Node 測試。 |

## 工具資料流

```text
Codex / Claude / MCP Client
  -> BEAI MCP 127.0.0.1:3000/mcp
  -> 五個唯讀工具白名單
  -> BridgeGateway /api/v1/execute
  -> Reality Bridge 127.0.0.1:8765
  -> BeaiQueryExternalEventHandler queue
  -> Autodesk Revit ExternalEvent / UI thread
  -> RealityDispatcher + GenericRevitReader
  -> 結構化唯讀回應
```

## 本次修正

- Gateway 強制 canonical endpoint、執行期工具白名單與 request ID 對應。
- 移除 MCP Schema 中未實作的查詢欄位；`parameters` 現在會實際過濾回傳參數。
- Reality Bridge 拒絕未知合約版本，錯誤回應不再回傳內部例外文字。
- 維護型舊端點由 `actions` feature flag 保護，預設關閉。
- chunked HTTP body 也受 1 MB 限制。
- PR 必須建置及測試 MCP，並執行跨檔案合規檢查。

## 回退

若新 MCP 發生相容問題，可停止本機 MCP Server；Revit 主外掛與舊唯讀 HTTP 路由仍可獨立運作。若需回退程式版本，使用既有整包 Updater／Release 回退，不可啟動舊 Adapter 作為第二套 Bridge。
