#!/usr/bin/env python3
"""Verify the exact public deployment profile embedded in a native release artifact."""

from __future__ import annotations

import hashlib
import json
import pathlib
import re
import sys
import zipfile

from verify_deployment_profile import validate_profile

EXPECTED_PROFILE_ID = "xueqing-prod-sg-v1"
EXPECTED_ENVIRONMENT_ID = "production-sg-v1"
EXPECTED_TRUST_DOMAIN_ID = "xueqing-native-prod-sg"
EXPECTED_EDGE_REGION = "ap-southeast-1"
EXPECTED_REPOSITORY = "qbjsdsb/xueqing-native"

FORBIDDEN_TEXT_MARKERS = (
    b"sb_" + b"secret" + b"_",
    b"service" + b"_role",
    b"-----BEGIN PRIVATE KEY-----",
    b"-----BEGIN RSA PRIVATE KEY-----",
)
FORBIDDEN_ARCHIVE_SUFFIXES = (
    ".pem",
    ".pfx",
    ".p12",
    ".jks",
    ".keystore",
)

# A database URI scheme is not credential material by itself and can
# legitimately occur in runtime/diagnostic libraries. Reject only a URI that
# embeds a username and password before '@'. Build markers in parts so this
# verifier does not trip the repository's own privileged-material grep.
_DATABASE_SCHEME_MARKERS = (
    b"postgres" + b"://",
    b"postgres" + b"ql://",
)


def contains_database_credentials(value: bytes) -> bool:
    lowered = value.lower()
    for scheme in _DATABASE_SCHEME_MARKERS:
        cursor = 0
        while True:
            index = lowered.find(scheme, cursor)
            if index < 0:
                break

            remainder = lowered[index + len(scheme):index + len(scheme) + 1024]
            authority_end = len(remainder)
            for delimiter in (b"/", b"?", b"#", b" ", bytes([9]), bytes([10]), bytes([13]), bytes([0])):
                position = remainder.find(delimiter)
                if position >= 0:
                    authority_end = min(authority_end, position)

            authority = remainder[:authority_end]
            if b"@" in authority:
                user_info = authority.split(b"@", 1)[0]
                if b":" in user_info:
                    username, password = user_info.split(b":", 1)
                    if username and password:
                        return True

            cursor = index + len(scheme)

    return False


def expected_member(kind: str) -> str:
    if kind == "windows-msix":
        return "deployment_profile.json"
    if kind == "android-apk":
        return "assets/deployment_profile.json"
    raise ValueError(f"unsupported artifact kind: {kind}")


def verify_artifact(
    artifact_path: pathlib.Path,
    kind: str,
    source_commit: str,
) -> tuple[dict, str]:
    require_commit(source_commit)
    member = expected_member(kind)

    with zipfile.ZipFile(artifact_path) as archive:
        names = archive.namelist()
        if names.count(member) != 1:
            raise ValueError(
                f"expected exactly one {member!r} in artifact; found {names.count(member)}"
            )

        for name in (value.lower() for value in names):
            if name.endswith(FORBIDDEN_ARCHIVE_SUFFIXES):
                raise ValueError(f"forbidden credential container in artifact: {name}")

        payload = archive.read(member)
        profile = json.loads(payload.decode("utf-8"))
        validate_profile(profile)

        if profile["profile_id"] != EXPECTED_PROFILE_ID:
            raise ValueError("artifact deployment profile id mismatch")
        if profile["environment_id"] != EXPECTED_ENVIRONMENT_ID:
            raise ValueError("artifact deployment environment mismatch")
        if profile["trust_domain_id"] != EXPECTED_TRUST_DOMAIN_ID:
            raise ValueError("artifact deployment trust-domain mismatch")
        if profile["required_edge_region"] != EXPECTED_EDGE_REGION:
            raise ValueError("artifact Edge region mismatch")
        if profile["source"]["repository"] != EXPECTED_REPOSITORY:
            raise ValueError("artifact source repository mismatch")
        if profile["source"]["commit"] != source_commit:
            raise ValueError("artifact source commit does not match exact build head")

        scan_for_privileged_material(archive)

    return profile, hashlib.sha256(payload).hexdigest()


def scan_for_privileged_material(archive: zipfile.ZipFile) -> None:
    # Keep enough overlap to catch fixed markers and a bounded database URI
    # split across adjacent chunks.
    tail_limit = max(max(map(len, FORBIDDEN_TEXT_MARKERS)) - 1, 2048)

    for info in archive.infolist():
        if info.is_dir():
            continue

        tail = b""
        with archive.open(info) as stream:
            while True:
                chunk = stream.read(1024 * 1024)
                if not chunk:
                    break

                candidate = tail + chunk
                lowered = candidate.lower()

                for marker in FORBIDDEN_TEXT_MARKERS:
                    if marker.lower() in lowered:
                        raise ValueError(
                            "forbidden privileged material marker found in "
                            f"artifact entry: {info.filename}"
                        )

                if contains_database_credentials(candidate):
                    raise ValueError(
                        "credential-bearing database URI found in "
                        f"artifact entry: {info.filename}"
                    )

                tail = candidate[-tail_limit:]


def require_commit(value: str) -> None:
    if re.fullmatch(r"[0-9a-f]{40}", value) is None:
        raise ValueError("source commit must be a 40-char lowercase SHA")


def main() -> None:
    if len(sys.argv) != 4:
        raise SystemExit(
            "usage: verify_native_artifact_profile.py ARTIFACT KIND SOURCE_COMMIT"
        )

    artifact = pathlib.Path(sys.argv[1])
    kind = sys.argv[2]
    source_commit = sys.argv[3]

    if not artifact.is_file():
        raise SystemExit(f"artifact not found: {artifact}")

    try:
        profile, digest = verify_artifact(artifact, kind, source_commit)
    except (ValueError, zipfile.BadZipFile, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise SystemExit(f"native artifact profile verification failed: {exc}") from exc

    print(
        "native artifact profile valid: "
        f"kind={kind} source={profile['source']['commit']} sha256={digest}"
    )


if __name__ == "__main__":
    main()
