#!/usr/bin/env python3
"""Validate Xueqing public production deployment profiles with no third-party deps."""

from __future__ import annotations

import base64
import ipaddress
import json
import pathlib
import re
import sys
from urllib.parse import urlsplit

TOP_KEYS = {
    "contract",
    "profile_id",
    "environment_id",
    "trust_domain_id",
    "provider_id",
    "project_origin",
    "publishable_key",
    "required_edge_region",
    "capabilities",
    "source",
}
SOURCE_KEYS = {"repository", "commit"}
ALLOWED_CAPABILITIES = {
    "auth",
    "database-rpc",
    "edge-functions",
    "private-storage",
}
ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{1,95}$")
REGION_RE = re.compile(r"^[a-z]{2}-[a-z0-9-]+-[0-9]+$")
COMMIT_RE = re.compile(r"^[0-9a-f]{40}$")
SECRET_VALUE_RE = re.compile(
    r"(?:sb_secret_|service[_-]?role|postgres(?:ql)?://|database[_-]?password|"
    r"backup[_-]?(?:key|password|passphrase))",
    re.IGNORECASE,
)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def exact_keys(value: dict, expected: set[str], path: str) -> None:
    require(set(value) == expected, f"{path} keys mismatch")


def identifier(value: object, path: str, max_length: int = 96) -> str:
    require(isinstance(value, str), f"{path} must be a string")
    require(2 <= len(value) <= max_length, f"{path} length invalid")
    require(ID_RE.fullmatch(value) is not None, f"{path} format invalid")
    return value


def validate_origin(value: object) -> None:
    require(isinstance(value, str), "project_origin must be a string")
    parsed = urlsplit(value)
    require(parsed.scheme == "https", "project_origin must use HTTPS")
    require(parsed.hostname is not None, "project_origin hostname missing")
    require(parsed.username is None and parsed.password is None, "project_origin must not contain user info")
    require(parsed.path == "", "project_origin must be an origin without a path or trailing slash")
    require(parsed.query == "" and parsed.fragment == "", "project_origin must not contain query or fragment")
    require(parsed.port in (None, 443), "project_origin production port must be 443")
    host = parsed.hostname.lower()
    require(host not in {"localhost", "localhost.localdomain"}, "project_origin must not be loopback")
    require(not host.endswith((".local", ".internal", ".localhost")), "project_origin uses local-only hostname")
    try:
        address = ipaddress.ip_address(host)
    except ValueError:
        address = None
    require(address is None, "project_origin must use a production DNS hostname, not an IP literal")


def decode_jwt_payload(value: str) -> dict | None:
    parts = value.split(".")
    if len(parts) != 3:
        return None
    try:
        payload = parts[1] + "=" * (-len(parts[1]) % 4)
        decoded = base64.urlsafe_b64decode(payload.encode("ascii"))
        parsed = json.loads(decoded.decode("utf-8"))
        return parsed if isinstance(parsed, dict) else None
    except Exception:
        return None


def validate_publishable_key(value: object) -> None:
    require(isinstance(value, str), "publishable_key must be a string")
    require(16 <= len(value) <= 2048, "publishable_key length invalid")
    require(value.strip() == value and not any(ch.isspace() for ch in value), "publishable_key contains whitespace")
    require(SECRET_VALUE_RE.search(value) is None, "publishable_key looks like privileged/secret material")
    payload = decode_jwt_payload(value)
    if payload is not None:
        role = payload.get("role")
        require(role != "service_role", "service-role JWT must never be shipped as publishable_key")


def validate_profile(value: object) -> None:
    require(isinstance(value, dict), "deployment profile must be an object")
    exact_keys(value, TOP_KEYS, "profile")
    require(value["contract"] == "deployment_profile_v1", "unsupported deployment profile contract")
    identifier(value["profile_id"], "profile_id", 64)
    identifier(value["environment_id"], "environment_id", 64)
    identifier(value["trust_domain_id"], "trust_domain_id", 96)
    identifier(value["provider_id"], "provider_id", 32)
    validate_origin(value["project_origin"])
    validate_publishable_key(value["publishable_key"])

    region = value["required_edge_region"]
    require(isinstance(region, str) and REGION_RE.fullmatch(region) is not None, "required_edge_region invalid")

    capabilities = value["capabilities"]
    require(isinstance(capabilities, list) and capabilities, "capabilities must be a non-empty array")
    require(all(isinstance(item, str) for item in capabilities), "capabilities must contain strings")
    require(len(capabilities) == len(set(capabilities)), "capabilities must be unique")
    require(capabilities == sorted(capabilities), "capabilities must be sorted for deterministic provenance")
    require(set(capabilities) <= ALLOWED_CAPABILITIES, "unsupported capability")

    source = value["source"]
    require(isinstance(source, dict), "source must be an object")
    exact_keys(source, SOURCE_KEYS, "source")
    require(source["repository"] == "qbjsdsb/xueqing-native", "source.repository must bind to this repository")
    require(isinstance(source["commit"], str) and COMMIT_RE.fullmatch(source["commit"]) is not None, "source.commit must be a 40-char lowercase SHA")


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: verify_deployment_profile.py PROFILE.json")
    path = pathlib.Path(sys.argv[1])
    value = json.loads(path.read_text(encoding="utf-8"))
    try:
        validate_profile(value)
    except ValueError as exc:
        raise SystemExit(f"invalid deployment profile: {exc}") from exc
    print("deployment profile valid: v1")


if __name__ == "__main__":
    main()
