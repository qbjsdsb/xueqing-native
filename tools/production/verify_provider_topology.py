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

    primary = value.get("primary_project_region")
    require(isinstance(primary, dict), "primary_project_region must be an object")
    region = primary.get("region")
    require(
        isinstance(region, str) and REGION_RE.match(region) is not None,
        "accepted topology requires an exact provider region identifier",
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

    edge_region = surfaces["edge_invitation_delivery"].get("production_execution_region")
    require(
        isinstance(edge_region, str) and edge_region,
        "accepted topology requires explicit Edge execution-region policy",
    )
    require(
        surfaces["logs_diagnostics"].get("provider_log_residency"),
        "accepted topology requires provider log-residency evidence",
    )
    require(
        surfaces["backups"].get("backup_residency"),
        "accepted topology requires backup-residency evidence",
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
