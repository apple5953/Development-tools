"""Development tools 維運判斷邏輯（純函式，不連網、不碰 Revit，方便單元測試）。

devtools_ops.py 負責向 GitHub 取資料，再交給這裡判斷：
- evaluate_pipeline(): 把發佈管線快照轉成「問題清單」，每個問題寫明由誰處理（agent / human）。
- triage_log(): 把 RevitAddinLog.txt 的錯誤分組、分類、對應到原始碼位置。
- check_release_pr(): 判斷 release PR 是否符合「agent 可以合併」的固定條件。
"""
from __future__ import annotations

import re
from datetime import datetime, timezone
from pathlib import Path

RUNNER_LABEL = "revit-2024"
MAINTAINER = "apple5953"
CANDIDATE_WORKFLOW = "revit-candidate.yml"
PREPARE_WORKFLOW = "prepare-production-release.yml"
QUEUED_ALERT_MINUTES = 15
APPROVAL_ALERT_HOURS = 24

CANDIDATE_ARTIFACT = re.compile(r"^development-tools-candidate-v(?P<version>\d+\.\d+\.\d+)-(?P<sha>[0-9a-fA-F]+)$")
RELEASE_BRANCH = re.compile(r"^release/v(?P<version>\d+\.\d+\.\d+)-run-(?P<run>\d+)$")
SEMVER = re.compile(r"^v?(\d+)\.(\d+)\.(\d+)$")


# ---------------------------------------------------------------- 共用

def parse_version(value):
    m = SEMVER.fullmatch((value or "").strip())
    if not m:
        return None
    return tuple(int(x) for x in m.groups())


def parse_time(value):
    if not value:
        return None
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def minutes_between(start, now):
    t = parse_time(start)
    return None if t is None else round((now - t).total_seconds() / 60)


def problem(code, severity, actor, summary, action, url=None, **extra):
    """actor: agent = agent 可以自己處理；human = 只有董事長（apple5953）能做。"""
    p = {"code": code, "severity": severity, "actor": actor, "summary": summary, "action": action}
    if url:
        p["url"] = url
    p.update(extra)
    return p


def candidate_version(artifact_names):
    for name in artifact_names or []:
        m = CANDIDATE_ARTIFACT.fullmatch(name.strip())
        if m:
            return m.group("version")
    return None


# ---------------------------------------------------------------- 發佈管線

