#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import pathlib
import tempfile
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "tools/deployment/verify_native_artifact_profile.py"
TEMPLATE_PATH = ROOT / "deployment/production_profile.template.json"

spec = importlib.util.spec_from_file_location("verify_native_artifact_profile", MODULE_PATH)
module = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(module)

render_spec = importlib.util.spec_from_file_location(
    "render_deployment_profile",
    ROOT / "tools/deployment/render_deployment_profile.py",
)
renderer = importlib.util.module_from_spec(render_spec)
assert render_spec.loader is not None
render_spec.loader.exec_module(renderer)

SOURCE = "89abcdef0123456789abcdef0123456789abcdef"
template = json.loads(TEMPLATE_PATH.read_text(encoding="utf-8"))
profile = renderer.render_profile(template, SOURCE)
payload = (json.dumps(profile, indent=2) + "\n").encode("utf-8")


def make_artifact(path: pathlib.Path, member: str, extra: dict[str, bytes] | None = None) -> None:
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr(member, payload)
        for name, value in (extra or {}).items():
            archive.writestr(name, value)


with tempfile.TemporaryDirectory() as directory:
    root = pathlib.Path(directory)

    msix = root / "fixture.msix"
    make_artifact(msix, "deployment_profile.json")
    parsed, digest = module.verify_artifact(msix, "windows-msix", SOURCE)
    assert parsed["source"]["commit"] == SOURCE
    assert len(digest) == 64

    apk = root / "fixture.apk"
    make_artifact(apk, "assets/deployment_profile.json")
    module.verify_artifact(apk, "android-apk", SOURCE)

    wrong = root / "wrong.apk"
    make_artifact(wrong, "assets/deployment_profile.json")
    try:
        module.verify_artifact(
            wrong,
            "android-apk",
            "0123456789abcdef0123456789abcdef01234567",
        )
    except ValueError:
        pass
    else:
        raise AssertionError("expected source provenance mismatch rejection")

    secret = root / "secret.apk"
    marker = b"sb_" + b"secret" + b"_fictional"
    make_artifact(
        secret,
        "assets/deployment_profile.json",
        {"assets/accidental.txt": marker},
    )
    try:
        module.verify_artifact(secret, "android-apk", SOURCE)
    except ValueError:
        pass
    else:
        raise AssertionError("expected privileged marker rejection")

    credential_container = root / "credential.msix"
    make_artifact(
        credential_container,
        "deployment_profile.json",
        {"assets/signing.pfx": b"fictional"},
    )
    try:
        module.verify_artifact(credential_container, "windows-msix", SOURCE)
    except ValueError:
        pass
    else:
        raise AssertionError("expected credential-container rejection")

print("native artifact deployment profile tests passed")
