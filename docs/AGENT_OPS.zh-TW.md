# Development tools 維運：給 AI agent 調動的指令

這份文件讓 AI agent（Paperclip 專業塔 10F「外掛維運工程師」、Codex、Claude Code）用固定指令維運本外掛，不必每次重新摸索發佈流程。開發新功能仍照 [TEAM_AI_PLAYBOOK.md](TEAM_AI_PLAYBOOK.md) 與 [AI_AGENT_RIBBON_CONTRACT.zh-TW.md](AI_AGENT_RIBBON_CONTRACT.zh-TW.md)。

入口：`python tools/agent_ops/devtools_ops.py <指令>`。只用 Python 標準函式庫與已用 `apple5953` 登入的 `gh`。預設輸出 JSON；加 `--format md` 給人看，加 `--out <檔案>` 另存。

## 指令

| 指令 | 用途 | 誰可以執行 |
|---|---|---|
| `status` | 一次巡檢：revit-2024 runner、候選版編譯、production 核准、release 分支與 PR、最新 Release 與 `update_manifest.json` | agent，唯讀 |
| `triage-log --log <RevitAddinLog.txt>` | 把錯誤記錄分組、分類、對應到原始碼行號、給修法建議 | agent，唯讀，不需要 gh |
| `ensure-runner` | runner 離線時，在本機背景啟動 `C:\actions-runner\run.cmd`，等它上線 | agent，只在裝 runner 的 Windows 電腦 |
| `prepare-release --run-id N --version X.Y.Z --notes "..."` | 把編譯成功的候選版送進 `Prepare production release`；會停在 production 核准 | agent（檢查候選版成功、版本相符且比正式版新） |
| `open-release-pr` | release 分支已產生、但 GitHub Actions 沒權限開 PR 時補開 | agent |
| `merge-release-pr --pr N` | 合併 release PR，觸發正式 GitHub Release | agent，**只在下面的固定條件全部成立時** |

結束碼：`0` 正常、`10` 有 agent 能處理的問題、`20` 需要董事長、`1` 執行失敗、`2` 參數錯誤。

## `status` 的問題代碼

每個問題都有 `actor`：`agent` 表示 agent 照 `action` 自己處理；`human` 表示只有董事長能做，agent 只能把連結送給他。

| 代碼 | 意思 | actor |
|---|---|---|
| `RUNNER_OFFLINE` | 沒有在線上的 revit-2024 runner（家裡電腦重開機後 runner 會停） | agent：`ensure-runner` |
| `CANDIDATE_QUEUED` | 候選版排隊超過 15 分鐘 | agent：先處理 runner |
| `CANDIDATE_FAILED` | 最近一次候選版編譯失敗 | agent：讀 `gh run view <id> --log-failed`，程式錯誤開 fix PR，環境錯誤修好後 `gh run rerun` |
| `CANDIDATE_READY` | 有比正式版新的候選版還沒送正式發佈 | agent：寫發佈說明後 `prepare-release` |
| `PRODUCTION_APPROVAL_PENDING` | 正式發佈等 production 核准 | **human**：Review deployments → production → Approve and deploy |
| `RELEASE_PR_NOT_CREATED` / `RELEASE_PR_MISSING` | 核准後只差開 release PR | agent：`open-release-pr` |
| `RELEASE_PR_OPEN` | release PR 還沒合併 | agent：`merge-release-pr`（條件不符時回傳 `needs_human` 與原因） |
| `MANIFEST_RELEASE_MISMATCH` | 更新清單和最新 Release 版本不一致 | agent：檢查 `Publish approved production release`，必要時 rerun |

## `merge-release-pr` 的固定條件

[CLAUDE.md](../CLAUDE.md) 規定 AI 不可合併、不可發佈。這個指令是**唯一的例外**，而且只在董事長已經在 GitHub 按下 production 核准之後，把同一個決定執行完。條件寫死在 `tools/agent_ops/ops_core.py` 的 `check_release_pr()`，全部成立才合併：

1. 分支名稱是 `release/vX.Y.Z-run-N`，目標是 `main`，不是草稿，GitHub 判定可合併。
2. 只改了 `update_manifest.json` 與 `release/releases/vX.Y.Z.json`，而且後者存在。
3. 所有 CI 檢查成功。
4. 在這個分支第一筆 commit 前 6 小時內，有一次 `Prepare production release` 由 `apple5953` 核准 production。

任何一條不成立就不合併，回傳原因，交給董事長。

## 董事長授權：維運 agent 可以合併修正 PR

董事長（apple5953）於 2026-11-17 授權 Paperclip 10F「外掛維運工程師」推送分支並合併**它自己開的修正 PR**，條件全部成立才可合併：

1. PR 由維運 agent 開，分支是 `fix/<簡述>`，目標 `main`。
2. 所有 CI 檢查成功（含 Core-change disclosure 與 Trusted maintainer gate）。
3. 外掛測試員在 Revit 2024 實測通過，1F 獨立 QA 驗收通過，兩者的紀錄連結寫在 PR 留言。
4. 沒有改 `.github/`、`release/`、`installer/`、`DevelopmentTools.Updater/`、授權（`CommandAccessGuard`、ToolId）與 RealityBridge 邊界。改到這些仍要董事長自己合併。

production 環境核准仍由董事長本人在 GitHub 按，agent 不能代按。其他 AI（模組開發用的 Codex／Claude Code）仍照 CLAUDE.md：只開 PR，不合併。

## 錯誤記錄分診（`triage-log`）

`RevitAddinLog.txt` 在外掛 DLL 同一個資料夾（超過 5 MB 會改名成 `.old`，加 `--include-old` 一起讀）。分類與建議：

| 分類 | 常見原因 | 修法方向 |
|---|---|---|
| `transaction-conflict` | 全域事件裡開交易，碰到別的外掛的交易或唯讀文件 | 全域事件不開交易；改排 ExternalEvent；先檢查 `doc.IsModifiable`；失敗 RollBack 只寫記錄 |
| `document-state` | 文件或元素已關閉、失效 | 先檢查 `IsValidObject`、`IsReadOnly` |
| `null-reference` | `ActiveUIDocument`、`ActiveView`、`Application.Current` 為 null | 加防呆；UI 改用 `App.UiDispatcher` |
| `thread` | 非 Revit 主執行緒呼叫 API | 改用 ExternalEvent |
| `global-event` | ViewActivated／DocumentChanged／Idling 被其他外掛觸發 | 只有自己的面板開著時才處理；例外只寫記錄、不跳視窗（範例：PR #36 AdaptiveRegion） |
| `io` | 權限或路徑 | 改用使用者可寫的資料夾 |

修正流程照 CLAUDE.md：開 `fix/<簡述>` 分支、只改相關檔案、改到 `App.cs` 等保護路徑時 PR 內文寫「核心變更已揭露」與回退方式，並附「不開面板、用其他外掛重現原本的操作」的實機測試步驟。

## 測試

```
python -m unittest discover -s tools/agent_ops -p "test_*.py" -v
```