def evaluate_pipeline(snap, now=None):
    """snap 結構見 devtools_ops.collect_snapshot()。回傳 problems（依嚴重度排序）。"""
    now = now or datetime.now(timezone.utc)
    problems = []

    # 1. Revit 2024 runner
    runners = snap.get("runners") or []
    labeled = [r for r in runners if RUNNER_LABEL in r.get("labels", [])]
    online = [r for r in labeled if r.get("status") == "online"]
    if not online:
        names = "、".join(r["name"] for r in labeled) or "（沒有任何 revit-2024 runner）"
        problems.append(problem(
            "RUNNER_OFFLINE", "high", "agent",
            f"Revit 2024 runner 不在線上：{names}",
            "在裝 runner 的電腦執行 `python tools/agent_ops/devtools_ops.py ensure-runner`；"
            "若電腦關機或沒有 runner，回報董事長開機。",
            runners=[{"name": r["name"], "status": r.get("status")} for r in labeled]))

    # 2. 候選版編譯
    cand_runs = snap.get("candidate_runs") or []
    for run in cand_runs:
        if run.get("status") in ("queued", "waiting", "pending"):
            waited = minutes_between(run.get("created_at"), now)
            if waited is not None and waited >= QUEUED_ALERT_MINUTES:
                problems.append(problem(
                    "CANDIDATE_QUEUED", "high", "agent",
                    f"候選版編譯排隊 {waited} 分鐘沒人接手（通常是 runner 沒開）",
                    "先處理 RUNNER_OFFLINE；runner 上線後會自動接手，不用重跑。",
                    url=run.get("html_url"), run_id=run.get("id"), waited_minutes=waited))
    latest_done = next((r for r in cand_runs if r.get("status") == "completed"), None)
    if latest_done and latest_done.get("conclusion") not in ("success", "cancelled", "skipped"):
        problems.append(problem(
            "CANDIDATE_FAILED", "high", "agent",
            f"最近一次候選版編譯失敗（{latest_done.get('conclusion')}）",
            "用 `gh run view <id> --log-failed` 讀錯誤；程式錯誤開 fix 分支與 PR，環境問題（Revit/.NET/PowerShell 7）修 runner 電腦後 `gh run rerun <id>`。",
            url=latest_done.get("html_url"), run_id=latest_done.get("id")))

    # 3. 正式版、更新清單、候選版的版本關係
    manifest_v = (snap.get("manifest") or {}).get("latest_version")
    release_tag = (snap.get("latest_release") or {}).get("tag_name")
    if manifest_v and release_tag and parse_version(manifest_v) != parse_version(release_tag):
        problems.append(problem(
            "MANIFEST_RELEASE_MISMATCH", "high", "agent",
            f"main 的 update_manifest.json 是 {manifest_v}，但最新 GitHub Release 是 {release_tag}",
            "檢查 Publish approved production release 的執行紀錄；失敗就 `gh run rerun`，仍失敗回報董事長。"))

    # 4. 等董事長核准 production
    for run in snap.get("prepare_runs") or []:
        if run.get("status") == "waiting":
            waited = minutes_between(run.get("created_at"), now) or 0
            problems.append(problem(
                "PRODUCTION_APPROVAL_PENDING", "medium" if waited < APPROVAL_ALERT_HOURS * 60 else "high", "human",
                f"正式發佈等待 production 核准（已等 {waited // 60} 小時 {waited % 60} 分）",
                "董事長打開連結 → Review deployments → 勾 production → Approve and deploy。agent 不能代按。",
                url=run.get("html_url"), run_id=run.get("id"), waited_minutes=waited))
        elif run.get("status") == "completed" and run.get("conclusion") == "failure" and run.get("id") in (snap.get("prepare_failed_at_pr_step") or []):
            problems.append(problem(
                "RELEASE_PR_NOT_CREATED", "high", "agent",
                "核准後的準備流程只差「開 release PR」失敗（repo 不允許 GitHub Actions 開 PR）",
                "執行 `python tools/agent_ops/devtools_ops.py open-release-pr`；並請董事長到 Settings › Actions › General 勾選 Allow GitHub Actions to create and approve pull requests。",
                url=run.get("html_url"), run_id=run.get("id")))

    # 5. 有 release 分支卻沒有 PR
    open_pr_heads = {pr.get("headRefName") for pr in snap.get("open_prs") or []}
    for branch in snap.get("release_branches") or []:
        m = RELEASE_BRANCH.fullmatch(branch)
        if not m or branch in open_pr_heads:
            continue
        v = parse_version(m.group("version"))
        if manifest_v and v and v <= parse_version(manifest_v):
            continue  # 已經發佈過的舊分支
        problems.append(problem(
            "RELEASE_PR_MISSING", "high", "agent",
            f"release 分支 {branch} 已產生，但沒有對應的 PR",
            f"執行 `python tools/agent_ops/devtools_ops.py open-release-pr --branch {branch}`。",
            branch=branch))

    # 6. 開著的 release PR
    for pr in snap.get("open_prs") or []:
        if RELEASE_BRANCH.fullmatch(pr.get("headRefName") or ""):
            problems.append(problem(
                "RELEASE_PR_OPEN", "medium", "agent",
                f"Release PR #{pr['number']} 尚未合併",
                f"執行 `python tools/agent_ops/devtools_ops.py merge-release-pr --pr {pr['number']}`；工具會檢查董事長已核准 production 且 CI 全綠才合併。",
                url=pr.get("url"), pr=pr["number"]))

    # 7. 有已編譯好、比正式版新、但還沒送正式發佈的候選版
    cand = snap.get("latest_candidate") or {}
    cv = cand.get("version")
    waiting_prepare = any(r.get("status") in ("waiting", "queued", "in_progress") for r in snap.get("prepare_runs") or [])
    has_release_pr = any(RELEASE_BRANCH.fullmatch(pr.get("headRefName") or "") for pr in snap.get("open_prs") or [])
    if cv and manifest_v and parse_version(cv) > parse_version(manifest_v) and not waiting_prepare and not has_release_pr:
        problems.append(problem(
            "CANDIDATE_READY", "low", "agent",
            f"候選版 v{cv} 已編譯成功，還沒送正式發佈（目前正式版 {manifest_v}）",
            f"寫好發佈說明後執行 `python tools/agent_ops/devtools_ops.py prepare-release --run-id {cand.get('run_id')} --version {cv} --notes \"...\"`，"
            "接著會出現 PRODUCTION_APPROVAL_PENDING 等董事長按核准。",
            url=cand.get("html_url"), run_id=cand.get("run_id"), version=cv))

    order = {"high": 0, "medium": 1, "low": 2}
    problems.sort(key=lambda p: order.get(p["severity"], 9))
    return problems


