"""Validate hot-swappable worker folders without executing worker code."""
import json
import re
import sys
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
WORKERS = ROOT / "DevelopmentTools.AgentWorkers"
SCHEMA = json.loads((Path(__file__).parent / "agent-worker.schema.json").read_text(encoding="utf-8"))
HOST_VERSION = json.loads((ROOT / "DevelopmentTools.Addin/version.json").read_text(encoding="utf-8"))["current_version"]
RESERVED_TOOL_IDS = set()
for module_manifest in (ROOT / "DevelopmentTools.Modules").glob("*/module.json"):
    try: RESERVED_TOOL_IDS.add(json.loads(module_manifest.read_text(encoding="utf-8"))["ToolId"])
    except (OSError, KeyError, json.JSONDecodeError): pass


def manifest_paths():
    return sorted(WORKERS.glob("*/agent.worker.json")) if WORKERS.exists() else []


def validate_one(path):
    path = Path(path).resolve()
    data = json.loads(path.read_text(encoding="utf-8"))
    missing = set(SCHEMA["required"]) - set(data)
    extra = set(data) - set(SCHEMA["properties"])
    if missing or extra:
        raise ValueError(f"{path}: missing={sorted(missing)} extra={sorted(extra)}")
    if data["schemaVersion"] != 1:
        raise ValueError(f"{path}: unsupported schemaVersion")
    for key in ("id", "version", "hostVersion", "executable", "toolId"):
        pattern = SCHEMA["properties"][key]["pattern"]
        if not isinstance(data[key], str) or not re.fullmatch(pattern, data[key]):
            raise ValueError(f"{path}: invalid {key}")
    if tuple(map(int, data["hostVersion"].split("."))) > tuple(map(int, HOST_VERSION.split("."))):
        raise ValueError(f"{path}: requires future host {data['hostVersion']}")
    if data["toolId"] in RESERVED_TOOL_IDS:
        raise ValueError(f"{path}: ToolId collides with a static module")
    if data["id"] != path.parent.name:
        raise ValueError(f"{path}: id must equal folder name")
    if not isinstance(data["revitVersions"], list) or not data["revitVersions"] or any(v not in ("2024", "2025", "2026") for v in data["revitVersions"]):
        raise ValueError(f"{path}: invalid revitVersions")
    if len(set(data["revitVersions"])) != len(data["revitVersions"]):
        raise ValueError(f"{path}: duplicate revitVersions")
    if not isinstance(data["permissions"], list) or data["permissions"] != ["document-context-read"]:
        raise ValueError(f"{path}: only document-context-read is supported")
    if not isinstance(data["timeoutSeconds"], int) or not 1 <= data["timeoutSeconds"] <= 120:
        raise ValueError(f"{path}: timeoutSeconds must be 1..120")
    if not isinstance(data["displayName"], str) or not data["displayName"].strip() or not isinstance(data["description"], str) or not data["description"].strip():
        raise ValueError(f"{path}: displayName, description and tests are required")
    if not isinstance(data["tests"], list) or not data["tests"] or any(not isinstance(test, str) or not test.strip() for test in data["tests"]):
        raise ValueError(f"{path}: tests must contain non-empty strings")
    projects = list(path.parent.glob("*.csproj"))
    if len(projects) != 1:
        raise ValueError(f"{path}: exactly one csproj required")
    project = ET.parse(projects[0]).getroot()
    assembly = next((e.text for e in project.iter("AssemblyName") if e.text), projects[0].stem)
    if data["executable"] != assembly + ".exe":
        raise ValueError(f"{path}: executable must match project AssemblyName")
    text = projects[0].read_text(encoding="utf-8-sig", errors="replace")
    if re.search(r"RevitAPI(?:UI)?", text, re.IGNORECASE):
        raise ValueError(f"{path}: hot workers must not reference Revit API")
    return data, projects[0]


def main():
    seen_ids, seen_tools = set(), set()
    paths = manifest_paths()
    for path in paths:
        data, _ = validate_one(path)
        if data["id"] in seen_ids or data["toolId"] in seen_tools:
            raise ValueError(f"{path}: duplicate id or toolId")
        seen_ids.add(data["id"]); seen_tools.add(data["toolId"])
    print(f"validated {len(paths)} hot agent worker manifest(s)")


if __name__ == "__main__":
    try: main()
    except (ValueError, OSError, json.JSONDecodeError, ET.ParseError) as exc:
        print(exc, file=sys.stderr); sys.exit(1)
