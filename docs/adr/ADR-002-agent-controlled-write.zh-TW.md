# ADR-002：AI agent 受控寫入（plan → 核准 → commit）

狀態：**提案，等待主管決定**。本 ADR 只有設計，沒有程式碼；核准後才開始實作，且實作 PR 會再標示核心變更。

## 背景

外掛要全面改成由 AI agent 執行（見 [AGENT_FIRST_MIGRATION.zh-TW.md](../AGENT_FIRST_MIGRATION.zh-TW.md)）。唯讀與「暫存後回復」的分析工具已經可以透過既有 Reality Bridge 的工具註冊機制提供（PR #33、#34）。但大部分外掛（開圖、圖紙、標註、裝修、配筋…）都會**改模型**，而 V0.1 Reality Bridge 明定唯讀，`analysis`、`actions` 旗標也都關閉。

如果直接讓 agent 呼叫會寫入的工具，會有三個問題：

1. agent 判斷錯誤時，一次可能改掉上百個元素，人事後才發現。
2. 沒有紀錄可以回答「這是誰、在什麼條件下、改了什麼」。
3. 建照、結構圖說最後是建築師、技師簽證，簽證人需要看得到並同意每一批變更。

## 選項

1. **直接寫入**：工具收到請求就開交易寫入。最快，但沒有人在迴圈中，違反上述三點。
2. **每個工具自己做確認視窗**：寫入前跳 TaskDialog。agent 無法回應視窗，而且人不在電腦前時整個 Revit 會卡住。
3. **兩段式受控寫入（建議）**：agent 先呼叫 `<area>.plan` 取得變更計畫（不改模型），人在 Revit 內或指定介面核准該計畫後，agent 再呼叫 `<area>.commit` 帶著計畫編號執行；每次 commit 一個 Revit 交易群組，可用 Revit 復原，並寫入稽核紀錄。

## 決定（提案）

採用選項 3，規則如下。

### 1. 工具形態

- 寫入工具成對註冊：`views.create_from_template.plan` 與 `.commit`（名稱沿用 `<area>.<action>`，以 `.plan`／`.commit` 結尾）。
- 在 `RealityModelEffect` 新增 `controlled-write`，只有成對的 commit 工具可以宣告。Registry 拒絕沒有對應 plan 的 commit。
- 新的旗標 `controlledWrite`（預設 `false`），與 `analysis` 分開開關。

### 2. plan

- 輸入：工具參數。輸出：`planId`、`documentId`（文件 GUID＋路徑）、`modelFingerprint`（見第 4 點）、變更清單（每一項：動作 create／modify／delete、類別、目標元素 ID 或將建立的名稱、修改前後值）、預估元素數量、風險等級、到期時間（預設 30 分鐘）。
- plan 不開交易；若需要試算（例如試排版），在 `TransactionGroup` 中試做後 `RollBack()`，與法規檢討相同的「暫存回復」模式。
- plan 存在本機 `%LOCALAPPDATA%\BEAI\Plans\<planId>.json`，內容雜湊（SHA-256）即為 `planHash`。

### 3. 核准

- **核准只能由人在 Revit 端完成**：Ribbon「Agent 中心」新增「待核准變更」清單，顯示 plan 的變更清單、風險、預覽（可選元素），人按「核准」或「拒絕」。核准紀錄寫入 plan 檔（核准人帳號＝目前 Google 登入帳號、時間）。
- HTTP／MCP 端**沒有**核准端點；agent 無法自行核准自己的 plan。
- 風險等級 `high`（例如刪除元素、改共用參數、跨文件轉移）一律需要逐筆核准；`low`（例如只建立新視圖）可由主管設定為「同一工作階段內同類 plan 自動核准」，預設關閉。

### 4. commit

- 輸入只有 `planId` 與 `planHash`。Bridge 依序檢查：plan 存在且未過期、已核准、雜湊一致、文件相同、`modelFingerprint` 未變（相關元素的 `VersionGuid`／`UniqueId` 與參數值與 plan 時相同）；任何一項不符就拒絕並要求重新 plan。
- 執行：一個 `TransactionGroup`（名稱 `BEAI Agent: <tool> <planId>`），內部可有多個交易；任何一步失敗整組 `RollBack()`，模型回到 commit 前。成功後 `Assimilate()`，人可以在 Revit 用一次「復原」撤銷整批。
- 回傳：實際建立／修改／刪除的元素 ID、與 plan 的差異（應為空）、`bim_verify` 類的事後檢查結果。
- commit 只能執行一次；之後同一 `planId` 再呼叫回傳 `BEAI_PLAN_CONSUMED`。

### 5. 稽核

- 每次 plan、核准、拒絕、commit、失敗都寫入 `%LOCALAPPDATA%\BEAI\Logs\agent-audit.jsonl`：時間、工具、planId、planHash、文件、核准人、元素數、結果。
- Agent 中心可匯出稽核紀錄給簽證人。

### 6. 交易位置

- 交易仍然寫在**模組**（Addin）的 Runner 內，不寫在 `BEAI.QueryHost` 原始碼中，`validate-beai-mcp.py` 的「Bridge 不得 `new Transaction(`」檢查維持不變。
- 但 commit 工具確實會改模型，因此 Bridge 合約版本需升為 V0.2，並在 `system.health`／capabilities 中回報 `controlledWrite` 旗標狀態。

## 第一批實作建議

| 順序 | 工具 | 理由 |
|---|---|---|
| 1 | `drawing_factory.plan/commit`（量化開圖） | 已是批次、每張圖一個交易且有失敗回復，最容易對應 plan 清單 |
| 2 | `sheets.rename.plan/commit` | 變更最單純（舊名→新名），適合驗證核准介面 |
| 3 | `areas.auto_boundary.plan/commit` | 建照產線的前置；配合 V5 樣板實測的「超出計入容積面積」自動回填 |
| 4 | `views.create_from_template.plan/commit` | 開圖核心 |

## 取捨

- 多一個核准步驟，agent 不能完全無人運作；這是刻意的：建照與結構成果需要簽證人負責。
- plan 到 commit 之間模型若被人修改，commit 會被拒絕而需重做 plan。
- 核准介面放在 Revit 內，人必須在 Revit 前；遠端核准（手機、Slack）屬 `remoteTransport`，另案評估。

## 需要主管決定的事項

1. 是否同意採用兩段式受控寫入（選項 3）。
2. 新增 `controlledWrite` 旗標與 `RealityModelEffect.ControlledWrite`、Bridge 合約升為 V0.2。
3. 核准人身分是否沿用 Google 登入帳號，或需另外的簽證人名單。
4. `low` 風險自動核准是否允許（預設關閉）。
5. 第一批實作順序。
