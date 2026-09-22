#!/usr/bin/env python3
from __future__ import annotations

import base64
import copy
import importlib.util
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "tools/deployment/verify_deployment_profile.py"
FIXTURE = ROOT / "tools/deployment/fixtures/valid_deployment_profile.json"

spec = importlib.util.spec_from_file_location("verify_deployment_profile", MODULE_PATH)
module = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(module)

valid = json.loads(FIXTURE.read_text(encoding="utf-8"))
module.validate_profile(valid)


def rejected(mutator, label: str) -> None:
    value = copy.deepcopy(valid)
    mutator(value)
    try:
        module.validate_profile(value)
    except ValueError:
        return
    raise AssertionError(f"expected rejection: {label}")


rejected(lambda v: v.__setitem__("project_origin", "http://prod.example.com"), "non-HTTPS origin")
rejected(lambda v: v.__setitem__("project_origin", "https://user@example.com"), "origin user-info")
rejected(lambda v: v.__setitem__("project_origin", "https://example.com/rest/v1"), "origin path")
rejected(lambda v: v.__setitem__("project_origin", "https://localhost"), "loopback hostname")
rejected(lambda v: v.__setitem__("publishable_key", "sb_secret_fictional_should_fail"), "secret key")
rejected(lambda v: v.__setitem__("capabilities", ["auth", "auth"]), "duplicate capability")
rejected(lambda v: v.__setitem__("capabilities", ["private-storage", "auth"]), "non-deterministic capability order")
rejected(lambda v: v["source"].__setitem__("commit", "abc"), "invalid provenance SHA")
rejected(lambda v: v.__setitem__("access_token", "forbidden"), "unknown token field")

payload = base64.urlsafe_b64encode(json.dumps({"role": "service_role"}).encode()).rstrip(b"=").decode()
service_jwt = "eyJhbGciOiJub25lIn0." + payload + ".fictional"
rejected(lambda v: v.__setitem__("publishable_key", service_jwt), "service-role JWT")

print("deployment profile validator tests passed")
