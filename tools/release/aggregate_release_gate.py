#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import subprocess
import sys
import time
import urllib.parse
import urllib.request

REPO = os.environ["GITHUB_REPOSITORY"]
TOKEN = os.environ["GITHUB_TOKEN"]
HEAD_SHA = os.environ["XUEQING_HEAD_SHA"]
BASE_SHA = os.environ["XUEQING_BASE_SHA"]

def changed_files() -> list[str]:
    output = subprocess.check_output(
        ["git", "diff", "--name-only", f"{BASE_SHA}...{HEAD_SHA}"],
        text=True,
    )
    return [line.strip() for line in output.splitlines() if line.strip()]

def any_match(files: list[str], prefixes: tuple[str, ...]) -> bool:
    return any(path.startswith(prefixes) for path in files)

def required_workflows(files: list[str]) -> set[str]:
    required = {"foundation"}
    release_scope = any_match(files, (
        "release/",
        "tools/release/",
        "contracts/operations/RELEASE_IDENTITY_SIGNING_V1.md",
        ".github/workflows/release-gate.yml",
        ".github/workflows/release-dry-run.yml",
        ".github/workflows/release-signing.yml",
        ".github/workflows/release-candidate.yml",
        ".github/workflows/manual-acceptance-account.yml",
    ))
    android_scope = release_scope or any_match(files, ("apps/android/",))
    windows_scope = release_scope or any_match(files, ("apps/windows/", "tools/packaging/", "tools/ux/"))
    backend_scope = any_match(files, ("backend/",))
    production_scope = release_scope or any_match(files, (
        "deployment/",
        "tools/deployment/",
        "contracts/schemas/DEPLOYMENT_PROFILE_V1.schema.json",
        "contracts/operations/PRODUCTION_CLIENT_DEPLOYMENT_SESSION_V1.md",
    ))

    if release_scope:
        required.update({
            "release-dry-run",
            "backend-api",
            "production-topology-probe",
        })
    if android_scope:
        required.update({"android-spike", "android-observation-slice"})
    if windows_scope:
        required.update({
            "windows-spike",
            "windows-real-app-integration",
            "windows-observation-read-slice",
        })
    if backend_scope:
        required.add("backend-api")
    if production_scope:
        required.add("production-client")
    return required

def fetch_runs() -> list[dict]:
    query = urllib.parse.urlencode({
        "head_sha": HEAD_SHA,
        "event": "pull_request",
        "per_page": "100",
    })
    request = urllib.request.Request(
        f"https://api.github.com/repos/{REPO}/actions/runs?{query}",
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {TOKEN}",
            "X-GitHub-Api-Version": "2022-11-28",
        },
    )
    with urllib.request.urlopen(request, timeout=20) as response:
        return json.load(response)["workflow_runs"]

def main() -> int:
    files = changed_files()
    required = required_workflows(files)
    print("Changed files:")
    for path in files:
        print(f"  {path}")
    print("Required exact-head workflows:", ", ".join(sorted(required)))

    deadline = time.monotonic() + 35 * 60
    while True:
        runs = fetch_runs()
        latest: dict[str, dict] = {}
        for run in runs:
            name = run.get("name")
            if name not in latest:
                latest[name] = run

        pending = []
        failed = []
        for name in sorted(required):
            run = latest.get(name)
            if run is None:
                pending.append(f"{name}:missing")
                continue
            status = run.get("status")
            conclusion = run.get("conclusion")
            if status != "completed":
                pending.append(f"{name}:{status}")
            elif conclusion != "success":
                failed.append(f"{name}:{conclusion}")

        if failed:
            print("release-gate failed:", ", ".join(failed), file=sys.stderr)
            return 1
        if not pending:
            print("release-gate passed: all required exact-head workflows are green.")
            return 0
        if time.monotonic() >= deadline:
            print("release-gate timed out waiting for:", ", ".join(pending), file=sys.stderr)
            return 1

        print("Waiting for:", ", ".join(pending))
        time.sleep(20)

if __name__ == "__main__":
    raise SystemExit(main())
