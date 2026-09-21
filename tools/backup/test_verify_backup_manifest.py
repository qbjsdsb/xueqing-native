#!/usr/bin/env python3
import copy
import importlib.util
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
VALIDATOR_PATH = ROOT / "tools" / "backup" / "verify_backup_manifest.py"
FIXTURE_PATH = ROOT / "tools" / "backup" / "fixtures" / "valid_backup_manifest.json"

spec = importlib.util.spec_from_file_location("verify_backup_manifest", VALIDATOR_PATH)
assert spec is not None and spec.loader is not None
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


class BackupManifestValidatorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.valid = json.loads(FIXTURE_PATH.read_text(encoding="utf-8"))

    def assert_rejected(self, mutate, expected: str) -> None:
        value = copy.deepcopy(self.valid)
        mutate(value)
        with self.assertRaisesRegex(ValueError, expected):
            validator.validate(value)

    def test_valid_fixture_passes(self) -> None:
        validator.validate(copy.deepcopy(self.valid))

    def test_object_count_must_match(self) -> None:
        self.assert_rejected(
            lambda value: value["storage"].__setitem__("object_count", 2),
            "object_count must match",
        )

    def test_duplicate_locator_rejected(self) -> None:
        def mutate(value: dict) -> None:
            duplicate = copy.deepcopy(value["storage"]["objects"][0])
            duplicate["archive_relative_path"] = "objects/second.jpg"
            value["storage"]["objects"].append(duplicate)
            value["storage"]["object_count"] = 2

        self.assert_rejected(mutate, "duplicate logical_object_locator")

    def test_path_traversal_rejected(self) -> None:
        self.assert_rejected(
            lambda value: value["database"].__setitem__(
                "archive_relative_path", "../production.dump"
            ),
            "must not traverse",
        )

    def test_private_bucket_cannot_be_relaxed(self) -> None:
        self.assert_rejected(
            lambda value: value["storage"].__setitem__("private", False),
            "must remain private",
        )

    def test_digest_must_be_sha256(self) -> None:
        self.assert_rejected(
            lambda value: value["storage"]["objects"][0].__setitem__(
                "sha256", "not-a-digest"
            ),
            "must be lowercase SHA-256 hex",
        )

    def test_table_fingerprint_keys_must_match_row_counts(self) -> None:
        self.assert_rejected(
            lambda value: value["database"]["table_fingerprints_sha256"].pop("app_users"),
            "must cover exactly",
        )

    def test_table_fingerprint_must_be_sha256(self) -> None:
        self.assert_rejected(
            lambda value: value["database"]["table_fingerprints_sha256"].__setitem__(
                "app_users", "bad"
            ),
            "must be lowercase SHA-256 hex",
        )

    def test_migration_count_must_match(self) -> None:
        self.assert_rejected(
            lambda value: value["source"].__setitem__("migration_count", 13),
            "migration_count must match",
        )

    def test_timestamp_order_must_be_valid(self) -> None:
        self.assert_rejected(
            lambda value: value["consistency"].__setitem__(
                "database_finished_at", "2026-09-21T12:38:59Z"
            ),
            "finish precedes start",
        )

    def test_archive_must_be_encrypted(self) -> None:
        self.assert_rejected(
            lambda value: value["security"].__setitem__("archive_encrypted", False),
            "archive must be encrypted",
        )

    def test_key_must_be_separate(self) -> None:
        self.assert_rejected(
            lambda value: value["security"].__setitem__("key_stored_separately", False),
            "key must be stored separately",
        )

    def test_public_ci_production_material_is_forbidden(self) -> None:
        self.assert_rejected(
            lambda value: value["security"].__setitem__(
                "production_material_in_public_ci", True
            ),
            "must not be placed in public CI",
        )

    def test_secret_like_field_is_rejected(self) -> None:
        def mutate(value: dict) -> None:
            value["security"]["database_password"] = "fictional"

        self.assert_rejected(mutate, "forbidden secret-like field")

    def test_secret_like_value_is_rejected(self) -> None:
        self.assert_rejected(
            lambda value: value["source"].__setitem__(
                "environment_id",
                "postgresql://user:password@example.invalid/db",
            ),
            "contains secret-like material",
        )


if __name__ == "__main__":
    unittest.main()
