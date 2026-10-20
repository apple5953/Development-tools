"""Choose a monotonically increasing candidate version from stable and retained artifacts."""
import argparse
import json
import re
from pathlib import Path


CANDIDATE_PATTERN = re.compile(
    r"^development-tools-candidate-v(?P<version>\d+\.\d+\.\d+)-[0-9a-fA-F]+$"
)
SEMVER_PATTERN = re.compile(r"^(\d+)\.(\d+)\.(\d+)$")


def parse_version(value):
    match = SEMVER_PATTERN.fullmatch(value.strip())
    if not match:
        raise ValueError(f"invalid semantic version: {value}")
    return tuple(int(part) for part in match.groups())


def choose_next(stable_version, artifact_names):
    versions = [parse_version(stable_version)]
    for name in artifact_names:
        match = CANDIDATE_PATTERN.fullmatch(name.strip())
        if match:
            versions.append(parse_version(match.group("version")))
    major, minor, patch = max(versions)
    return f"{major}.{minor}.{patch + 1}"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--artifact-names-file", type=Path)
    parser.add_argument("--artifact-name", action="append", default=[])
    args = parser.parse_args()

    manifest = json.loads(args.manifest.read_text(encoding="utf-8-sig"))
    names = list(args.artifact_name)
    if args.artifact_names_file and args.artifact_names_file.exists():
        names.extend(args.artifact_names_file.read_text(encoding="utf-8-sig").splitlines())
    print(choose_next(manifest["latest_version"], names))


if __name__ == "__main__":
    main()
