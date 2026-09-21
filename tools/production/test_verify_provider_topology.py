#!/usr/bin/env python3
import copy
import importlib.util
import json
from pathlib import Path
import unittest

REPO_ROOT = Path(__file__).resolve().parents[2]
VERIFIER_PATH = REPO_ROOT / "tools" / "production" / "verify_provider_topology.py"
MANIFEST_PATH = REPO_ROOT / "docs" / "production" / "PROVIDER_TOPOLOGY.json"

spec = importlib.util.spec_from_file_location("verify_provider_topology", VERIFIER_PATH)
assert spec is not None and spec.loader is not None
verifier = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verifier)


class ProviderTopologyVerifierTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.blocked = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))

    def test_checked_in_manifest_records_selected_supabase_singapore_policy(self) -> None:
        value = self.blocked
        self.assertEqual("blocked", value["status"])
        self.assertEqual("supabase-hosted", value["provider_candidate"])
        self.assertEqual(
            "non-mainland-acceptable-singapore-selected",
            value["target_jurisdiction"],
        )
        self.assertEqual(
            "ap-southeast-1",
            value["primary_project_region"]["region"],
        )
        self.assertEqual(
            "ap-southeast-1",
            value["data_surfaces"]["edge_invitation_delivery"]["production_execution_region"],
        )
        self.assertEqual(
            "accepted-for-v1",
            value["data_surfaces"]["edge_invitation_delivery"]["global_gateway_transit"],
        )
        self.assertIn(
            "global-cdn-accepted-for-v1",
            value["data_surfaces"]["storage"]["edge_cache_scope"],
        )

    def test_checked_in_manifest_remains_blocked_until_runtime_and_backup_evidence_exist(self) -> None:
        value = self.blocked
        self.assertFalse(
            value["hosted_environment_observation"]["native_production_project_provisioned"]
        )
        self.assertIn(
            "native_production_project_not_provisioned_and_region_not_verified",
            value["blockers"],
        )
        self.assertIn(
            "independent_database_and_private_storage_backup_not_proven",
            value["blockers"],
        )
        with self.assertRaisesRegex(ValueError, "production topology is still blocked"):
            verifier.validate(value, True, REPO_ROOT)

    def accepted_manifest(self) -> dict:
        value = copy.deepcopy(self.blocked)
        value["status"] = "accepted"
        value["blockers"] = []
        value["target_jurisdiction"] = "fictional-test-jurisdiction"
        value["provider_candidate"] = "supabase-hosted"

        primary = value["primary_project_region"]
        primary["region"] = "ap-southeast-1"
        primary["evidence"] = "fictional-provisioned-project-region-evidence"

        value["hosted_environment_observation"]["native_production_project_provisioned"] = True
        value["production_credentials"]["accepted"] = True

        for surface in value["data_surfaces"].values():
            surface["accepted"] = True

        edge = value["data_surfaces"]["edge_invitation_delivery"]
        edge["production_execution_region"] = "ap-southeast-1"
        edge["global_gateway_transit"] = "explicitly-accepted-for-fictional-test"

        logs = value["data_surfaces"]["logs_diagnostics"]
        logs["provider_log_residency"] = "fictional-provider-log-region-evidence"

        backup = value["data_surfaces"]["backups"]
        backup["database_backup"] = "fictional-logical-database-backup"
        backup["backup_residency"] = "fictional-backup-region-evidence"
        return value

    def assert_rejected(self, mutate, expected: str) -> None:
        value = self.accepted_manifest()
        mutate(value)
        with self.assertRaisesRegex(ValueError, expected):
            verifier.validate(value, True, REPO_ROOT)

    def test_fictional_complete_acceptance_fixture_passes(self) -> None:
        verifier.validate(self.accepted_manifest(), True, REPO_ROOT)

    def test_provider_neutral_exact_region_id_accepts_cloudbase_style(self) -> None:
        value = self.accepted_manifest()
        value["provider_candidate"] = "tencent-cloudbase"
        value["primary_project_region"]["region"] = "ap-shanghai"
        value["primary_project_region"]["supported_regions_snapshot"].append("ap-shanghai")
        value["data_surfaces"]["edge_invitation_delivery"]["production_execution_region"] = "ap-shanghai"
        value["data_surfaces"]["edge_invitation_delivery"]["supported_regions_snapshot"].append(
            "ap-shanghai"
        )
        verifier.validate(value, True, REPO_ROOT)

    def test_broad_region_label_is_not_an_exact_region_id(self) -> None:
        self.assert_rejected(
            lambda value: value["primary_project_region"].__setitem__("region", "apac"),
            "exact provider region identifier",
        )

    def test_acceptance_requires_concrete_primary_region_evidence(self) -> None:
        self.assert_rejected(
            lambda value: value["primary_project_region"].__setitem__(
                "evidence", "provisioned-project-evidence-required"
            ),
            "concrete production-project region evidence",
        )

    def test_acceptance_requires_credential_boundary_acceptance(self) -> None:
        self.assert_rejected(
            lambda value: value["production_credentials"].__setitem__("accepted", False),
            "credential boundary acceptance",
        )

    def test_acceptance_rejects_unbound_attachment_origin(self) -> None:
        self.assert_rejected(
            lambda value: value["data_surfaces"]["storage"].__setitem__(
                "object_origin_region", "unknown"
            ),
            "bind private Attachment origin",
        )

    def test_acceptance_requires_explicit_attachment_cache_policy(self) -> None:
        self.assert_rejected(
            lambda value: value["data_surfaces"]["storage"].__setitem__(
                "edge_cache_scope", "unresolved"
            ),
            "CDN/cache policy",
        )

    def test_acceptance_requires_concrete_log_residency(self) -> None:
        self.assert_rejected(
            lambda value: value["data_surfaces"]["logs_diagnostics"].__setitem__(
                "provider_log_residency", "unresolved"
            ),
            "concrete provider log-residency evidence",
        )

    def test_acceptance_rejects_plan_dependent_backup_placeholder(self) -> None:
        self.assert_rejected(
            lambda value: value["data_surfaces"]["backups"].__setitem__(
                "database_backup", "provider-plan-dependent"
            ),
            "concrete database backup mechanism",
        )

    def test_acceptance_requires_concrete_backup_residency(self) -> None:
        self.assert_rejected(
            lambda value: value["data_surfaces"]["backups"].__setitem__(
                "backup_residency", "unknown"
            ),
            "concrete backup-residency evidence",
        )


if __name__ == "__main__":
    unittest.main()
