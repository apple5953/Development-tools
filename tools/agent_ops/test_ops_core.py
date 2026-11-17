"""單元測試：python -m unittest discover -s tools/agent_ops -p "test_*.py" -v"""
import sys
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import ops_core as core  # noqa: E402

NOW = datetime(2026, 11, 17, 3, 0, tzinfo=timezone.utc)


def base_snap(**over):
    snap = {
        "runners": [{"name": "DESKTOP-COOSTPI-revit2024", "status": "online", "labels": ["self-hosted", "Windows", "X64", "revit-2024"]}],
        "candidate_runs": [{"id": 1, "status": "completed", "conclusion": "success", "created_at": "2026-11-17T01:00:00Z", "html_url": "u1"}],
        "prepare_runs": [],
        "manifest": {"latest_version": "3.2.20"},
        "latest_release": {"tag_name": "v3.2.20"},
        "latest_candidate": {"run_id": 1, "version": "3.2.20", "html_url": "u1"},
        "open_prs": [],
        "release_branches": [],
    }
    snap.update(over)
    return snap


def codes(problems):
    return [p["code"] for p in problems]


class PipelineTests(unittest.TestCase):
    def test_healthy(self):
        self.assertEqual(core.evaluate_pipeline(base_snap(), NOW), [])
        self.assertEqual(core.overall([]), "ok")

    def test_runner_offline_and_queued(self):
        snap = base_snap(
            runners=[{"name": "DESKTOP-COOSTPI-revit2024", "status": "offline", "labels": ["revit-2024"]}],
            candidate_runs=[{"id": 2, "status": "queued", "created_at": "2026-11-17T02:30:00Z", "html_url": "u2"}],
        )
        p = core.evaluate_pipeline(snap, NOW)
        self.assertEqual(codes(p), ["RUNNER_OFFLINE", "CANDIDATE_QUEUED"])
        self.assertEqual(p[1]["waited_minutes"], 30)
        self.assertEqual(core.overall(p), "needs_agent")

    def test_short_queue_is_not_alert(self):
        snap = base_snap(candidate_runs=[{"id": 2, "status": "queued", "created_at": "2026-11-17T02:55:00Z"}])
        self.assertEqual(core.evaluate_pipeline(snap, NOW), [])

    def test_candidate_failed(self):
        snap = base_snap(candidate_runs=[{"id": 3, "status": "completed", "conclusion": "failure", "created_at": "2026-11-17T01:00:00Z"}])
        self.assertEqual(codes(core.evaluate_pipeline(snap, NOW)), ["CANDIDATE_FAILED"])

    def test_candidate_ready(self):
        snap = base_snap(latest_candidate={"run_id": 9, "version": "3.2.21", "html_url": "u9"})
        p = core.evaluate_pipeline(snap, NOW)
        self.assertEqual(codes(p), ["CANDIDATE_READY"])
        self.assertIn("--run-id 9 --version 3.2.21", p[0]["action"])

    def test_approval_pending_needs_human(self):
        snap = base_snap(latest_candidate={"run_id": 9, "version": "3.2.21"},
                         prepare_runs=[{"id": 5, "status": "waiting", "created_at": "2026-11-17T02:00:00Z", "html_url": "u5"}])
        p = core.evaluate_pipeline(snap, NOW)
        self.assertEqual(codes(p), ["PRODUCTION_APPROVAL_PENDING"])  # 已送出就不再提示 CANDIDATE_READY
        self.assertEqual(p[0]["actor"], "human")
        self.assertEqual(core.overall(p), "needs_human")

    def test_release_branch_without_pr(self):
        snap = base_snap(release_branches=["release/v3.2.21-run-77", "release/v3.2.20-run-43991801696"],
                         prepare_runs=[{"id": 6, "status": "completed", "conclusion": "failure", "created_at": "2026-11-17T02:00:00Z"}],
                         prepare_failed_at_pr_step=[6])
        self.assertEqual(sorted(codes(core.evaluate_pipeline(snap, NOW))), ["RELEASE_PR_MISSING", "RELEASE_PR_NOT_CREATED"])

    def test_release_pr_open(self):
        snap = base_snap(open_prs=[{"number": 37, "headRefName": "release/v3.2.21-run-77", "url": "pr37"}],
                         release_branches=["release/v3.2.21-run-77"])
        self.assertEqual(codes(core.evaluate_pipeline(snap, NOW)), ["RELEASE_PR_OPEN"])

    def test_manifest_mismatch(self):
        snap = base_snap(latest_release={"tag_name": "v3.2.18"})
        self.assertEqual(codes(core.evaluate_pipeline(snap, NOW)), ["MANIFEST_RELEASE_MISMATCH"])


