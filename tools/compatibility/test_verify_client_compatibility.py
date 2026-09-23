import copy
import json
import pathlib
import unittest

from tools.compatibility.verify_client_compatibility import verify


ROOT = pathlib.Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "contracts" / "fixtures" / "valid_client_compatibility_v1.json"


class ClientCompatibilityVerifierTests(unittest.TestCase):
    def setUp(self):
        self.value = json.loads(FIXTURE.read_text(encoding="utf-8"))

    def test_valid_fixture(self):
        verify(self.value)

    def test_rejects_unknown_authoritative_state(self):
        value = copy.deepcopy(self.value)
        value["decision"]["state"] = "unknown"
        with self.assertRaises(ValueError):
            verify(value)

    def test_allows_nonblocking_recommended_update(self):
        value = copy.deepcopy(self.value)
        value["decision"]["state"] = "update_recommended"
        value["decision"]["reason_code"] = "XQ_UPDATE_RECOMMENDED"
        value["decision"]["recommended_app_version"] = "1.1.0"
        value["decision"]["update_uri"] = "https://github.com/qbjsdsb/xueqing-native/releases"
        verify(value)

    def test_rejects_privileged_or_non_https_update_uri(self):
        for uri in (
            "http://example.com/update",
            "https://user:secret@example.com/update",
        ):
            with self.subTest(uri=uri):
                value = copy.deepcopy(self.value)
                value["decision"]["update_uri"] = uri
                with self.assertRaises(ValueError):
                    verify(value)

    def test_rejects_extra_fields(self):
        value = copy.deepcopy(self.value)
        value["decision"]["feature_flags"] = {}
        with self.assertRaises(ValueError):
            verify(value)

    def test_rejects_impossible_contract_window(self):
        value = copy.deepcopy(self.value)
        value["decision"]["minimum_supported_contract_version"] = 2
        value["decision"]["server_contract_version"] = 1
        with self.assertRaises(ValueError):
            verify(value)


if __name__ == "__main__":
    unittest.main()
