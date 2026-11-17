#!/usr/bin/env python3
"""Development tools 維運 CLI：給 AI agent（Paperclip 專業塔、Codex、Claude Code）呼叫。

只用 Python 標準函式庫與已登入的 GitHub CLI（gh）。每個指令都輸出 JSON（--format md 輸出給人看的摘要）。
說明與權限邊界：docs/AGENT_OPS.zh-TW.md

  python tools/agent_ops/devtools_ops.py status
  python tools/agent_ops/devtools_ops.py triage-log --log "<RevitAddinLog.txt>" [--since 2026-11-01]
  python tools/agent_ops/devtools_ops.py ensure-runner [--runner-dir C:\\actions-runner]
  python tools/agent_ops/devtools_ops.py prepare-release --run-id N --version X.Y.Z --notes "..."
  python tools/agent_ops/devtools_ops.py open-release-pr [--branch release/vX.Y.Z-run-N]
  python tools/agent_ops/devtools_ops.py merge-release-pr --pr N [--dry-run]

結束碼：0 正常／沒有問題；10 有 agent 可以處理的問題；20 需要董事長；2 使用方式錯誤；1 執行失敗。
"""
from __future__ import annotations

import argparse
import base64
import json
import os
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ops_core as core  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_REPO = os.environ.get("DEVTOOLS_REPO", "apple5953/Development-tools")
EXIT = {"ok": 0, "needs_agent": 10, "needs_human": 20}


class OpsError(Exception):
    pass


# ---------------------------------------------------------------- gh

class Gh:
    def __init__(self, repo):
        self.repo = repo
        if not shutil.which("gh"):
            raise OpsError("找不到 gh（GitHub CLI）。請安裝並以 apple5953 登入：gh auth login")

    def run(self, *args, input_text=None):
        p = subprocess.run(["gh", *args], capture_output=True, text=True, encoding="utf-8", input=input_text)
        if p.returncode != 0:
            raise OpsError(f"gh {' '.join(args[:3])} 失敗：{(p.stderr or p.stdout).strip()[:400]}")
        return p.stdout

    def api(self, path, method="GET", fields=None):
        args = ["api", "-H", "Accept: application/vnd.github+json"]
        if method != "GET":
            args += ["-X", method]
        for k, v in (fields or {}).items():
            args += ["-f", f"{k}={v}"]
        out = self.run(*args, path.replace("{repo}", self.repo))
        return json.loads(out) if out.strip() else None

    def api_or_none(self, path):
        try:
            return self.api(path)
        except OpsError:
            return None


# ---------------------------------------------------------------- 快照

def _runs(gh, workflow, n=10):
    data = gh.api(f"repos/{{repo}}/actions/workflows/{workflow}/runs?per_page={n}") or {}
    keep = ("id", "status", "conclusion", "created_at", "updated_at", "html_url", "head_sha", "event")
    return [{k: r.get(k) for k in keep} for r in data.get("workflow_runs", [])]


def _failed_at_pr_step(gh, run):
    jobs = gh.api_or_none(f"repos/{{repo}}/actions/runs/{run['id']}/jobs") or {}
    for job in jobs.get("jobs", []):
        failed = [s.get("name", "") for s in job.get("steps", []) if s.get("conclusion") == "failure"]
        if failed and all("release PR" in s for s in failed):
            return True
    return False


def collect_snapshot(gh):
    runners = gh.api("repos/{repo}/actions/runners") or {}
    snap = {
        "repo": gh.repo,
        "at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "runners": [{"name": r["name"], "status": r.get("status"), "busy": r.get("busy"),
                     "labels": [l["name"] for l in r.get("labels", [])]} for r in runners.get("runners", [])],
        "candidate_runs": _runs(gh, core.CANDIDATE_WORKFLOW),
        "prepare_runs": _runs(gh, core.PREPARE_WORKFLOW, 5),
    }
    manifest = gh.api_or_none("repos/{repo}/contents/update_manifest.json?ref=main")
    if manifest and manifest.get("content"):
        snap["manifest"] = json.loads(base64.b64decode(manifest["content"]).decode("utf-8-sig"))
    rel = gh.api_or_none("repos/{repo}/releases/latest")
    snap["latest_release"] = {"tag_name": rel.get("tag_name"), "published_at": rel.get("published_at"),
                              "html_url": rel.get("html_url")} if rel else None

    latest_ok = next((r for r in snap["candidate_runs"] if r["status"] == "completed" and r["conclusion"] == "success"), None)
    if latest_ok:
        arts = gh.api_or_none(f"repos/{{repo}}/actions/runs/{latest_ok['id']}/artifacts") or {}
        names = [a["name"] for a in arts.get("artifacts", []) if not a.get("expired")]
        snap["latest_candidate"] = {"run_id": latest_ok["id"], "version": core.candidate_version(names),
                                    "html_url": latest_ok["html_url"], "artifacts": names}

    snap["prepare_failed_at_pr_step"] = [r["id"] for r in snap["prepare_runs"][:3]
                                         if r["status"] == "completed" and r["conclusion"] == "failure" and _failed_at_pr_step(gh, r)]
    prs = json.loads(gh.run("pr", "list", "-R", gh.repo, "--state", "open", "--limit", "50",
                            "--json", "number,title,headRefName,url,isDraft,createdAt"))
    snap["open_prs"] = prs
    branches = gh.api("repos/{repo}/branches?per_page=100") or []
    snap["release_branches"] = [b["name"] for b in branches if b["name"].startswith("release/v")]
    return snap


