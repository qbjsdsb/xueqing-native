#!/usr/bin/env python3
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path, PurePosixPath
import re
import sys
import uuid

SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
COMMIT_RE = re.compile(r"^[0-9a-f]{40}$")
REGION_RE = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+){1,4}$")
SECRET_KEY_RE = re.compile(
    r"(?:service[_-]?role|secret[_-]?key|database[_-]?password|db[_-]?password|"
    r"access[_-]?token|refresh[_-]?token|s3[_-]?(?:access|secret)|backup[_-]?credential)",
    re.IGNORECASE,
)
SECRET_VALUE_RE = re.compile(
    r"(?:sb_secret_[A-Za-z0-9_-]+|postgres(?:ql)?://[^\s]+:[^\s]+@|"
    r"eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,})"
)

ROOT_KEYS = {
    "schema_version", "archive_id", "created_at", "source",
    "consistency", "database", "storage", "security",
}
SOURCE_KEYS = {
    "provider_id", "environment_id", "region", "source_commit",
    "migration_count", "migrations", "schema_fingerprint_sha256",
}
CONSISTENCY_KEYS = {
    "strategy", "boundary_id", "database_started_at", "database_finished_at",
    "objects_started_at", "objects_finished_at",
    "maximum_data_loss_window_seconds",
}
DATABASE_KEYS = {
    "format", "tool", "tool_version", "archive_relative_path",
    "byte_length", "sha256", "row_counts",
}
STORAGE_KEYS = {"bucket_id", "private", "object_count", "objects"}
OBJECT_KEYS = {
    "logical_object_locator", "archive_relative_path",
    "byte_length", "mime_type", "sha256",
}
SECURITY_KEYS = {
    "archive_encrypted", "encryption_scheme",
    "key_stored_separately", "production_material_in_public_ci",
}
ALLOWED_MIME = {"image/jpeg", "image/png", "image/webp"}
ALLOWED_STRATEGIES = {"quiesced-window", "deterministic-reconciliation"}


def fail(message: str) -> None:
    raise ValueError(message)


def require(condition: bool, message: str) -> None:
    if not condition:
        fail(message)


def object_value(value: object, name: str) -> dict:
    require(isinstance(value, dict), f"{name} must be an object")
    return value


def exact_keys(value: dict, expected: set[str], name: str) -> None:
    missing = expected - set(value)
    extra = set(value) - expected
    require(not missing, f"{name} missing keys: {sorted(missing)}")
    require(not extra, f"{name} contains unknown keys: {sorted(extra)}")


def nonempty_string(value: object, name: str) -> str:
    require(isinstance(value, str) and bool(value.strip()), f"{name} must be a non-empty string")
    return value.strip()


def positive_int(value: object, name: str, *, allow_zero: bool = False) -> int:
    require(isinstance(value, int) and not isinstance(value, bool), f"{name} must be an integer")
    require(value >= (0 if allow_zero else 1), f"{name} is out of range")
    return value


def parse_utc(value: object, name: str) -> datetime:
    text = nonempty_string(value, name)
    require(text.endswith("Z"), f"{name} must be UTC and end with Z")
    try:
        parsed = datetime.fromisoformat(text[:-1] + "+00:00")
    except ValueError as exc:
        raise ValueError(f"{name} must be RFC3339 UTC") from exc
    require(parsed.tzinfo is not None, f"{name} must include timezone")
    return parsed.astimezone(timezone.utc)


def sha256(value: object, name: str) -> str:
    text = nonempty_string(value, name)
    require(SHA256_RE.fullmatch(text) is not None, f"{name} must be lowercase SHA-256 hex")
    return text


def safe_relative_path(value: object, name: str) -> str:
    text = nonempty_string(value, name).replace("\\", "/")
    path = PurePosixPath(text)
    require(not path.is_absolute(), f"{name} must be relative")
    require(".." not in path.parts and "." not in path.parts, f"{name} must not traverse")
    require(all(part not in {"", "."} for part in path.parts), f"{name} contains empty path component")
    return text


def reject_secret_material(value: object, path: str = "$") -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            require(SECRET_KEY_RE.search(str(key)) is None, f"{path}.{key} is a forbidden secret-like field")
            reject_secret_material(child, f"{path}.{key}")
    elif isinstance(value, list):
        for index, child in enumerate(value):
            reject_secret_material(child, f"{path}[{index}]")
    elif isinstance(value, str):
        require(SECRET_VALUE_RE.search(value) is None, f"{path} contains secret-like material")


