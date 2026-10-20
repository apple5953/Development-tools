import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from activate_agent_version import activate
from install_agent_bundle import install


class AgentInstallTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def bundle(self, version, content):
        root = self.root / ("bundle-" + version); package = root / "sample-agent" / "versions" / version
        package.mkdir(parents=True); exe = package / "Sample.exe"; exe.write_bytes(content)
        digest = hashlib.sha256(content).hexdigest()
        catalog = {"catalogVersion": 1, "agents": [{"id": "sample-agent", "version": version, "packagePath": f"sample-agent/versions/{version}", "files": [{"path": "Sample.exe", "sha256": digest}], "active": True}]}
        file = root / "approved-agents.json"; file.write_text(json.dumps(catalog), encoding="utf-8")
        return root, hashlib.sha256(file.read_bytes()).hexdigest()

    def test_install_upgrade_and_rollback(self):
        installed = self.root / "installed"
        first, first_hash = self.bundle("0.1.0", b"one"); install(first, installed, first_hash)
        second, second_hash = self.bundle("0.2.0", b"two"); install(second, installed, second_hash)
        catalog = json.loads((installed / "approved-agents.json").read_text(encoding="utf-8"))
        self.assertEqual(["0.2.0"], [a["version"] for a in catalog["agents"] if a["active"]])
        self.assertTrue((installed / "sample-agent/versions/0.1.0/Sample.exe").is_file())
        activate(installed, "sample-agent", "0.1.0")
        catalog = json.loads((installed / "approved-agents.json").read_text(encoding="utf-8"))
        self.assertEqual(["0.1.0"], [a["version"] for a in catalog["agents"] if a["active"]])

    def test_catalog_hash_required(self):
        bundle, _ = self.bundle("0.1.0", b"one")
        with self.assertRaisesRegex(ValueError, "catalog SHA-256 mismatch"):
            install(bundle, self.root / "installed", "0" * 64)


if __name__ == "__main__": unittest.main()