# ---------------------------------------------------------------- 指令

def cmd_status(args, gh):
    snap = collect_snapshot(gh)
    problems = core.evaluate_pipeline(snap)
    state = core.overall(problems)
    result = {
        "state": state,
        "repo": gh.repo,
        "at": snap["at"],
        "stable_version": (snap.get("manifest") or {}).get("latest_version"),
        "latest_release": (snap.get("latest_release") or {}).get("tag_name"),
        "latest_candidate": snap.get("latest_candidate"),
        "runners": snap["runners"],
        "problems": problems,
    }
    if args.include_snapshot:
        result["snapshot"] = snap
    return result, EXIT[state]


def cmd_triage_log(args, gh):
    paths = [Path(p) for p in args.log]
    text = ""
    for p in paths:
        old = Path(str(p) + ".old")
        for f in ([old] if old.exists() and args.include_old else []) + [p]:
            if not f.exists():
                raise OpsError(f"找不到記錄檔：{f}")
            text += f.read_text(encoding="utf-8", errors="replace") + "\n"
    since = datetime.strptime(args.since, "%Y-%m-%d") if args.since else None
    groups = core.triage_log(text, since)
    core.locate_sources(groups, REPO_ROOT)
    for g in groups:
        g["detail"] = g["detail"][:6]
    result = {"state": "ok" if not groups else "needs_agent", "files": [str(p) for p in paths],
              "since": args.since, "groups": groups[: args.top]}
    return result, 0 if not groups else EXIT["needs_agent"]


def _local_listener_running():
    if os.name != "nt":
        return None
    p = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Runner.Listener.exe", "/NH"], capture_output=True, text=True)
    return "Runner.Listener.exe" in p.stdout


def cmd_ensure_runner(args, gh):
    def labeled():
        data = gh.api("repos/{repo}/actions/runners") or {}
        return [r for r in data.get("runners", []) if any(l["name"] == core.RUNNER_LABEL for l in r.get("labels", []))]

    before = labeled()
    if any(r.get("status") == "online" for r in before):
        return {"state": "ok", "action": "none", "runners": [{"name": r["name"], "status": r["status"]} for r in before]}, 0
    if os.name != "nt":
        raise OpsError("ensure-runner 只能在裝 runner 的 Windows 電腦上執行。")
    runner_dir = Path(args.runner_dir)
    run_cmd = runner_dir / "run.cmd"
    if not run_cmd.exists():
        raise OpsError(f"{runner_dir} 沒有 run.cmd，這台電腦沒有裝 runner。請回報董事長。")
    if args.dry_run:
        return {"state": "needs_agent", "action": "would_start", "runner_dir": str(runner_dir)}, EXIT["needs_agent"]
    if not _local_listener_running():
        subprocess.run(["powershell", "-NoProfile", "-Command",
                        f"Start-Process -FilePath '{run_cmd}' -WorkingDirectory '{runner_dir}' -WindowStyle Minimized"],
                       check=True)
    deadline = time.time() + args.wait
    after = before
    while time.time() < deadline:
        time.sleep(10)
        after = labeled()
        if any(r.get("status") == "online" for r in after):
            return {"state": "ok", "action": "started", "runners": [{"name": r["name"], "status": r["status"]} for r in after]}, 0
    raise OpsError(f"已啟動 {run_cmd}，但 {args.wait} 秒內 GitHub 仍顯示離線。請查看 {runner_dir}\\_diag 的記錄。")