def overall(problems):
    if not problems:
        return "ok"
    if any(p["actor"] == "human" for p in problems):
        return "needs_human"
    return "needs_agent"


# ---------------------------------------------------------------- release PR 合併條件

def check_release_pr(pr, files, commits, prepare_runs, approvals_by_run, maintainer=MAINTAINER):
    """回傳 (可以合併?, 原因清單)。全部條件都成立才可以合併。

    pr: gh pr view --json number,headRefName,state,mergeable,statusCheckRollup,isDraft,baseRefName
    files: PR 變更的檔名清單
    commits: [{author_login, committed_at}]
    prepare_runs: Prepare production release 的 run 清單
    approvals_by_run: {run_id: [{"user": login, "state": "approved"|"rejected"}]}
    """
    reasons = []
    m = RELEASE_BRANCH.fullmatch(pr.get("headRefName") or "")
    if not m:
        return False, ["不是 release/vX.Y.Z-run-N 分支"]
    version = m.group("version")
    if pr.get("state") != "OPEN":
        reasons.append(f"PR 狀態是 {pr.get('state')}")
    if pr.get("isDraft"):
        reasons.append("PR 是草稿")
    if pr.get("baseRefName") != "main":
        reasons.append("目標分支不是 main")
    if pr.get("mergeable") not in ("MERGEABLE", None):
        reasons.append(f"GitHub 判定不能合併：{pr.get('mergeable')}")

    allowed = {"update_manifest.json", f"release/releases/v{version}.json"}
    extra = sorted(set(files) - allowed)
    if extra:
        reasons.append("PR 改了發佈紀錄以外的檔案：" + "、".join(extra))
    if f"release/releases/v{version}.json" not in files:
        reasons.append(f"缺少 release/releases/v{version}.json")

    bad_checks = []
    for c in pr.get("statusCheckRollup") or []:
        state = (c.get("conclusion") or c.get("state") or c.get("status") or "").upper()
        if state not in ("SUCCESS", "NEUTRAL", "SKIPPED"):
            bad_checks.append(f"{c.get('name') or c.get('context')}={state or 'PENDING'}")
    if not pr.get("statusCheckRollup"):
        bad_checks.append("沒有任何 CI 結果")
    if bad_checks:
        reasons.append("CI 未全綠：" + "、".join(bad_checks))

    first_commit = min((parse_time(c.get("committed_at")) for c in commits if c.get("committed_at")), default=None)
    approved = False
    for run in prepare_runs or []:
        created = parse_time(run.get("created_at"))
        if first_commit and created and not (created <= first_commit and (first_commit - created).total_seconds() < 6 * 3600):
            continue
        for a in approvals_by_run.get(run.get("id")) or approvals_by_run.get(str(run.get("id"))) or []:
            if a.get("user") == maintainer and a.get("state") == "approved":
                approved = True
    if not approved:
        reasons.append(f"找不到 {maintainer} 在產生這個分支前 6 小時內核准 production 的紀錄")
    return (not reasons), reasons


# ---------------------------------------------------------------- RevitAddinLog.txt 分診

LOG_LINE = re.compile(r"^\[(?P<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d+)?)\]\s?(?P<msg>.*)$")
ERROR_HINT = re.compile(r"error|exception|fail|異常|錯誤|失敗|崩潰|閃退", re.I)
NOISE = re.compile(r"IsSyncingOrLoading check", re.I)