def validate(value: dict) -> None:
    reject_secret_material(value)
    exact_keys(value, ROOT_KEYS, "manifest")

    require(value["schema_version"] == 1, "schema_version must be 1")
    try:
        uuid.UUID(nonempty_string(value["archive_id"], "archive_id"))
    except ValueError as exc:
        raise ValueError("archive_id must be a UUID") from exc
    parse_utc(value["created_at"], "created_at")

    source = object_value(value["source"], "source")
    exact_keys(source, SOURCE_KEYS, "source")
    nonempty_string(source["provider_id"], "source.provider_id")
    nonempty_string(source["environment_id"], "source.environment_id")
    region = nonempty_string(source["region"], "source.region")
    require(REGION_RE.fullmatch(region) is not None, "source.region must be an exact provider region")
    commit = nonempty_string(source["source_commit"], "source.source_commit")
    require(COMMIT_RE.fullmatch(commit) is not None, "source.source_commit must be a 40-char lowercase commit SHA")
    migrations = source["migrations"]
    require(isinstance(migrations, list) and migrations, "source.migrations must be a non-empty array")
    require(all(isinstance(item, str) and item.strip() for item in migrations), "source.migrations contains invalid item")
    require(len(migrations) == len(set(migrations)), "source.migrations must be unique")
    count = positive_int(source["migration_count"], "source.migration_count")
    require(count == len(migrations), "source.migration_count must match migrations length")
    sha256(source["schema_fingerprint_sha256"], "source.schema_fingerprint_sha256")

    consistency = object_value(value["consistency"], "consistency")
    exact_keys(consistency, CONSISTENCY_KEYS, "consistency")
    require(consistency["strategy"] in ALLOWED_STRATEGIES, "unsupported consistency.strategy")
    nonempty_string(consistency["boundary_id"], "consistency.boundary_id")
    db_start = parse_utc(consistency["database_started_at"], "consistency.database_started_at")
    db_finish = parse_utc(consistency["database_finished_at"], "consistency.database_finished_at")
    object_start = parse_utc(consistency["objects_started_at"], "consistency.objects_started_at")
    object_finish = parse_utc(consistency["objects_finished_at"], "consistency.objects_finished_at")
    require(db_start <= db_finish, "database backup finish precedes start")
    require(object_start <= object_finish, "object backup finish precedes start")
    positive_int(
        consistency["maximum_data_loss_window_seconds"],
        "consistency.maximum_data_loss_window_seconds",
        allow_zero=True,
    )

    database = object_value(value["database"], "database")
    exact_keys(database, DATABASE_KEYS, "database")
    require(database["format"] == "postgresql-custom", "database.format must be postgresql-custom")
    require(database["tool"] == "pg_dump", "database.tool must be pg_dump")
    nonempty_string(database["tool_version"], "database.tool_version")
    safe_relative_path(database["archive_relative_path"], "database.archive_relative_path")
    positive_int(database["byte_length"], "database.byte_length")
    sha256(database["sha256"], "database.sha256")
    row_counts = object_value(database["row_counts"], "database.row_counts")
    for table, count_value in row_counts.items():
        nonempty_string(table, "database.row_counts table")
        positive_int(count_value, f"database.row_counts.{table}", allow_zero=True)

    storage = object_value(value["storage"], "storage")
    exact_keys(storage, STORAGE_KEYS, "storage")
    require(storage["bucket_id"] == "teaching-attachments-v1", "unexpected Storage bucket")
    require(storage["private"] is True, "Storage bucket must remain private")
    objects = storage["objects"]
    require(isinstance(objects, list), "storage.objects must be an array")
    object_count = positive_int(storage["object_count"], "storage.object_count", allow_zero=True)
    require(object_count == len(objects), "storage.object_count must match objects length")

    locators: set[str] = set()
    archive_paths: set[str] = set()
    for index, raw in enumerate(objects):
        item = object_value(raw, f"storage.objects[{index}]")
        exact_keys(item, OBJECT_KEYS, f"storage.objects[{index}]")
        locator = nonempty_string(item["logical_object_locator"], f"storage.objects[{index}].logical_object_locator")
        path = safe_relative_path(item["archive_relative_path"], f"storage.objects[{index}].archive_relative_path")
        require(locator not in locators, "duplicate logical_object_locator")
        require(path not in archive_paths, "duplicate object archive_relative_path")
        locators.add(locator)
        archive_paths.add(path)
        positive_int(item["byte_length"], f"storage.objects[{index}].byte_length")
        require(item["mime_type"] in ALLOWED_MIME, f"storage.objects[{index}].mime_type is not allowed")
        sha256(item["sha256"], f"storage.objects[{index}].sha256")

    security = object_value(value["security"], "security")
    exact_keys(security, SECURITY_KEYS, "security")
    require(security["archive_encrypted"] is True, "backup archive must be encrypted")
    nonempty_string(security["encryption_scheme"], "security.encryption_scheme")
    require(security["key_stored_separately"] is True, "backup decryption key must be stored separately")
    require(
        security["production_material_in_public_ci"] is False,
        "production backup material must not be placed in public CI",
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    args = parser.parse_args()
    try:
        value = json.loads(args.manifest.read_text(encoding="utf-8"))
        require(isinstance(value, dict), "manifest root must be an object")
        validate(value)
    except (OSError, json.JSONDecodeError, ValueError) as exc:
        print(f"backup manifest validation failed: {exc}", file=sys.stderr)
        return 1

    print("backup manifest valid: v1")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
