"""Validate reviewed module folders without executing module code."""
import argparse
import json
import re
import sys
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
MODULES = ROOT / "DevelopmentTools.Modules"
SCHEMA = json.loads((Path(__file__).parent / "module.schema.json").read_text(encoding="utf-8"))
RESERVED_NAMES = set(re.findall(r'(?:Btn|Mcp|Menu)\("([A-Za-z0-9_]+)"|Name\s*=\s*"([A-Za-z0-9_]+)"', (ROOT / "DevelopmentTools.Addin/Core/RibbonCatalog.cs").read_text(encoding="utf-8")))
RESERVED_NAMES = {name for pair in RESERVED_NAMES for name in pair if name}
RESERVED_TOOLS = set()
for source in (ROOT / "DevelopmentTools.Addin").rglob("*.cs"):
    if "GeneratedModules" not in source.parts:
        RESERVED_TOOLS.update(re.findall(r'"(DT_[A-Za-z0-9_]+)"', source.read_text(encoding="utf-8-sig", errors="replace")))

LEGACY_MODULES = {"PerspectiveMinimap", "OpeningDetail", "DrawingFactory", "ProjectStandards"}

NAME_PATTERN = r"[A-Za-z][A-Za-z0-9_]*"
ENTRY_PATTERN = r"[A-Za-z_][A-Za-z0-9_.]*"
COLOR_PATTERN = r"#[0-9A-Fa-f]{6}"


def ribbon_tools(data):
    """Return explicit tools, or one compatible tool for a v1 manifest."""
    ribbon = data["Ribbon"]
    if "tools" in ribbon:
        return ribbon["tools"]
    return [{
        "name": ribbon["name"] + "Open",
        "text": ribbon["text"],
        "entryClass": data["entryClass"],
        "tooltip": "開啟" + ribbon["text"],
    }]

def manifest_paths():
    paths = sorted(MODULES.glob("*/module.json"))
    for project in MODULES.glob("*/*.csproj"):
        if project.parent.name not in LEGACY_MODULES and not (project.parent / "module.json").is_file():
            raise ValueError(f"{project}: new module project requires module.json")
    return paths

