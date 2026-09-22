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


# The production profile is a template because a checked-in file cannot
# self-contain the SHA of the commit that contains itself. Release/CI renders
# the exact source SHA and validates the final shippable profile.
RENDERER_PATH = ROOT / "tools/deployment/render_deployment_profile.py"
PRODUCTION_TEMPLATE = ROOT / "deployment/production_profile.template.json"
renderer_spec = importlib.util.spec_from_file_location(
    "render_deployment_profile",
    RENDERER_PATH,
)
renderer = importlib.util.module_from_spec(renderer_spec)
assert renderer_spec.loader is not None
renderer_spec.loader.exec_module(renderer)

production_template = json.loads(PRODUCTION_TEMPLATE.read_text(encoding="utf-8"))
rendered = renderer.render_profile(
    production_template,
    "89abcdef0123456789abcdef0123456789abcdef",
)
module.validate_profile(rendered)
assert rendered["profile_id"] == "xueqing-prod-sg-v1"
assert rendered["environment_id"] == "production-sg-v1"
assert rendered["trust_domain_id"] == "xueqing-native-prod-sg"
assert rendered["required_edge_region"] == "ap-southeast-1"
assert rendered["source"]["commit"] == "89abcdef0123456789abcdef0123456789abcdef"

try:
    renderer.render_profile(production_template, "not-a-commit")
except ValueError:
    pass
else:
    raise AssertionError("expected production renderer to reject malformed source SHA")

wrong_placeholder = copy.deepcopy(production_template)
wrong_placeholder["source"]["commit"] = "0123456789abcdef0123456789abcdef01234567"
try:
    renderer.render_profile(
        wrong_placeholder,
        "89abcdef0123456789abcdef0123456789abcdef",
    )
except ValueError:
    pass
else:
    raise AssertionError("expected renderer to reject a template with frozen provenance")
