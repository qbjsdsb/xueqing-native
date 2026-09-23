#!/usr/bin/env python3
from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
VERSION = ROOT / "release" / "version.properties"
WINDOWS_TEMPLATE = ROOT / "release" / "windows" / "Package.production.appxmanifest.template"
ANDROID_GRADLE = ROOT / "apps" / "android" / "app" / "build.gradle.kts"
WINDOWS_SETUP_PROJECT = ROOT / "apps" / "windows" / "src" / "Xueqing.Setup" / "Xueqing.Setup.csproj"
WINDOWS_SETUP_PROGRAM = ROOT / "apps" / "windows" / "src" / "Xueqing.Setup" / "Program.cs"
WINDOWS_SETUP_BUILDER = ROOT / "tools" / "release" / "build-windows-setup.ps1"
RELEASE_SIGNING_WORKFLOW = ROOT / ".github" / "workflows" / "release-signing.yml"

SEMVER_RC = re.compile(r"^\d+\.\d+\.\d+-rc\.\d+$")
WINDOWS_VERSION = re.compile(r"^\d+\.\d+\.\d+\.\d+$")
PACKAGE_NAME = re.compile(r"^[A-Za-z0-9.-]+$")


def read_properties(path: pathlib.Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            raise ValueError(f"invalid release property line: {raw}")
        key, value = line.split("=", 1)
        if not key or key in values or value != value.strip():
            raise ValueError(f"invalid release property: {raw}")
        values[key] = value
    return values


def verify() -> None:
    values = read_properties(VERSION)
    expected = {
        "releaseVersion",
        "releaseChannel",
        "androidApplicationId",
        "androidVersionCode",
        "androidVersionName",
        "windowsPackageName",
        "windowsPackageVersion",
        "windowsArchitecture",
    }
    if set(values) != expected:
        raise ValueError("release/version.properties keys do not match V1 contract")

    if values["releaseChannel"] != "rc":
        raise ValueError("current V1 candidate must remain rc until final promotion")
    if not SEMVER_RC.fullmatch(values["releaseVersion"]):
        raise ValueError("releaseVersion must be an explicit rc label")
    if values["androidVersionName"] != values["releaseVersion"]:
        raise ValueError("Android versionName must equal releaseVersion")
    if values["androidApplicationId"] != "com.xueqing.app":
        raise ValueError("Android application id drifted")
    code = int(values["androidVersionCode"])
    if not 1 <= code <= 2_100_000_000:
        raise ValueError("Android versionCode is outside supported range")
    if values["windowsPackageName"] != "Xueqing.Native":
        raise ValueError("Windows production package name drifted")
    if not WINDOWS_VERSION.fullmatch(values["windowsPackageVersion"]):
        raise ValueError("Windows package version must be four numeric parts")
    if values["windowsArchitecture"] != "x64":
        raise ValueError("Windows V1 architecture drifted")

    template = WINDOWS_TEMPLATE.read_text(encoding="utf-8")
    required = [
        'Name="Xueqing.Native"',
        'Publisher="__XUEQING_WINDOWS_PUBLISHER__"',
        'Version="__XUEQING_WINDOWS_PACKAGE_VERSION__"',
        'ProcessorArchitecture="x64"',
        '<uap:Protocol Name="xueqing">',
    ]
    for needle in required:
        if needle not in template:
            raise ValueError(f"production Windows manifest template missing: {needle}")

    if values["windowsPackageVersion"] in template:
        raise ValueError("Windows template must render package version from the release source")

    for path in (WINDOWS_SETUP_PROJECT, WINDOWS_SETUP_PROGRAM, WINDOWS_SETUP_BUILDER):
        if not path.is_file():
            raise ValueError(f"required Windows Setup release file is missing: {path.relative_to(ROOT)}")

    setup_project = WINDOWS_SETUP_PROJECT.read_text(encoding="utf-8")
    setup_program = WINDOWS_SETUP_PROGRAM.read_text(encoding="utf-8")
    setup_builder = WINDOWS_SETUP_BUILDER.read_text(encoding="utf-8")
    if "<AssemblyName>XueqingSetup</AssemblyName>" not in setup_project:
        raise ValueError("Windows Setup assembly identity drifted")
    if "Xueqing.Native.msix" not in setup_project or "EmbeddedResource" not in setup_project:
        raise ValueError("Windows Setup must embed the exact MSIX payload")
    for needle in (
        "WinVerifyTrust",
        "PackageManager",
        "XueqingPayloadSha256",
        "XueqingPublisher",
        'const string LaunchUri = "xueqing://today"',
    ):
        if needle not in setup_program:
            raise ValueError(f"Windows Setup contract missing: {needle}")
    if "build-windows-setup.ps1" in setup_builder:
        raise ValueError("Windows Setup builder must not recursively invoke itself")
    if "Get-FileHash" not in setup_builder or "XueqingEmbeddedMsix" not in setup_builder:
        raise ValueError("Windows Setup builder must bind the embedded MSIX hash and bytes")

    signing_workflow = RELEASE_SIGNING_WORKFLOW.read_text(encoding="utf-8")
    for needle in (
        "environment: production-release",
        "XUEQING_ANDROID_KEYSTORE_B64",
        "XUEQING_WINDOWS_SIGNING_MODE",
        "Build single-file XueqingSetup from exact signed MSIX",
        "Normalize signed MSIX artifact name",
        "Signed Setup install smoke",
        "release-candidate-record:",
        "Create durable draft GitHub Release from signed bytes",
        "actions/download-artifact@37930b1c2abaa49bbe596cd826c3c89aef350131",
    ):
        if needle not in signing_workflow:
            raise ValueError(f"release signing workflow contract missing: {needle}")
    forbidden_release_literals = (
        "BEGIN PRIVATE KEY",
        "BEGIN RSA PRIVATE KEY",
        "BEGIN EC PRIVATE KEY",
    )
    if any(value in signing_workflow for value in forbidden_release_literals):
        raise ValueError("release signing workflow contains private key material")

    gradle = ANDROID_GRADLE.read_text(encoding="utf-8")
    if 'applicationId = releaseVersionProperties.getProperty("androidApplicationId")' not in gradle:
        raise ValueError("Android application id is not sourced from release/version.properties")
    if 'versionCode = releaseVersionProperties.getProperty("androidVersionCode").toInt()' not in gradle:
        raise ValueError("Android versionCode is not sourced from release/version.properties")
    if 'versionName = releaseVersionProperties.getProperty("androidVersionName")' not in gradle:
        raise ValueError("Android versionName is not sourced from release/version.properties")


def main() -> int:
    verify()
    print("Release identity v1 is valid.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
