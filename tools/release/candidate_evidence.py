#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import pathlib
import re
from typing import Any

ROOT = pathlib.Path(__file__).resolve().parents[2]
HASH = re.compile(r"^[0-9a-f]{64}$")
SHA = re.compile(r"^[0-9a-f]{40}$")


def read_properties() -> dict[str, str]:
    values: dict[str, str] = {}
    for raw in (ROOT / "release" / "version.properties").read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        key, value = line.split("=", 1)
        values[key] = value
    return values


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def workflow() -> dict[str, Any]:
    return {
        "repository": os.environ.get("GITHUB_REPOSITORY", "qbjsdsb/xueqing-native"),
        "run_id": os.environ.get("GITHUB_RUN_ID"),
        "run_attempt": int(os.environ.get("GITHUB_RUN_ATTEMPT", "1")),
    }


def base(source_sha: str, platform: str) -> dict[str, Any]:
    if not SHA.fullmatch(source_sha):
        raise ValueError("source SHA must be exact lowercase 40-hex")
    props = read_properties()
    return {
        "contract": "release_candidate_evidence_v1",
        "generated_at": dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z"),
        "source_commit": source_sha,
        "release_version": props["releaseVersion"],
        "platform": platform,
        "workflow": workflow(),
    }


def write_android(args: argparse.Namespace) -> None:
    props = read_properties()
    artifact = args.artifact.resolve(strict=True)
    certificate = args.certificate_sha256.lower()
    if not HASH.fullmatch(certificate):
        raise ValueError("Android certificate SHA-256 is invalid")

    value = base(args.source_sha, "android")
    value.update({
        "identity": {
            "application_id": props["androidApplicationId"],
            "version_code": int(props["androidVersionCode"]),
            "version_name": props["androidVersionName"],
        },
        "artifacts": [
            {"kind": "apk", "name": artifact.name, "sha256": sha256(artifact)},
        ],
        "signing": {
            "mode": "android-release-key",
            "certificate_sha256": certificate,
        },
    })
    write(value, args.output)


def write_windows(args: argparse.Namespace) -> None:
    props = read_properties()
    msix = args.msix.resolve(strict=True)
    setup = args.setup.resolve(strict=True)
    certificate = args.certificate_sha256.lower()
    if not HASH.fullmatch(certificate):
        raise ValueError("Windows certificate SHA-256 is invalid")
    if args.signing_mode not in {"signpath", "pfx"}:
        raise ValueError("Windows signing mode is invalid")

    value = base(args.source_sha, "windows")
    value.update({
        "identity": {
            "package_name": props["windowsPackageName"],
            "package_version": props["windowsPackageVersion"],
            "publisher": args.publisher,
            "architecture": props["windowsArchitecture"],
        },
        "artifacts": [
            {"kind": "msix", "name": msix.name, "sha256": sha256(msix)},
            {"kind": "setup", "name": setup.name, "sha256": sha256(setup)},
        ],
        "signing": {
            "mode": args.signing_mode,
            "certificate_sha256": certificate,
        },
    })
    write(value, args.output)


