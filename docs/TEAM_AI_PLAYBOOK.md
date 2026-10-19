# BEAI 團隊 AI Agent 開發說明書（Codex / Claude Code）

適用：每位 BIM 工程師各自使用 Windows + GitHub Desktop + Codex 或 Claude Code。你不用將程式寄給主管，也不需要知道其他同事本機資料夾。

## 第一次設定（每位同事）
1. 向主管提供自己的 GitHub 帳號，由主管邀請成為 repo collaborator。接受邀請。
2. 安裝 Git、GitHub Desktop、Visual Studio / Build Tools（需要 .NET Framework 4.8 開發工具；若要在本機編譯 Revit 外掛，需合法 Revit 2024 API 開發環境）。
3. GitHub Desktop：File > Clone repository，選 `apple5953/Development-tools`，Local path 建議 `D:\Development-tools`。若公司禁止 D 槽，選有權限的路徑。
4. 確認 `main` 已更新，使用 Branch > New branch，名稱 `feature/<your-task>`。每項任務分支不同。
5. 在 Codex 或 Claude Code 開啟剛 Clone 的完整儲存庫資料夾，不要只選舊 Agent 的獨立資料夾。AI 應先讀 `AGENTS.md`, `CLAUDE.md`, `docs/MODULE_CONTRACT.md`, `docs/CONTRIBUTING.md`。
6. 將下方「可複製提示詞」貼給 AI。不要把個人密碼、GitHub token、Google 金鑰交給 AI。

## 每日工作
- 工作前：在 GitHub Desktop 確認所在分支；Fetch origin、同步最新 main（當前分支有未提交修改時先存檔/commit/stash，再依 Git 工具提示處理）。
- AI 只在自己分支、指定模組目錄內修改。AI 不得自行更改 main 或 Release。
- 工作後：Review changed files；排除 `bin/`, `obj/`, DLL、帳密、公司模型，再 Commit 與 Push origin。
- GitHub Desktop > Preview Pull Request > Create Pull Request，目標 main。尚未完成時建立 Draft PR。
- GitHub 網頁檢視 Actions 結果；提交測試報告。必須在 Revit 實測的部分，用主管指定的測試模型提供截圖、測試步驟與結果。
- 主管要求修改時，在同一分支繼續 Commit + Push；PR 會自動更新。
- PR 合併後，下一個任務從更新後的 main 建立新分支。不要在已合併分支持續堆功能。

## 可複製提示詞 — 第一次加入專案
```text
你是 BEAI Revit 專案的協作開發助理。請先執行唯讀環境檢查：
1. 顯示本機 repo 根目錄、git remote、git status、目前分支，確認 origin 是 apple5953/Development-tools。
2. 讀取 AGENTS.md、CLAUDE.md、docs/CONTRIBUTING.md、docs/MODULE_CONTRACT.md。
3. 不要直接修改 main、不執行 force push、不提交任何金鑰、不發布 Release。
4. 依照任務名稱建議 feature/<task> 分支。若目錄有未提交變更，先列出並請人確認，不能自行丟棄。
5. 檢查合法的本機 Revit 2024 + .NET Framework 4.8 建置環境，缺少就標記 BLOCKED，不要下載或提交專有 DLL。
6. 產出檢查報告：環境、預計編輯範圍、風險、下一步。沒有任務指派時只檢查，不編輯程式。
```

## 可複製提示詞 — 開發一個功能
```text
你是 BEAI Revit 功能模組工程師。
任務名稱：[在此填寫]
功能描述：[在此填寫]
允許修改目錄：[在此填寫]
驗收條件：[在此填寫]
請：
1. 先讀 AGENTS.md、CLAUDE.md、docs/MODULE_CONTRACT.md、docs/CONTRIBUTING.md。
2. 確認 git branch 是 feature/...，並只對允許路徑修改。
3. 分析現有類似模組、接口、ToolId、授權、Ribbon 的接入方式，不要建立第二套 RealityBridge。
4. 先列出改動檔案及相依需求。若需要更動 App.cs、RealityBridge、主 csproj、授權、Updater 或 Release，先標記 NEEDS_OWNER_APPROVAL，不要偷改。
5. 優先把演算法寫為可無 Revit 測試的程式，配上單元測試。
6. 只能執行安全的本機檢查；不允許直接部署正式環境，不允許 push main、merge PR、建立 Release。
7. 列出實際執行的測試命令、輸出、通過/失敗/未執行原因；Revit 實機尚未驗收不得標示通過。
8. 產出 PR 描述與人工測試步驟，等待工程師 Review 後自行提交。
```

## 可複製提示詞 — 舊獨立 Agent 專案匯入
```text
我有一個既有獨立 BIM AI Agent 專案，準備匯入 apple5953/Development-tools。
舊專案路徑：[填入舊資料夾]
目標模組名稱：[填入模組名稱]
先不要搬動或刪除舊專案：
1. 唯讀盤點來源路徑、語言、Revit API 版本、授權、Ribbon、Bridge、依賴與 Git 狀態。
2. 檢查當前新儲存庫分支是 feature/import-...；不要直接動 main。
3. 對照 DevelopmentTools.Modules/README.md 和 docs/MODULE_CONTRACT.md，擬定保留功能的最小移植方案。
4. 僅複製所需原始碼到 DevelopmentTools.Modules/<ModuleName>/；不複製 .git、bin、obj、金鑰、私有模型、Revit 原廠 DLL。
5. 若舊專案自帶 Bridge、MCP、登入、Ribbon 啟動器、Updater，先列出替換成現有元件的方案，不可製造第二套。
6. 主專案的引用、Ribbon、授權 ToolId 變更必須獨立列出並等待主管審核。
7. 編寫模組 README、測試與 PR 報告。遇到不明相依或編譯錯誤，不要憑空說明已修復。
```

## GitHub Desktop 版本衝突
當有黃色衝突提示，不要直接選「保留我的全部」。先比較雙方版本，要求 AI 解釋每個衝突如何合併；共用核心衝突交由主管處理。不可 force push 或重置同事分支。

## 同事不得碰的界線
不得把非自己任務的變更一起提交；不得自行啟用未授權工具、改動 Google 授權或發版；不得假設 CI 成功代表 Revit 實機成功；不得把公司真實模型與敏感資料公開提交。
