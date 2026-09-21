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

    def test_checked_in_manifest_remains_blocked_only_until_runtime_region_probe(self) -> None:
        value = self.blocked
        self.assertTrue(
            value["hosted_environment_observation"]["native_production_project_provisioned"]
        )
        self.assertTrue(
            value["hosted_environment_observation"]["production_project_capacity_resolved"]
        )
        self.assertTrue(value["data_surfaces"]["postgres"]["accepted"])
        self.assertTrue(value["data_surfaces"]["auth"]["accepted"])
        self.assertTrue(value["data_surfaces"]["storage"]["accepted"])
        self.assertTrue(value["data_surfaces"]["logs_diagnostics"]["accepted"])
        self.assertTrue(value["data_surfaces"]["backups"]["accepted"])
        self.assertEqual(
            ["hosted_edge_runtime_region_not_independently_probed"],
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
        value["hosted_environment_observation"]["production_project_capacity_resolved"] = True
        value["production_credentials"]["accepted"] = True
        value["data_surfaces"]["edge_invitation_delivery"]["function_deployed"] = True
        value["data_surfaces"]["edge_invitation_delivery"]["required_region_policy_source"] = "fictional-checked-in-region-policy"
        value["data_surfaces"]["edge_invitation_delivery"]["runtime_region_evidence"] = "fictional-runtime-region-evidence"

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

        value["data_surfaces"]["postgres"]["at_rest_region"] = "ap-shanghai"
        value["data_surfaces"]["auth"]["at_rest_region"] = "ap-shanghai"
        value["data_surfaces"]["storage"]["metadata_region"] = "ap-shanghai"
        value["data_surfaces"]["storage"]["object_origin_region"] = "ap-shanghai"

        value["data_surfaces"]["edge_invitation_delivery"]["production_execution_region"] = "ap-shanghai"
        value["data_surfaces"]["edge_invitation_delivery"]["supported_regions_snapshot"].append(
            "ap-shanghai"
        )
        verifier.validate(value, True, REPO_ROOT)

    def test_broad_region_label_is_not_an_exact_region_id(self) -> None:
        def mutate(value: dict) -> None:
            # Isolate the provider-neutral region-id grammar from the checked-in
            # Supabase deployment-policy binding tested separately below.
            value["provider_candidate"] = "fictional-provider"
            value["primary_project_region"]["region"] = "apac"

        self.assert_rejected(
            mutate,
            "exact provider region identifier",
        )

    def test_acceptance_requires_concrete_edge_region_policy(self) -> None:
        self.assert_rejected(
            lambda value: value["data_surfaces"]["edge_invitation_delivery"].__setitem__(
                "required_region_policy_source",
                "pending",
            ),
            "concrete server-side required-region policy",
        )

    def test_supabase_manifest_region_must_match_checked_in_deployment_policy(self) -> None:
        def mutate(value: dict) -> None:
            region = "ap-northeast-1"
            value["primary_project_region"]["region"] = region
            value["data_surfaces"]["postgres"]["at_rest_region"] = region
            value["data_surfaces"]["auth"]["at_rest_region"] = region
            value["data_surfaces"]["storage"]["metadata_region"] = region
            value["data_surfaces"]["storage"]["object_origin_region"] = region
            value["data_surfaces"]["edge_invitation_delivery"]["production_execution_region"] = region

        self.assert_rejected(
            mutate,
            "checked-in Supabase hosted region policy must match accepted primary/Edge region",
        )

    def test_acceptance_requires_concrete_edge_runtime_region_evidence(self) -> None:
        self.assert_rejected(
            lambda value: value["data_surfaces"]["edge_invitation_delivery"].__setitem__(
                "runtime_region_evidence",
                "pending-required-region-secret",
            ),
            "concrete Edge runtime-region evidence",
        )

    def test_acceptance_requires_resolved_production_project_capacity(self) -> None:
        self.assert_rejected(
            lambda value: value["hosted_environment_observation"].__setitem__(
                "production_project_capacity_resolved",
                False,
            ),
            "resolved production-project capacity",
        )

    def test_acceptance_requires_concrete_primary_region_evidence(self) -> None:
        self.assert_rejected(
            lambda value: value["primary_project_region"].__setitem__(
                "evidence", "provisioned-project-evidence-required"
            ),
            "concrete production-project region evidence",
        )

    def test_acceptance_rejects_checked_in_policy_placeholders_as_evidence(self) -> None:
        value = copy.deepcopy(self.blocked)
        value["status"] = "accepted"
        value["blockers"] = []
        value["primary_project_region"]["evidence"] = "provisioned-project-evidence-required"
        value["data_surfaces"]["edge_invitation_delivery"]["runtime_region_evidence"] = (
            "fictional-runtime-region-evidence"
        )
        for surface in value["data_surfaces"].values():
            surface["accepted"] = True

        with self.assertRaisesRegex(
            ValueError,
            "concrete production-project region evidence",
        ):
            verifier.validate(value, True, REPO_ROOT)

    def test_acceptance_rejects_pending_backup_destination_text(self) -> None:
        self.assert_rejected(
            lambda value: value["data_surfaces"]["backups"].__setitem__(
                "backup_residency",
                "non-mainland-private-encrypted-destination-pending-backup-restore-gate",
            ),
            "concrete backup-residency evidence",
        )

    def test_acceptance_rejects_required_only_backup_mechanism_text(self) -> None:
        self.assert_rejected(
            lambda value: value["data_surfaces"]["backups"].__setitem__(
                "database_backup",
                "independent-encrypted-logical-backup-required-outside-live-project",
            ),
            "concrete database backup mechanism",
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
