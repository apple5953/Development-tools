import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from validate import manifest_paths, validate_one

TEMPLATE = Path(__file__).parent / "template"

class ModuleValidationTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.folder = Path(self.tmp.name) / "example-agent"
        self.folder.mkdir()
        (self.folder / "ExampleAgent.csproj").write_text((TEMPLATE / "ExampleAgent.csproj").read_text(encoding="utf-8"), encoding="utf-8")
        self.data = json.loads((TEMPLATE / "module.json").read_text(encoding="utf-8"))

    def check(self):
        path = self.folder / "module.json"
        path.write_text(json.dumps(self.data), encoding="utf-8")
        return validate_one(path)

    def test_valid_template(self):
        self.check()

    def test_existing_ribbon_name_rejected(self):
        self.data["Ribbon"]["name"] = "DrawingFactory"
        with self.assertRaisesRegex(ValueError, "collision"):
            self.check()

    def test_bridge_permission_requires_separate_review(self):
        self.data["permissions"] = ["bridge-read"]
        with self.assertRaisesRegex(ValueError, "separate explicit"):
            self.check()

    def test_future_host_version_rejected(self):
        self.data["hostVersion"] = "99.0.0"
        with self.assertRaisesRegex(ValueError, "requires host"):
            self.check()

    def test_new_project_without_manifest_rejected(self):
        with patch("validate.MODULES", Path(self.tmp.name)):
            with self.assertRaisesRegex(ValueError, "requires module.json"):
                manifest_paths()

if __name__ == "__main__":
    unittest.main()