def write(value: dict[str, Any], output: pathlib.Path) -> None:
    verify(value)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def verify(value: Any) -> None:
    if not isinstance(value, dict) or set(value) != {
        "contract", "generated_at", "source_commit", "release_version", "platform",
        "workflow", "identity", "artifacts", "signing",
    }:
        raise ValueError("release evidence top-level shape is invalid")
    if value["contract"] != "release_candidate_evidence_v1":
        raise ValueError("release evidence contract is invalid")
    if not isinstance(value["source_commit"], str) or not SHA.fullmatch(value["source_commit"]):
        raise ValueError("release evidence source SHA is invalid")
    props = read_properties()
    if value["release_version"] != props["releaseVersion"]:
        raise ValueError("release evidence version does not match release/version.properties")
    if value["platform"] not in {"android", "windows"}:
        raise ValueError("release evidence platform is invalid")

    workflow_value = value["workflow"]
    if not isinstance(workflow_value, dict) or set(workflow_value) != {"repository", "run_id", "run_attempt"}:
        raise ValueError("release evidence workflow shape is invalid")
    if workflow_value["repository"] != "qbjsdsb/xueqing-native":
        raise ValueError("release evidence repository is invalid")
    if workflow_value["run_id"] is not None and not str(workflow_value["run_id"]).isdigit():
        raise ValueError("release evidence run_id is invalid")
    if not isinstance(workflow_value["run_attempt"], int) or workflow_value["run_attempt"] < 1:
        raise ValueError("release evidence run_attempt is invalid")

    artifacts = value["artifacts"]
    if not isinstance(artifacts, list):
        raise ValueError("release evidence artifacts are invalid")
    expected_kinds = {"apk"} if value["platform"] == "android" else {"msix", "setup"}
    kinds = set()
    for artifact in artifacts:
        if not isinstance(artifact, dict) or set(artifact) != {"kind", "name", "sha256"}:
            raise ValueError("release evidence artifact shape is invalid")
        if not isinstance(artifact["name"], str) or not artifact["name"] or "/" in artifact["name"] or "\\" in artifact["name"]:
            raise ValueError("release evidence artifact name is invalid")
        if not isinstance(artifact["sha256"], str) or not HASH.fullmatch(artifact["sha256"]):
            raise ValueError("release evidence artifact SHA-256 is invalid")
        kinds.add(artifact["kind"])
    if kinds != expected_kinds:
        raise ValueError("release evidence artifact kinds are invalid")

    signing = value["signing"]
    if not isinstance(signing, dict) or set(signing) != {"mode", "certificate_sha256"}:
        raise ValueError("release evidence signing shape is invalid")
    if not isinstance(signing["certificate_sha256"], str) or not HASH.fullmatch(signing["certificate_sha256"]):
        raise ValueError("release evidence certificate SHA-256 is invalid")

    identity = value["identity"]
    if value["platform"] == "android":
        if not isinstance(identity, dict) or set(identity) != {"application_id", "version_code", "version_name"}:
            raise ValueError("Android release identity evidence is invalid")
        if identity != {
            "application_id": props["androidApplicationId"],
            "version_code": int(props["androidVersionCode"]),
            "version_name": props["androidVersionName"],
        }:
            raise ValueError("Android evidence identity drift")
        if signing["mode"] != "android-release-key":
            raise ValueError("Android evidence signing mode is invalid")
    else:
        if not isinstance(identity, dict) or set(identity) != {"package_name", "package_version", "publisher", "architecture"}:
            raise ValueError("Windows release identity evidence is invalid")
        if identity["package_name"] != props["windowsPackageName"] or identity["package_version"] != props["windowsPackageVersion"]:
            raise ValueError("Windows evidence package identity drift")
        if identity["architecture"] != props["windowsArchitecture"] or not identity["publisher"]:
            raise ValueError("Windows evidence Publisher/architecture is invalid")
        if signing["mode"] not in {"signpath", "pfx"}:
            raise ValueError("Windows evidence signing mode is invalid")


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="platform", required=True)

    android = sub.add_parser("android")
    android.add_argument("--source-sha", required=True)
    android.add_argument("--artifact", type=pathlib.Path, required=True)
    android.add_argument("--certificate-sha256", required=True)
    android.add_argument("--output", type=pathlib.Path, required=True)
    android.set_defaults(handler=write_android)

    windows = sub.add_parser("windows")
    windows.add_argument("--source-sha", required=True)
    windows.add_argument("--msix", type=pathlib.Path, required=True)
    windows.add_argument("--setup", type=pathlib.Path, required=True)
    windows.add_argument("--publisher", required=True)
    windows.add_argument("--certificate-sha256", required=True)
    windows.add_argument("--signing-mode", required=True)
    windows.add_argument("--output", type=pathlib.Path, required=True)
    windows.set_defaults(handler=write_windows)

    verify_parser = sub.add_parser("verify")
    verify_parser.add_argument("--input", type=pathlib.Path, required=True)

    args = parser.parse_args()
    if args.platform == "verify":
        verify(json.loads(args.input.read_text(encoding="utf-8")))
        print("Release candidate evidence is valid.")
        return 0

    args.handler(args)
    print(f"Wrote release candidate evidence: {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
