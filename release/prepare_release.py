"""Validate a downloaded candidate and prepare a reviewed release-record change."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from datetime import datetime, timezone
from pathlib import Path


SEMVER_PATTERN = re.compile(r"^(\d+)\.(\d+)\.(\d+)$")


def parse_version(value: str, label: str) -> tuple[int, int, int]:
    match = SEMVER_PATTERN.fullmatch(value.strip())
    if match is None:
        raise ValueError(f"{label} must use numeric major.minor.patch format")
    return tuple(int(part) for part in match.groups())


def validate_promotion_version(current: str, candidate: str) -> None:
    current_version = parse_version(current, "repository latest_version")
    candidate_version = parse_version(candidate, "version")
    if candidate_version <= current_version:
        raise ValueError(
            f"candidate version {candidate} must be newer than stable version {current}"
        )


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--notes", required=True)
    args = parser.parse_args()

    if not args.run_id.isdigit():
        raise SystemExit("run ID must be numeric")

    root = args.root.resolve()
    candidate = args.candidate.resolve()
    current_manifest_path = root / "update_manifest.json"
    if not current_manifest_path.is_file():
        raise SystemExit("repository update_manifest.json is missing")
    current_manifest = json.loads(current_manifest_path.read_text(encoding="utf-8-sig"))
    try:
        validate_promotion_version(
            str(current_manifest.get("latest_version", "")), args.version
        )
    except ValueError as error:
        raise SystemExit(str(error)) from error
    metadata_paths = list(candidate.rglob("candidate-metadata.json"))
    manifest_paths = list(candidate.rglob("update_manifest.json"))
    if len(metadata_paths) != 1 or len(manifest_paths) != 1:
        raise SystemExit("candidate must contain exactly one metadata file and one update manifest")
    metadata = json.loads(metadata_paths[0].read_text(encoding="utf-8-sig"))
    if metadata.get("version") != args.version or str(metadata.get("runId")) != str(args.run_id):
        raise SystemExit("candidate version or run ID does not match promotion request")

    zip_paths = list(candidate.rglob(metadata["zipName"]))
    installer_paths = list(candidate.rglob(metadata["installerName"]))
    if len(zip_paths) != 1 or len(installer_paths) != 1:
        raise SystemExit("candidate ZIP or installer is missing/ambiguous")
    if sha256(zip_paths[0]) != metadata["zipSha256"]:
        raise SystemExit("candidate ZIP hash mismatch")
    if sha256(installer_paths[0]) != metadata["installerSha256"]:
        raise SystemExit("candidate installer hash mismatch")

    manifest = json.loads(manifest_paths[0].read_text(encoding="utf-8-sig"))
    if manifest.get("latest_version") != args.version or manifest.get("sha256") != metadata["zipSha256"]:
        raise SystemExit("candidate manifest does not match verified metadata")
    manifest["release_note"] = args.notes
    manifest["published_at"] = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    current_manifest_path.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )

    record = dict(metadata)
    record["releaseNotes"] = args.notes
    record["approvedAtUtc"] = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    records = root / "release" / "releases"
    records.mkdir(parents=True, exist_ok=True)
    record_path = records / f"v{args.version}.json"
    if record_path.exists():
        raise SystemExit(f"release record already exists: {record_path}")
    record_path.write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(record_path.relative_to(root).as_posix())


if __name__ == "__main__":
    main()
