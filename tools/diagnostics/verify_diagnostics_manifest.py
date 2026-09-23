#!/usr/bin/env python3
"""Strict verifier for the privacy-safe DIAGNOSTICS_MANIFEST_V1 contract."""

from __future__ import annotations

import datetime as dt
import json
import pathlib
import re
import sys

ROOT = {
    "contract", "generated_at", "platform", "app", "deployment", "runtime",
    "compatibility", "queues", "recent_error_codes", "archive_files",
}
PLATFORM = {"kind", "os_version", "architecture", "package_identity"}
APP = {"version", "source_commit", "client_contract_version", "local_schema_version"}
DEPLOYMENT = {"profile_id", "environment_id", "trust_domain_id", "provider_id"}
RUNTIME = {"session_state", "last_sync_category"}
COMPATIBILITY = {"state", "reason_code", "policy_revision"}
QUEUES = {"pending_intents", "outbox_items", "attachment_staging"}
ARCHIVE_FILE = {"name", "contract"}
ERROR = re.compile(r"^XQ_[A-Z0-9_]{3,64}$")
REVISION = re.compile(r"^[a-z0-9][a-z0-9._-]{1,63}$")
COMMIT = re.compile(r"^[0-9a-f]{40}$")


def exact(value, keys, label):
    if not isinstance(value, dict) or set(value) != keys:
        raise ValueError(f"{label} keys do not match diagnostics v1")
    return value


def bounded(value, label, maximum):
    if not isinstance(value, str) or not value.strip() or value != value.strip() or len(value) > maximum:
        raise ValueError(f"{label} is invalid")
    return value


def positive(value, label, allow_zero=False):
    minimum = 0 if allow_zero else 1
    if not isinstance(value, int) or isinstance(value, bool) or value < minimum:
        raise ValueError(f"{label} is invalid")
    return value


def verify(value):
    root = exact(value, ROOT, "manifest")
    if root["contract"] != "diagnostics_manifest_v1":
        raise ValueError("unsupported diagnostics contract")

    generated = root["generated_at"]
    if not isinstance(generated, str):
        raise ValueError("generated_at is invalid")
    parsed = dt.datetime.fromisoformat(generated.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("generated_at must be offset-aware")

    platform = exact(root["platform"], PLATFORM, "platform")
    if platform["kind"] not in {"android", "windows"}:
        raise ValueError("platform kind is invalid")
    bounded(platform["os_version"], "os_version", 128)
    bounded(platform["architecture"], "architecture", 32)
    bounded(platform["package_identity"], "package_identity", 160)

    app = exact(root["app"], APP, "app")
    bounded(app["version"], "app version", 64)
    if not isinstance(app["source_commit"], str) or not COMMIT.fullmatch(app["source_commit"]):
        raise ValueError("source_commit is invalid")
    positive(app["client_contract_version"], "client_contract_version")
    positive(app["local_schema_version"], "local_schema_version")

    deployment = exact(root["deployment"], DEPLOYMENT, "deployment")
    bounded(deployment["profile_id"], "profile_id", 64)
    bounded(deployment["environment_id"], "environment_id", 64)
    bounded(deployment["trust_domain_id"], "trust_domain_id", 96)
    bounded(deployment["provider_id"], "provider_id", 32)

    runtime = exact(root["runtime"], RUNTIME, "runtime")
    if runtime["session_state"] not in {
        "signed_out", "authenticated", "refresh_required",
        "revoked_or_invalid", "configuration_unavailable",
    }:
        raise ValueError("session_state is invalid")
    if runtime["last_sync_category"] not in {"never", "recent", "stale", "unknown"}:
        raise ValueError("last_sync_category is invalid")

    compatibility = exact(root["compatibility"], COMPATIBILITY, "compatibility")
    if compatibility["state"] not in {
        "supported", "update_recommended", "update_required",
        "security_blocked", "unknown",
    }:
        raise ValueError("compatibility state is invalid")
    reason = compatibility["reason_code"]
    if reason is not None and (not isinstance(reason, str) or not ERROR.fullmatch(reason)):
        raise ValueError("compatibility reason is invalid")
    revision = compatibility["policy_revision"]
    if revision is not None and (not isinstance(revision, str) or not REVISION.fullmatch(revision)):
        raise ValueError("compatibility revision is invalid")

    queues = exact(root["queues"], QUEUES, "queues")
    for key in QUEUES:
        if queues[key] is not None:
            positive(queues[key], key, allow_zero=True)

    errors = root["recent_error_codes"]
    if not isinstance(errors, list) or len(errors) > 20:
        raise ValueError("recent_error_codes is invalid")
    if any(not isinstance(code, str) or not ERROR.fullmatch(code) for code in errors):
        raise ValueError("recent_error_codes contains non-machine-readable data")

    files = root["archive_files"]
    if not isinstance(files, list) or not (1 <= len(files) <= 4):
        raise ValueError("archive_files is invalid")
    for item in files:
        item = exact(item, ARCHIVE_FILE, "archive file")
        if item != {"name": "diagnostics.json", "contract": "diagnostics_manifest_v1"}:
            raise ValueError("unexpected diagnostics archive file")


def main(argv):
    if len(argv) != 2:
        print("usage: verify_diagnostics_manifest.py <manifest.json>", file=sys.stderr)
        return 2
    verify(json.loads(pathlib.Path(argv[1]).read_text(encoding="utf-8")))
    print("Diagnostics manifest v1 is valid.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