def cmd_prepare_release(args, gh):
    run = gh.api(f"repos/{{repo}}/actions/runs/{args.run_id}")
    if run.get("path", "").split("/")[-1].split("@")[0] != core.CANDIDATE_WORKFLOW:
        raise OpsError(f"run {args.run_id} 不是 {core.CANDIDATE_WORKFLOW} 的執行。")
    if run.get("conclusion") != "success":
        raise OpsError(f"候選版 run {args.run_id} 沒有成功（{run.get('conclusion')}），不能送正式發佈。")
    arts = gh.api(f"repos/{{repo}}/actions/runs/{args.run_id}/artifacts") or {}
    names = [a["name"] for a in arts.get("artifacts", []) if not a.get("expired")]
    cv = core.candidate_version(names)
    if cv != args.version:
        raise OpsError(f"候選版工件版本是 {cv}，和輸入的 {args.version} 不符。")
    manifest = gh.api("repos/{repo}/contents/update_manifest.json?ref=main")
    stable = json.loads(base64.b64decode(manifest["content"]).decode("utf-8-sig"))["latest_version"]
    if core.parse_version(cv) <= core.parse_version(stable):
        raise OpsError(f"候選版 {cv} 沒有比正式版 {stable} 新。")
    if len(args.notes.strip()) < 8:
        raise OpsError("請寫給同事看的發佈說明（至少一句完整的話）。")
    if args.dry_run:
        return {"state": "ok", "action": "would_dispatch", "version": cv, "run_id": args.run_id}, 0
    gh.run("workflow", "run", core.PREPARE_WORKFLOW, "-R", gh.repo, "--ref", "main",
           "-f", f"candidate_run_id={args.run_id}", "-f", f"version={cv}", "-f", f"release_notes={args.notes}")
    time.sleep(5)
    latest = _runs(gh, core.PREPARE_WORKFLOW, 1)
    return {"state": "needs_human", "action": "dispatched", "version": cv,
            "next": "董事長要在 production 核准；之後再跑 status。",
            "run": latest[0] if latest else None}, EXIT["needs_human"]


RELEASE_PR_BODY = """## Production release v{version}

Approved candidate run: {run}

Release notes: {notes}

Merging this PR publishes the immutable candidate as a GitHub Release and activates it for the stable updater.

Rollback: revert this manifest PR and mark or remove the GitHub Release.

核心變更已揭露
"""


def cmd_open_release_pr(args, gh):
    branches = [args.branch] if args.branch else [
        b["name"] for b in (gh.api("repos/{repo}/branches?per_page=100") or []) if core.RELEASE_BRANCH.fullmatch(b["name"])]
    open_heads = {p["headRefName"] for p in json.loads(gh.run("pr", "list", "-R", gh.repo, "--state", "open",
                                                              "--limit", "50", "--json", "headRefName"))}
    stable = None
    manifest = gh.api_or_none("repos/{repo}/contents/update_manifest.json?ref=main")
    if manifest:
        stable = json.loads(base64.b64decode(manifest["content"]).decode("utf-8-sig"))["latest_version"]
    created = []
    for b in branches:
        m = core.RELEASE_BRANCH.fullmatch(b)
        if not m or b in open_heads:
            continue
        if stable and core.parse_version(m.group("version")) <= core.parse_version(stable):
            continue
        rec = gh.api(f"repos/{{repo}}/contents/release/releases/v{m.group('version')}.json?ref={b}")
        record = json.loads(base64.b64decode(rec["content"]).decode("utf-8-sig"))
        body = RELEASE_PR_BODY.format(version=m.group("version"), run=m.group("run"), notes=record.get("releaseNotes", ""))
        if args.dry_run:
            created.append({"branch": b, "would_create": True})
            continue
        url = gh.run("pr", "create", "-R", gh.repo, "--base", "main", "--head", b,
                     "--title", f"Release v{m.group('version')}", "--body", body).strip()
        created.append({"branch": b, "url": url})
    return {"state": "ok", "created": created}, 0


def cmd_merge_release_pr(args, gh):
    pr = json.loads(gh.run("pr", "view", str(args.pr), "-R", gh.repo, "--json",
                           "number,headRefName,state,mergeable,statusCheckRollup,isDraft,baseRefName,url"))
    files = [f["filename"] for f in gh.api(f"repos/{{repo}}/pulls/{args.pr}/files?per_page=100") or []]
    commits = [{"author_login": (c.get("author") or {}).get("login"),
                "committed_at": c["commit"]["committer"]["date"]}
               for c in gh.api(f"repos/{{repo}}/pulls/{args.pr}/commits?per_page=100") or []]
    prepare = _runs(gh, core.PREPARE_WORKFLOW, 10)
    approvals = {}
    for r in prepare:
        rows = gh.api_or_none(f"repos/{{repo}}/actions/runs/{r['id']}/approvals") or []
        approvals[r["id"]] = [{"user": (a.get("user") or {}).get("login"), "state": a.get("state")} for a in rows]
    ok, reasons = core.check_release_pr(pr, files, commits, prepare, approvals)
    if not ok:
        return {"state": "needs_human", "merged": False, "pr": pr.get("url"), "reasons": reasons}, EXIT["needs_human"]
    if args.dry_run:
        return {"state": "ok", "merged": False, "would_merge": True, "pr": pr.get("url")}, 0
    gh.run("pr", "merge", str(args.pr), "-R", gh.repo, "--merge")
    return {"state": "ok", "merged": True, "pr": pr.get("url"),
            "next": "Publish approved production release 會自動建立 GitHub Release；10 分鐘後再跑 status 確認。"}, 0