CATEGORIES = [
    ("document-state", re.compile(r"ObjectDisposed|InvalidObject|is not valid|not a valid|已關閉|closed|IsValidObject|disposed", re.I),
     "文件或元素已失效：處理前檢查 doc.IsValidObject、!doc.IsReadOnly、!doc.IsModifiable 與元素 IsValidObject；文件關閉中直接 return。"),
    ("transaction-conflict", re.compile(r"Transaction|ModificationForbidden|read-?only|唯讀|modifiable|another transaction|交易|ModificationOutsideTransaction", re.I),
     "和其他外掛的交易衝突：全域事件裡不要開交易，改排 ExternalEvent/Idling；開交易前檢查 doc.IsModifiable，失敗 RollBack 並只寫記錄。"),
    ("null-reference", re.compile(r"NullReference|Object reference not set|未將物件參考", re.I),
     "空值：檢查 ActiveUIDocument、ActiveView、Application.Current（在 Revit 常是 null，改用 App.UiDispatcher）。"),
    ("thread", re.compile(r"calling thread|different thread|InvalidOperationException.*thread|執行緒|API context", re.I),
     "在非 Revit 主執行緒呼叫 API：改用 ExternalEvent，UI 用 App.UiDispatcher。"),
    ("global-event", re.compile(r"OnViewActivated|ViewActivated|DocumentChanged|DocumentOpened|DocumentClosing|Idling|外部事件", re.I),
     "全域事件會被其他外掛觸發：只有自己的面板開著時才處理，例外只寫 RevitAddinLog.txt，不跳視窗（參考 PR #36 AdaptiveRegion 修法）。"),
    ("io", re.compile(r"IOException|UnauthorizedAccess|FileNotFound|DirectoryNotFound|being used by another process", re.I),
     "檔案權限或路徑：改用使用者可寫的資料夾，失敗時降級而非中斷。"),
]


def _normalize(msg):
    s = re.sub(r"[A-Za-z]:\\[^\s'\"]+", "<path>", msg)
    s = re.sub(r"\b[0-9a-fA-F]{8}-[0-9a-fA-F-]{27,}\b", "<guid>", s)
    s = re.sub(r"\d+", "#", s)
    return s.strip()[:200]


def _source_key(msg):
    m = re.match(r"^\[(?P<tag>[^\]]{2,40})\]", msg)
    if m:
        return f"[{m.group('tag')}]"
    m = re.match(r"^(?P<head>[^:：]{4,60})[:：]", msg)
    return m.group("head").strip() if m else None


def classify(text):
    for name, pat, advice in CATEGORIES:
        if pat.search(text):
            return name, advice
    return "other", "讀完整堆疊後判斷；先確認是不是本外掛自己的程式。"


def triage_log(text, since=None):
    """回傳依次數排序的錯誤群組。since：datetime，只算之後的紀錄。"""
    groups = {}
    current = None
    for raw in text.splitlines():
        m = LOG_LINE.match(raw)
        if not m:
            if current is not None and raw.strip():
                current["detail"].append(raw.strip())  # 堆疊或多行訊息接在上一筆
            continue
        current = None
        msg = m.group("msg")
        if NOISE.search(msg) or not ERROR_HINT.search(msg):
            continue
        ts = datetime.strptime(m.group("ts")[:19], "%Y-%m-%d %H:%M:%S")
        if since and ts < since:
            continue
        sig = _normalize(msg)
        g = groups.get(sig)
        if not g:
            g = groups[sig] = {"signature": sig, "sample": msg, "source_key": _source_key(msg),
                               "count": 0, "first_seen": m.group("ts"), "last_seen": m.group("ts"), "detail": []}
        g["count"] += 1
        g["last_seen"] = m.group("ts")
        current = g if len(g["detail"]) < 12 else None
    out = []
    for g in groups.values():
        cat, advice = classify(g["sample"] + "\n" + "\n".join(g["detail"]))
        g["category"], g["advice"] = cat, advice
        out.append(g)
    out.sort(key=lambda g: (-g["count"], g["signature"]))
    return out


def locate_sources(groups, repo_root, max_hits=5):
    """在 repo 的 .cs 檔裡找記錄訊息的字面字串，標出可能的出錯位置。"""
    root = Path(repo_root)
    files = [p for p in root.rglob("*.cs")
             if not any(part in ("bin", "obj", "Archive_Unused_2026-06-05", ".git") for part in p.parts)]
    cache = {}
    for g in groups:
        key = g.get("source_key")
        hits = []
        if key:
            needle = key if key.startswith("[") else key + ":"
            for p in files:
                text = cache.get(p)
                if text is None:
                    try:
                        text = cache[p] = p.read_text(encoding="utf-8", errors="ignore")
                    except OSError:
                        continue
                if needle in text:
                    for i, line in enumerate(text.splitlines(), 1):
                        if needle in line:
                            hits.append(f"{p.relative_to(root).as_posix()}:{i}")
                            if len(hits) >= max_hits:
                                break
                if len(hits) >= max_hits:
                    break
        g["source_hits"] = hits
    return groups
