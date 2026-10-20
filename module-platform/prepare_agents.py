"""Stage reviewed workers and generate the SHA-256 catalog consumed by Revit."""
import argparse
import hashlib
import json
import shutil
import sys
from pathlib import Path

from validate_agents import ROOT, manifest_paths, validate_one


def sha256(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def stage(configuration, output):
    entries = []
    output.mkdir(parents=True, exist_ok=True)
    for manifest in manifest_paths():
        data, project = validate_one(manifest)
        source = project.parent / "bin" / configuration
        executable = source / data["executable"]
        if not executable.is_file():
            raise ValueError(f"worker output missing: {executable}")
        package_rel = Path(data["id"]) / "versions" / data["version"]
        package = output / package_rel
        if package.exists(): shutil.rmtree(package)
        package.mkdir(parents=True)
        shutil.copy2(manifest, package / "agent.worker.json")
        for item in sorted(source.iterdir()):
            if item.is_file() and item.suffix.lower() not in (".pdb", ".xml"):
                shutil.copy2(item, package / item.name)
        files = []
        for item in sorted(package.iterdir()):
            if item.is_file(): files.append({"path": item.name, "sha256": sha256(item)})
        entry = dict(data)
        entry.update({"packagePath": package_rel.as_posix(), "files": files, "active": True})
        entries.append(entry)
    catalog = {"catalogVersion": 1, "agents": sorted(entries, key=lambda x: (x["id"], x["version"]))}
    (output / "approved-agents.json").write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"staged {len(entries)} approved hot agent worker(s) at {output}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--configuration", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try: stage(args.configuration, args.output.resolve())
    except Exception as exc: print(exc, file=sys.stderr); sys.exit(1)