# ---------------------------------------------------------------- 輸出

def to_markdown(cmd, result):
    lines = [f"# devtools-ops {cmd}：{result.get('state')}"]
    if result.get("state") == "error":
        lines.append(f"錯誤：{result.get('error')}")
    elif cmd == "status":
        lines.append(f"正式版 {result.get('stable_version')}｜最新 Release {result.get('latest_release')}｜"
                     f"候選版 {(result.get('latest_candidate') or {}).get('version') or '無'}")
        lines.append("Runner：" + ("、".join(f"{r['name']}={r['status']}" for r in result["runners"]) or "無"))
        if not result["problems"]:
            lines.append("\n沒有問題。")
        for p in result["problems"]:
            who = "董事長" if p["actor"] == "human" else "agent"
            lines.append(f"\n- **{p['code']}**（{p['severity']}，{who}）{p['summary']}\n  做法：{p['action']}" + (f"\n  {p['url']}" if p.get("url") else ""))
    elif cmd == "triage-log":
        if not result["groups"]:
            lines.append("沒有錯誤紀錄。")
        for g in result["groups"]:
            lines.append(f"\n- **{g['count']} 次**｜{g['category']}｜{g['sample'][:160]}\n  最後一次 {g['last_seen']}"
                         f"\n  位置：{'、'.join(g['source_hits']) or '找不到'}\n  建議：{g['advice']}")
    else:
        lines.append("```json\n" + json.dumps(result, ensure_ascii=False, indent=2) + "\n```")
    return "\n".join(lines)


def build_parser():
    p = argparse.ArgumentParser(description="Development tools 維運 CLI（給 AI agent）")
    p.add_argument("--repo", default=DEFAULT_REPO)
    p.add_argument("--format", choices=["json", "md"], default="json")
    p.add_argument("--out", help="結果另存成檔案")
    sub = p.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("status", help="巡檢 runner、候選版、核准、release PR、更新清單")
    s.add_argument("--include-snapshot", action="store_true")
    s = sub.add_parser("triage-log", help="分析 RevitAddinLog.txt")
    s.add_argument("--log", action="append", required=True)
    s.add_argument("--since")
    s.add_argument("--top", type=int, default=20)
    s.add_argument("--include-old", action="store_true", help="一併讀 RevitAddinLog.txt.old")
    s = sub.add_parser("ensure-runner", help="runner 離線時在本機啟動 run.cmd")
    s.add_argument("--runner-dir", default=os.environ.get("DEVTOOLS_RUNNER_DIR", r"C:\actions-runner"))
    s.add_argument("--wait", type=int, default=90)
    s.add_argument("--dry-run", action="store_true")
    s = sub.add_parser("prepare-release", help="把成功的候選版送進 Prepare production release（仍需董事長核准）")
    s.add_argument("--run-id", required=True)
    s.add_argument("--version", required=True)
    s.add_argument("--notes", required=True)
    s.add_argument("--dry-run", action="store_true")
    s = sub.add_parser("open-release-pr", help="release 分支已產生但沒有 PR 時補開")
    s.add_argument("--branch")
    s.add_argument("--dry-run", action="store_true")
    s = sub.add_parser("merge-release-pr", help="董事長已核准 production 且 CI 全綠時合併 release PR")
    s.add_argument("--pr", required=True, type=int)
    s.add_argument("--dry-run", action="store_true")
    return p


COMMANDS = {"status": cmd_status, "triage-log": cmd_triage_log, "ensure-runner": cmd_ensure_runner,
            "prepare-release": cmd_prepare_release, "open-release-pr": cmd_open_release_pr,
            "merge-release-pr": cmd_merge_release_pr}


def main(argv=None):
    args = build_parser().parse_args(argv)
    try:
        gh = None if args.cmd == "triage-log" else Gh(args.repo)
        result, code = COMMANDS[args.cmd](args, gh)
    except OpsError as e:
        result, code = {"state": "error", "error": str(e)}, 1
    text = to_markdown(args.cmd, result) if args.format == "md" else json.dumps(result, ensure_ascii=False, indent=2)
    if args.out:
        Path(args.out).parent.mkdir(parents=True, exist_ok=True)
        Path(args.out).write_text(text, encoding="utf-8")
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except AttributeError:
        pass
    print(text)
    return code


if __name__ == "__main__":
    sys.exit(main())