def validate_one(path):
    data = json.loads(path.read_text(encoding="utf-8"))
    missing = set(SCHEMA["required"]) - set(data)
    extra = set(data) - set(SCHEMA["properties"])
    if missing or extra:
        raise ValueError(f"{path}: missing={sorted(missing)} extra={sorted(extra)}")
    for key in ("id", "version", "hostVersion", "assembly", "entryClass", "ToolId"):
        pattern = SCHEMA["properties"][key]["pattern"]
        if not isinstance(data[key], str) or not re.fullmatch(pattern, data[key]):
            raise ValueError(f"{path}: invalid {key}")
    host_version = json.loads((ROOT / "DevelopmentTools.Addin/version.json").read_text(encoding="utf-8"))["current_version"]
    if tuple(map(int, data["hostVersion"].split("."))) > tuple(map(int, host_version.split("."))):
        raise ValueError(f"{path}: requires host {data['hostVersion']}, current is {host_version}")
    ribbon = data["Ribbon"]
    if not isinstance(ribbon, dict):
        raise ValueError(f"{path}: invalid Ribbon")
    allowed_ribbon = {"name", "text", "category", "icon", "color", "tools"}
    if (not {"name", "text", "category"}.issubset(ribbon)
            or not set(ribbon).issubset(allowed_ribbon)
            or not re.fullmatch(NAME_PATTERN, ribbon["name"])
            or not isinstance(ribbon["text"], str) or not ribbon["text"].strip()
            or ribbon["text"] != ribbon["text"].strip()
            or ribbon["category"] not in ("drawing", "model", "review")
            or ("icon" in ribbon and (not isinstance(ribbon["icon"], str) or not ribbon["icon"].strip() or len(ribbon["icon"]) > 8))
            or ("color" in ribbon and (not isinstance(ribbon["color"], str) or not re.fullmatch(COLOR_PATTERN, ribbon["color"])))):
        raise ValueError(f"{path}: invalid Ribbon")
    tools = ribbon_tools(data)
    if not isinstance(tools, list) or not tools or len(tools) > 20:
        raise ValueError(f"{path}: Ribbon.tools must contain 1 to 20 tools")
    tool_names = set()
    for tool in tools:
        allowed_tool = {"name", "text", "entryClass", "tooltip", "icon", "color"}
        if (not isinstance(tool, dict)
                or set(tool) - allowed_tool
                or not {"name", "text", "entryClass"}.issubset(tool)
                or not isinstance(tool["name"], str) or not re.fullmatch(NAME_PATTERN, tool["name"])
                or not isinstance(tool["text"], str) or not tool["text"].strip() or tool["text"] != tool["text"].strip()
                or not isinstance(tool["entryClass"], str) or not re.fullmatch(ENTRY_PATTERN, tool["entryClass"])
                or ("tooltip" in tool and (not isinstance(tool["tooltip"], str) or not tool["tooltip"].strip()))
                or ("icon" in tool and (not isinstance(tool["icon"], str) or not tool["icon"].strip() or len(tool["icon"]) > 8))
                or ("color" in tool and (not isinstance(tool["color"], str) or not re.fullmatch(COLOR_PATTERN, tool["color"])))):
            raise ValueError(f"{path}: invalid Ribbon tool")
        if tool["name"] in tool_names:
            raise ValueError(f"{path}: duplicate Ribbon tool name: {tool['name']}")
        if tool["name"] == ribbon["name"] or tool["name"] in RESERVED_NAMES:
            raise ValueError(f"{path}: existing Ribbon name or tool name collision")
        tool_names.add(tool["name"])
    if path.parent != Path(__file__).parent / "template" and data["id"] != path.parent.name:
        raise ValueError(f"{path}: id must equal folder name")
    if ribbon["name"] in RESERVED_NAMES or data["ToolId"] in RESERVED_TOOLS:
        raise ValueError(f"{path}: existing Ribbon name or ToolId collision")
    if not isinstance(data["RevitVersion"], list) or not data["RevitVersion"] or len(set(data["RevitVersion"])) != len(data["RevitVersion"]) or any(v not in ("2024", "2025", "2026") for v in data["RevitVersion"]):
        raise ValueError(f"{path}: invalid RevitVersion")
    if not isinstance(data["permissions"], list) or any(v not in ("ribbon-command", "bridge-read") for v in data["permissions"]) or len(set(data["permissions"])) != len(data["permissions"]):
        raise ValueError(f"{path}: invalid permissions")
    if "bridge-read" in data["permissions"]:
        raise ValueError(f"{path}: Bridge tools require separate explicit registration review")
    if not isinstance(data["dependencies"], list) or len(set(data["dependencies"])) != len(data["dependencies"]):
        raise ValueError(f"{path}: invalid dependencies")
    if any(not isinstance(dep, str) or not re.fullmatch(SCHEMA["properties"]["id"]["pattern"], dep) for dep in data["dependencies"]):
        raise ValueError(f"{path}: invalid dependency id")
    if not isinstance(data["tests"], list) or not data["tests"] or any(not isinstance(x, str) or not x.strip() for x in data["tests"]):
        raise ValueError(f"{path}: tests required")
    projects = list(path.parent.glob("*.csproj"))
    if len(projects) != 1:
        raise ValueError(f"{path}: exactly one csproj required")
    project = ET.parse(projects[0]).getroot()
    assembly = next((e.text for e in project.iter("AssemblyName") if e.text), projects[0].stem)
    if data["assembly"] != assembly + ".dll":
        raise ValueError(f"{path}: assembly mismatch")
    for ref in project.iter("ProjectReference"):
        target = (projects[0].parent / ref.attrib.get("Include", "")).resolve()
        if not target.is_relative_to(ROOT):
            raise ValueError(f"{path}: project reference escapes repository")
    return data, projects[0]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, help="validate one template or module manifest")
    args = parser.parse_args()
    paths = [args.manifest] if args.manifest else manifest_paths()
    seen_ids, seen_names, seen_texts, seen_tools, seen_item_names = set(), set(), set(), set(), set()
    modules = []
    for path in paths:
        data, project = validate_one(path.resolve())
        for key, seen, value in (("id", seen_ids, data["id"]), ("Ribbon name", seen_names, data["Ribbon"]["name"]), ("Ribbon text", seen_texts, data["Ribbon"]["text"]), ("ToolId", seen_tools, data["ToolId"])):
            if value in seen:
                raise ValueError(f"duplicate {key}: {value}")
            seen.add(value)
        for item_name in [data["Ribbon"]["name"]] + [tool["name"] for tool in ribbon_tools(data)]:
            if item_name in seen_item_names:
                raise ValueError(f"duplicate Ribbon item name: {item_name}")
            seen_item_names.add(item_name)
        modules.append((data, project))
    for data, _ in modules:
        if any(dep not in seen_ids or dep == data["id"] for dep in data["dependencies"]):
            raise ValueError(f"{data['id']}: invalid dependency")
    graph = {data["id"]: data["dependencies"] for data, _ in modules}
    visiting, done = set(), set()
    def visit(name):
        if name in visiting: raise ValueError("dependency cycle")
        if name in done: return
        visiting.add(name)
        for dep in graph[name]: visit(dep)
        visiting.remove(name); done.add(name)
    for name in graph: visit(name)
    print(f"validated {len(modules)} module manifest(s)")

if __name__ == "__main__":
    try: main()
    except (ValueError, OSError, json.JSONDecodeError) as exc:
        print(exc, file=sys.stderr); sys.exit(1)
