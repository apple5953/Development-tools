"""Derive portable Obfuscar input and owned-DLL lists from reviewed module folders."""
import json
import subprocess
import sys
from pathlib import Path, PureWindowsPath
from xml.etree import ElementTree as ET

from validate import ROOT, manifest_paths, validate_one

ADDIN = ROOT / "DevelopmentTools.Addin"
LEGACY_OWNED = {
    "DevelopmentTools.Addin.dll",
    "RevitPerspectiveMinimap.Core.dll",
    "OpeningDetailCore.dll",
    "BIMDrawingFactory.UI.dll",
    "DevelopmentTools.ProjectStandards.dll",
}

def prepare():
    subprocess.run([sys.executable, str(Path(__file__).with_name("validate.py"))], check=True)
    release_dir = ADDIN / "bin/Release/net48"
    tree = ET.parse(ADDIN / "obfuscar.xml")
    root = tree.getroot()
    for variable in root.findall("Var"):
        if variable.get("name") == "InPath": variable.set("value", str(release_dir))
        if variable.get("name") == "OutPath": variable.set("value", str(release_dir / "Obfuscated"))
    owned = set(LEGACY_OWNED)
    for module in root.findall("Module"):
        module.set("file", str(release_dir / PureWindowsPath(module.get("file")).name))
    for path in manifest_paths():
        data, _ = validate_one(path)
        dll = data["assembly"]
        if dll in owned:
            raise ValueError(f"owned DLL collision: {dll}")
        owned.add(dll)
        module = ET.SubElement(root, "Module", {"file": str(release_dir / dll)})
        ET.SubElement(module, "SkipType", {"name": "*", "skipProperties": "true", "skipEvents": "true"})
        ET.SubElement(module, "SkipEnums", {"value": "true"})
    configured = {Path(module.get("file")).name for module in root.findall("Module")}
    if configured != owned:
        raise ValueError(f"Obfuscar coverage mismatch: missing={owned-configured}, extra={configured-owned}")
    out = ADDIN / "obj/Release/module-platform"
    out.mkdir(parents=True, exist_ok=True)
    tree.write(out / "obfuscar.generated.xml", encoding="utf-8", xml_declaration=True)
    (out / "owned-dlls.json").write_text(json.dumps(sorted(owned), indent=2) + "\n", encoding="utf-8")
    (out / "owned-dlls.iss").write_text('#define OwnedDllExcludes "' + ','.join(sorted(owned)) + '"\n', encoding="utf-8")
    (out / "owned-files.iss").write_text(''.join(
        f'Source: "..\\..\\DevelopmentTools.Addin\\bin\\Release\\net48\\Obfuscated\\{dll}"; DestDir: "{{app}}\\App"; Flags: ignoreversion\n'
        for dll in sorted(owned)), encoding="utf-8")
    print(f"prepared {len(owned)} owned DLLs")

if __name__ == "__main__":
    try: prepare()
    except Exception as exc: print(exc, file=sys.stderr); sys.exit(1)