class ReleasePrGateTests(unittest.TestCase):
    PR = {"number": 37, "headRefName": "release/v3.2.21-run-77", "state": "OPEN", "isDraft": False,
          "baseRefName": "main", "mergeable": "MERGEABLE",
          "statusCheckRollup": [{"name": "policy", "conclusion": "SUCCESS"}]}
    FILES = ["update_manifest.json", "release/releases/v3.2.21.json"]
    COMMITS = [{"author_login": "github-actions[bot]", "committed_at": "2026-11-17T02:20:00Z"}]
    RUNS = [{"id": 5, "created_at": "2026-11-17T02:00:00Z"}]

    def test_all_good(self):
        ok, reasons = core.check_release_pr(self.PR, self.FILES, self.COMMITS, self.RUNS, {5: [{"user": "apple5953", "state": "approved"}]})
        self.assertTrue(ok, reasons)

    def test_no_human_approval(self):
        ok, reasons = core.check_release_pr(self.PR, self.FILES, self.COMMITS, self.RUNS, {5: []})
        self.assertFalse(ok)
        self.assertTrue(any("核准" in r for r in reasons))

    def test_approval_by_someone_else(self):
        ok, _ = core.check_release_pr(self.PR, self.FILES, self.COMMITS, self.RUNS, {5: [{"user": "bot", "state": "approved"}]})
        self.assertFalse(ok)

    def test_extra_files_block(self):
        ok, reasons = core.check_release_pr(self.PR, self.FILES + ["DevelopmentTools.Addin/App.cs"], self.COMMITS, self.RUNS,
                                            {5: [{"user": "apple5953", "state": "approved"}]})
        self.assertFalse(ok)
        self.assertTrue(any("App.cs" in r for r in reasons))

    def test_red_ci_blocks(self):
        pr = dict(self.PR, statusCheckRollup=[{"name": "policy", "conclusion": "FAILURE"}])
        ok, _ = core.check_release_pr(pr, self.FILES, self.COMMITS, self.RUNS, {5: [{"user": "apple5953", "state": "approved"}]})
        self.assertFalse(ok)

    def test_old_approval_does_not_count(self):
        runs = [{"id": 4, "created_at": "2026-11-10T02:00:00Z"}]
        ok, _ = core.check_release_pr(self.PR, self.FILES, self.COMMITS, runs, {4: [{"user": "apple5953", "state": "approved"}]})
        self.assertFalse(ok)

    def test_not_release_branch(self):
        ok, _ = core.check_release_pr(dict(self.PR, headRefName="feature/x"), self.FILES, self.COMMITS, self.RUNS, {})
        self.assertFalse(ok)


LOG = """[2026-11-16 09:00:00.001] Revit 啟動
[2026-11-16 09:01:00.123] IsSyncingOrLoading check for 'C:\\a.rvt': False (Active count: 0)
[2026-11-16 09:02:00.000] [AdaptiveRegion] 外部事件執行異常: Attempt to modify the model outside of a transaction.
   at Autodesk.Revit.DB.Element.set_Parameter
[2026-11-16 09:05:00.000] [AdaptiveRegion] 外部事件執行異常: Attempt to modify the model outside of a transaction.
[2026-11-16 10:00:00.000] Error in OnViewActivated: Object reference not set to an instance of an object.
[2026-11-17 08:00:00.000] Error in OnViewActivated: Object reference not set to an instance of an object.
"""


class TriageTests(unittest.TestCase):
    def test_groups_and_categories(self):
        g = core.triage_log(LOG)
        self.assertEqual(len(g), 2)
        self.assertEqual(g[0]["count"], 2)
        cats = {x["source_key"]: x["category"] for x in g}
        self.assertEqual(cats["[AdaptiveRegion]"], "transaction-conflict")
        self.assertEqual(cats["Error in OnViewActivated"], "null-reference")
        adaptive = next(x for x in g if x["source_key"] == "[AdaptiveRegion]")
        self.assertIn("at Autodesk.Revit.DB.Element.set_Parameter", adaptive["detail"])

    def test_since(self):
        g = core.triage_log(LOG, since=datetime(2026, 11, 17))
        self.assertEqual([x["count"] for x in g], [1])

    def test_locate_sources(self):
        with tempfile.TemporaryDirectory() as d:
            src = Path(d, "Mod", "App.cs")
            src.parent.mkdir()
            src.write_text('class A { void F() { Log($"Error in OnViewActivated: {ex.Message}"); } }', encoding="utf-8")
            groups = core.locate_sources(core.triage_log(LOG), d)
            hits = {x["source_key"]: x["source_hits"] for x in groups}
            self.assertEqual(hits["Error in OnViewActivated"], ["Mod/App.cs:1"])
            self.assertEqual(hits["[AdaptiveRegion]"], [])


class VersionTests(unittest.TestCase):
    def test_candidate_version(self):
        self.assertEqual(core.candidate_version(["x", "development-tools-candidate-v3.2.21-ab12cd3"]), "3.2.21")
        self.assertIsNone(core.candidate_version(["nope"]))
        self.assertEqual(core.parse_version("v3.2.20"), (3, 2, 20))


if __name__ == "__main__":
    unittest.main()
