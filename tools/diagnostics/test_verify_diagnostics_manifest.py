import copy
import json
import pathlib
import unittest

from tools.diagnostics.verify_diagnostics_manifest import verify


ROOT = pathlib.Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "contracts" / "fixtures" / "valid_diagnostics_manifest_v1.json"


class DiagnosticsManifestVerifierTests(unittest.TestCase):
    def setUp(self):
        self.value = json.loads(FIXTURE.read_text(encoding="utf-8"))

    def test_valid_fixture(self):
        verify(self.value)

    def test_rejects_freeform_message_field(self):
        value = copy.deepcopy(self.value)
        value["message"] = "虚构学生甲的课堂观察"
        with self.assertRaises(ValueError):
            verify(value)

    def test_rejects_non_machine_readable_error(self):
        value = copy.deepcopy(self.value)
        value["recent_error_codes"] = ["学生姓名=虚构学生甲"]
        with self.assertRaises(ValueError):
            verify(value)

    def test_rejects_token_like_extra_field(self):
        value = copy.deepcopy(self.value)
        value["runtime"]["access_token"] = "secret-token"
        with self.assertRaises(ValueError):
            verify(value)

    def test_unknown_queue_counts_are_explicit_null(self):
        value = copy.deepcopy(self.value)
        value["queues"] = {
            "pending_intents": None,
            "outbox_items": None,
            "attachment_staging": None,
        }
        verify(value)

    def test_unknown_compatibility_is_allowed_only_as_bounded_diagnostic_state(self):
        value = copy.deepcopy(self.value)
        value["compatibility"] = {
            "state": "unknown",
            "reason_code": None,
            "policy_revision": None,
        }
        verify(value)


if __name__ == "__main__":
    unittest.main()
