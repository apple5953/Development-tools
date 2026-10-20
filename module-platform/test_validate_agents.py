import json
import tempfile
import unittest
from pathlib import Path

from validate_agents import validate_one


class HotAgentValidationTests(unittest.TestCase):
    def make(self, **changes):
        root = Path(self.temp.name) / "sample-agent"; root.mkdir()
        data = {"schemaVersion": 1, "id": "sample-agent", "version": "0.1.0", "hostVersion": "3.0.0", "revitVersions": ["2024"], "executable": "SampleAgent.exe", "toolId": "DT_SampleAgent", "displayName": "Sample", "description": "Read-only sample", "permissions": ["document-context-read"], "timeoutSeconds": 30, "tests": ["protocol"]}
        data.update(changes)
        (root / "agent.worker.json").write_text(json.dumps(data), encoding="utf-8")
        (root / "SampleAgent.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><AssemblyName>SampleAgent</AssemblyName></PropertyGroup></Project>', encoding="utf-8")
        return root / "agent.worker.json"

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)

    def test_valid(self): validate_one(self.make())

    def test_revit_reference_rejected(self):
        path = self.make(); project = path.parent / "SampleAgent.csproj"
        project.write_text(project.read_text().replace('</Project>', '<ItemGroup><Reference Include="RevitAPI" /></ItemGroup></Project>'), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "must not reference Revit API"): validate_one(path)

    def test_write_permission_rejected(self):
        with self.assertRaisesRegex(ValueError, "only document-context-read"): validate_one(self.make(permissions=["model-write"]))


if __name__ == "__main__": unittest.main()
