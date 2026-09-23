#!/usr/bin/env python3
"""Strict verifier for CLIENT_COMPATIBILITY_V1 response fixtures."""

from __future__ import annotations

import datetime as dt
import json
import pathlib
import re
import sys
import urllib.parse

TOP_KEYS = {"contract", "generated_at_server", "policy_revision", "client", "decision"}
CLIENT_KEYS = {"platform", "app_version", "contract_version"}
DECISION_KEYS = {
    "state",
    "reason_code",
    "minimum_supported_app_version",
    "recommended_app_version",
    "minimum_supported_contract_version",
    "server_contract_version",
    "update_uri",
}
STATES = {"supported", "update_recommended", "update_required", "security_blocked"}
PLATFORMS = {"android", "windows"}
REVISION = re.compile(r"^[a-z0-9][a-z0-9._-]{1,63}$")
REASON = re.compile(r"^XQ_[A-Z0-9_]{3,64}$")


def _exact_keys(value: object, expected: set[str], label: str) -> dict:
    if not isinstance(value, dict) or set(value) != expected:
        raise ValueError(f"{label} keys do not match the v1 contract")
    return value


def _version(value: object, label: str) -> str:
    if not isinstance(value, str) or not (1 <= len(value) <= 64) or value.strip() != value:
        raise ValueError(f"{label} is invalid")
    return value


def _positive_int(value: object, label: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or not (1 <= value <= 2_147_483_647):
        raise ValueError(f"{label} is invalid")
    return value


def verify(value: object) -> None:
    root = _exact_keys(value, TOP_KEYS, "response")
    if root["contract"] != "client_compatibility_v1":
        raise ValueError("unsupported compatibility contract")

    generated = root["generated_at_server"]
    if not isinstance(generated, str):
        raise ValueError("generated_at_server is invalid")
    try:
        parsed = dt.datetime.fromisoformat(generated.replace("Z", "+00:00"))
    except ValueError as exc:
        raise ValueError("generated_at_server is invalid") from exc
    if parsed.tzinfo is None:
        raise ValueError("generated_at_server must be offset-aware")

    revision = root["policy_revision"]
    if not isinstance(revision, str) or REVISION.fullmatch(revision) is None:
        raise ValueError("policy_revision is invalid")

    client = _exact_keys(root["client"], CLIENT_KEYS, "client")
    if client["platform"] not in PLATFORMS:
        raise ValueError("client platform is invalid")
    _version(client["app_version"], "client app_version")
    _positive_int(client["contract_version"], "client contract_version")

    decision = _exact_keys(root["decision"], DECISION_KEYS, "decision")
    if decision["state"] not in STATES:
        raise ValueError("decision state is invalid")
    reason = decision["reason_code"]
    if not isinstance(reason, str) or REASON.fullmatch(reason) is None:
        raise ValueError("decision reason_code is invalid")

    _version(decision["minimum_supported_app_version"], "minimum supported app version")
    _version(decision["recommended_app_version"], "recommended app version")
    minimum_contract = _positive_int(
        decision["minimum_supported_contract_version"],
        "minimum supported contract version",
    )
    server_contract = _positive_int(
        decision["server_contract_version"],
        "server contract version",
    )
    if minimum_contract > server_contract:
        raise ValueError("minimum contract version cannot exceed server contract version")

    update_uri = decision["update_uri"]
    if update_uri is not None:
        if not isinstance(update_uri, str) or len(update_uri) > 2048:
            raise ValueError("update_uri is invalid")
        uri = urllib.parse.urlsplit(update_uri)
        if (
            uri.scheme != "https"
            or not uri.hostname
            or uri.username is not None
            or uri.password is not None
        ):
            raise ValueError("update_uri must be an HTTPS URI without user-info")


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print("usage: verify_client_compatibility.py <response.json>", file=sys.stderr)
        return 2
    value = json.loads(pathlib.Path(argv[1]).read_text(encoding="utf-8"))
    verify(value)
    print("Client compatibility v1 response is valid.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
