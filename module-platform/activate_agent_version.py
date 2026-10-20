"""Atomically activate a previously installed, hash-verified Agent version."""
import argparse
import json
import os
import sys
import uuid
from pathlib import Path

from install_agent_bundle import verify_entry


def activate(root, agent_id, version):
    root = root.resolve(); catalog_file = root / "approved-agents.json"
    catalog = json.loads(catalog_file.read_text(encoding="utf-8"))
    matches = [a for a in catalog.get("agents", []) if a["id"] == agent_id and a["version"] == version]
    if len(matches) != 1: raise ValueError("requested installed agent version not found")
    verify_entry(root, matches[0])
    for entry in catalog["agents"]:
        if entry["id"] == agent_id: entry["active"] = entry["version"] == version
    temporary = root / ("approved-agents." + uuid.uuid4().hex + ".tmp")
    temporary.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.replace(str(temporary), str(catalog_file))
    print(f"activated {agent_id} {version}; reload Agent Center to apply")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--install-root", type=Path, required=True)
    parser.add_argument("--id", required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    try: activate(args.install_root, args.id, args.version)
    except Exception as exc: print(exc, file=sys.stderr); sys.exit(1)
