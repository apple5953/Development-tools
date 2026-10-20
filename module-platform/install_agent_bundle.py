"""Atomically install a trusted Agent bundle without deleting rollback versions."""
import argparse
import hashlib
import json
import os
import shutil
import sys
import uuid
from pathlib import Path


def sha256(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""): h.update(chunk)
    return h.hexdigest()


def safe_child(root, relative):
    root = root.resolve(); path = (root / relative).resolve()
    if path == root or root not in path.parents: raise ValueError(f"path escapes root: {relative}")
    return path


def verify_entry(bundle, entry):
    package = safe_child(bundle, entry["packagePath"])
    expected = {}
    for item in entry.get("files", []):
        name = item["path"]
        if Path(name).name != name or name in expected: raise ValueError("invalid approved file name")
        expected[name] = item["sha256"].lower()
    actual = {p.name for p in package.iterdir() if p.is_file()}
    if actual != set(expected): raise ValueError(f"package file mismatch: {entry['id']} {entry['version']}")
    for name, digest in expected.items():
        if len(digest) != 64 or sha256(package / name) != digest: raise ValueError(f"hash mismatch: {name}")
    return package


def install(bundle, install_root, expected_catalog_hash):
    bundle = bundle.resolve(); install_root = install_root.resolve()
    catalog_file = bundle / "approved-agents.json"
    if sha256(catalog_file) != expected_catalog_hash.lower(): raise ValueError("trusted catalog SHA-256 mismatch")
    incoming = json.loads(catalog_file.read_text(encoding="utf-8"))
    if incoming.get("catalogVersion") != 1: raise ValueError("unsupported catalog")
    install_root.mkdir(parents=True, exist_ok=True)
    existing_file = install_root / "approved-agents.json"
    existing = json.loads(existing_file.read_text(encoding="utf-8")) if existing_file.exists() else {"catalogVersion": 1, "agents": []}
    merged = {(a["id"], a["version"]): a for a in existing.get("agents", [])}
    for entry in incoming.get("agents", []):
        source = verify_entry(bundle, entry)
        for old in merged.values():
            if old["id"] == entry["id"]: old["active"] = False
        destination = safe_child(install_root, entry["packagePath"])
        if destination.exists():
            verify_entry(install_root, entry)
        else:
            destination.parent.mkdir(parents=True, exist_ok=True)
            staging = destination.parent / (".incoming-" + uuid.uuid4().hex)
            shutil.copytree(source, staging)
            os.replace(str(staging), str(destination))
        installed = dict(entry); installed["active"] = True
        merged[(entry["id"], entry["version"])] = installed
    result = {"catalogVersion": 1, "agents": sorted(merged.values(), key=lambda a: (a["id"], a["version"]))}
    temporary = install_root / ("approved-agents." + uuid.uuid4().hex + ".tmp")
    temporary.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.replace(str(temporary), str(existing_file))
    print(f"installed {len(incoming.get('agents', []))} agent(s); previous versions retained for rollback")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--install-root", type=Path, required=True)
    parser.add_argument("--expected-catalog-sha256", required=True)
    args = parser.parse_args()
    try: install(args.bundle, args.install_root, args.expected_catalog_sha256)
    except Exception as exc: print(exc, file=sys.stderr); sys.exit(1)
