#!/usr/bin/env python3
import argparse
import json
from pathlib import Path
import re
import sys

REGION_RE = re.compile(r"^[a-z]{2}(?:-gov)?-[a-z]+-\d+$")


def fail(message: str) -> None:
    raise ValueError(message)


def require(condition: bool, message: str) -> None:
    if not condition:
        fail(message)


def load(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    require(isinstance(value, dict), "topology root must be an object")
    return value


def validate_repository_evidence(value: dict, repo_root: Path) -> None:
    evidence = value.get("repository_evidence")
    require(isinstance(evidence, dict), "repository_evidence must be an object")

    migration = repo_root / str(evidence.get("private_attachment_bucket_migration", ""))
    require(migration.is_file(), "private Attachment bucket migration evidence is missing")
    migration_text = migration.read_text(encoding="utf-8")
    require(
        "'teaching-attachments-v1'" in migration_text
        and "set public = false" in migration_text
        and re.search(r"['\"]teaching-attachments-v1['\"][\s\S]{0,160}?false", migration_text),
        "Attachment bucket must remain explicitly private",
    )

    edge_adapter = repo_root / str(evidence.get("invitation_delivery_server_adapter", ""))
    require(edge_adapter.is_file(), "invitation Delivery server adapter evidence is missing")
    edge_text = edge_adapter.read_text(encoding="utf-8")
    require(
        "SUPABASE_SECRET_KEYS" in edge_text or "SUPABASE_SECRET_KEY" in edge_text,
        "trusted Delivery adapter must keep provider secret access server-side",
    )
    require(
        "XUEQING_REQUIRED_EDGE_REGION" in edge_text
        and "SB_REGION" in edge_text
        and "enforceRequiredExecutionRegion();" in edge_text,
        "trusted Delivery adapter must support server-enforced execution region",
    )
    require(
        edge_text.index("enforceRequiredExecutionRegion();")
        < edge_text.index("const accessToken = requiredBearer(request);"),
        "execution-region guard must run before auth/body/business processing",
    )

    roots = evidence.get("production_client_source_roots")
    require(isinstance(roots, list) and roots, "production client source roots are required")
    forbidden = re.compile(
        r"SUPABASE_(?:SERVICE_ROLE|SECRET)(?:_KEYS?|_KEY)?|SERVICE_ROLE_KEY|service_role",
        re.IGNORECASE,
    )
    offenders: list[str] = []
    suffixes = {".cs", ".kt", ".kts", ".java", ".xml", ".json", ".properties"}
    for root_value in roots:
        root = repo_root / str(root_value)
        require(root.is_dir(), f"client source root is missing: {root_value}")
        for path in root.rglob("*"):
            if not path.is_file() or path.suffix.lower() not in suffixes:
                continue
            try:
                text = path.read_text(encoding="utf-8")
            except UnicodeDecodeError:
                continue
            if forbidden.search(text):
                offenders.append(str(path.relative_to(repo_root)))

    require(
        offenders == [],
        "production client source contains provider admin/secret identifiers: "
        + ", ".join(offenders),
    )


def validate(value: dict, require_accepted: bool, repo_root: Path) -> None:
    require(value.get("schema_version") == 1, "schema_version must be 1")
    require(
        value.get("gate") == "production-provider-region-data-residency",
        "unexpected gate identifier",
    )
    status = value.get("status")
    require(status in {"blocked", "accepted"}, "status must be blocked or accepted")

    surfaces = value.get("data_surfaces")
    require(isinstance(surfaces, dict), "data_surfaces must be an object")
    for key in (
        "postgres",
        "auth",
        "storage",
        "edge_invitation_delivery",
        "logs_diagnostics",
        "backups",
    ):
        require(isinstance(surfaces.get(key), dict), f"missing data surface: {key}")

    creds = value.get("production_credentials")
    require(isinstance(creds, dict), "production_credentials must be an object")
    require(
        creds.get("client_secret_keys_allowed") is False,
        "client secret keys must remain forbidden",
    )
    require(
        creds.get("public_repository_secret_keys_allowed") is False,
        "public repository secret keys must remain forbidden",
    )
    require(
        creds.get("public_ci_production_secrets_allowed") is False,
        "public CI production secrets must remain forbidden",
    )

    storage = surfaces["storage"]
    require(storage.get("bucket_model") == "private", "Attachment Storage must remain private")
    require(
        storage.get("signed_urls_used_by_xueqing_v1") is False,
        "V1 evidence must not silently introduce signed URL delivery",
    )

    backups = surfaces["backups"]
    require(
        backups.get("storage_objects_in_database_backup") is False,
        "database backup must not be treated as Storage object backup",
    )
    require(
        backups.get("independent_storage_object_backup_required") is True,
        "independent Storage object backup requirement must remain explicit",
    )

    hosted = value.get("hosted_environment_observation")
    require(isinstance(hosted, dict), "hosted_environment_observation must be an object")
    require(
        isinstance(hosted.get("native_production_project_provisioned"), bool),
        "native production project provisioning state must be explicit",
    )

    constraints = value.get("deployment_constraints")
    require(isinstance(constraints, dict), "deployment_constraints must be an object")
    require(
        constraints.get("zero_paid_dependency_target") is True,
        "zero-paid dependency target must remain explicit",
    )
    require(
        constraints.get("hosted_free_can_be_sole_production_durability_layer") is False,
        "hosted Free must not be treated as Xueqing's sole durability layer",
    )

    blockers = value.get("blockers")
    require(isinstance(blockers, list), "blockers must be an array")
    require(all(isinstance(item, str) and item for item in blockers), "blockers must be strings")

    references = value.get("references")
    require(
        isinstance(references, list) and references,
        "provider evidence references must be recorded",
    )

    validate_repository_evidence(value, repo_root)

    if not require_accepted:
        return

    require(status == "accepted", "production topology is still blocked")
    require(blockers == [], "accepted topology cannot retain blockers")

    jurisdiction = value.get("target_jurisdiction")
    require(isinstance(jurisdiction, str) and jurisdiction.strip(), "target jurisdiction is required")

    provider_candidate = value.get("provider_candidate")
    require(
        isinstance(provider_candidate, str) and provider_candidate.strip(),
        "accepted topology requires an explicit provider candidate",
    )

    primary = value.get("primary_project_region")
    require(isinstance(primary, dict), "primary_project_region must be an object")
    region = primary.get("region")
    require(
        isinstance(region, str) and REGION_RE.match(region) is not None,
        "accepted topology requires an exact provider region identifier",
    )
    supported_primary = primary.get("supported_regions_snapshot")
    require(
        isinstance(supported_primary, list) and region in supported_primary,
        "accepted topology region is not in the checked provider region snapshot",
    )
    require(
        hosted.get("native_production_project_provisioned") is True,
        "accepted topology requires a provisioned native production project",
    )
    primary_evidence = primary.get("evidence")
    require(
        isinstance(primary_evidence, str)
        and primary_evidence.strip()
        and primary_evidence
        not in {"provisioned-project-evidence-required", "unknown", "unresolved"},
        "accepted topology requires concrete production-project region evidence",
    )
    require(
        creds.get("accepted") is True,
        "accepted topology requires production credential boundary acceptance",
    )

    for key in (
        "postgres",
        "auth",
        "storage",
        "edge_invitation_delivery",
        "logs_diagnostics",
        "backups",
    ):
        require(
            surfaces[key].get("accepted") is True,
            f"accepted topology requires data surface acceptance: {key}",
        )

    postgres_region = surfaces["postgres"].get("at_rest_region")
    require(
        postgres_region in {region, "inherits-primary-project-region"},
        "accepted topology must bind PostgreSQL at-rest location to the primary region",
    )

    auth_region = surfaces["auth"].get("at_rest_region")
    require(
        auth_region in {region, "inherits-primary-project-region", "project-postgres-auth-schema"},
        "accepted topology must bind Auth at-rest location to the accepted project region",
    )

    storage = surfaces["storage"]
    storage_origin = storage.get("object_origin_region")
    require(
        storage_origin in {region, "inherits-primary-project-region"},
        "accepted topology must bind private Attachment origin to the primary region",
    )
    storage_cache = storage.get("edge_cache_scope")
    require(
        isinstance(storage_cache, str)
        and storage_cache.strip()
        and storage_cache not in {"unknown", "unresolved"},
        "accepted topology requires an explicit private Attachment CDN/cache policy",
    )

    edge = surfaces["edge_invitation_delivery"]
    edge_region = edge.get("production_execution_region")
    require(
        isinstance(edge_region, str) and edge_region,
        "accepted topology requires explicit Edge execution-region policy",
    )
    supported_edge = edge.get("supported_regions_snapshot")
    require(
        isinstance(supported_edge, list) and edge_region in supported_edge,
        "accepted Edge execution region is not in the checked regional-invocation snapshot",
    )
    if edge.get("same_region_with_primary_required") is True:
        require(
            edge_region == region,
            "Invitation Delivery execution region must match primary project region",
        )
    require(
        edge.get("global_gateway_transit") not in {None, "unresolved"},
        "accepted topology requires an explicit global gateway transit decision",
    )
    log_residency = surfaces["logs_diagnostics"].get("provider_log_residency")
    require(
        isinstance(log_residency, str)
        and log_residency.strip()
        and log_residency not in {"unknown", "unresolved"},
        "accepted topology requires concrete provider log-residency evidence",
    )

    backup = surfaces["backups"]
    database_backup = backup.get("database_backup")
    require(
        isinstance(database_backup, str)
        and database_backup.strip()
        and database_backup not in {"provider-plan-dependent", "unknown", "unresolved"},
        "accepted topology requires a concrete database backup mechanism",
    )
    backup_residency = backup.get("backup_residency")
    require(
        isinstance(backup_residency, str)
        and backup_residency.strip()
        and backup_residency not in {"unknown", "unresolved"},
        "accepted topology requires concrete backup-residency evidence",
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--require-accepted", action="store_true")
    parser.add_argument("--repo-root", type=Path, default=Path("."))
    args = parser.parse_args()

    try:
        value = load(args.manifest)
        validate(value, args.require_accepted, args.repo_root.resolve())
    except (OSError, json.JSONDecodeError, ValueError) as exc:
        print(f"provider-topology validation failed: {exc}", file=sys.stderr)
        return 1

    print(
        "provider-topology contract valid: "
        + ("accepted" if value["status"] == "accepted" else "blocked")
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
