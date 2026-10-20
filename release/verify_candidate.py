"""Verify a no-publish release and write immutable candidate metadata."""

from __future__ import annotations

import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
from zipfile import ZipFile


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--version", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--artifact-name", required=True)
    args = parser.parse_args()

    root = args.root.resolve()
    dist = root / "dist"
    zip_path = dist / f"DevelopmentTools_v{args.version}.zip"
    installer_path = dist / "DevelopmentTools_Setup.exe"
    manifest_path = root / "update_manifest.json"
    for path in (zip_path, installer_path, manifest_path):
        if not path.is_file() or path.stat().st_size == 0:
            raise SystemExit(f"candidate output missing or empty: {path}")

    manifests = []
    for path in sorted((root / "DevelopmentTools.Modules").glob("*/module.json")):
        data = load_json(path)
        manifests.append(
            {
                "id": data["id"],
                "version": data["version"],
                "assembly": data["assembly"],
                "toolId": data["ToolId"],
            }
        )

    required = {
        "DevelopmentTools.Addin.dll",
        "DevelopmentTools.Updater.exe",
        "UpdaterPayload/DevelopmentTools.Updater.exe",
        "version.json",
        *(module["assembly"] for module in manifests),
    }
    with ZipFile(zip_path) as archive:
        names = {name.replace("\\", "/").lstrip("./") for name in archive.namelist()}
        missing = sorted(required - names)
        if missing:
            raise SystemExit("candidate ZIP is missing: " + ", ".join(missing))
        version_data = json.loads(archive.read("version.json").decode("utf-8-sig"))
        if version_data.get("current_version") != args.version:
            raise SystemExit("ZIP version.json does not match requested candidate version")

    zip_hash = sha256(zip_path)
    remote_manifest = load_json(manifest_path)
    if remote_manifest.get("latest_version") != args.version:
        raise SystemExit("update_manifest latest_version mismatch")
    if str(remote_manifest.get("sha256", "")).lower() != zip_hash:
        raise SystemExit("update_manifest SHA-256 mismatch")
    if not str(remote_manifest.get("release_url", "")).endswith(zip_path.name):
        raise SystemExit("update_manifest release URL does not point to candidate ZIP")

    metadata = {
        "schemaVersion": 1,
        "version": args.version,
        "sourceSha": args.source_sha,
        "runId": str(args.run_id),
        "artifactName": args.artifact_name,
        "zipName": zip_path.name,
        "zipSha256": zip_hash,
        "installerName": installer_path.name,
        "installerSha256": sha256(installer_path),
        "generatedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "revitVersions": ["2024"],
        "modules": manifests,
    }
    output = dist / "candidate-metadata.json"
    output.write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"verified candidate v{args.version}: {len(manifests)} module(s), {zip_hash}")


if __name__ == "__main__":
    main()
