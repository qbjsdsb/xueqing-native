import json
import os
import pathlib
import tempfile
import unittest
from argparse import Namespace
from unittest import mock

from tools.release.candidate_evidence import verify, write_android, write_windows


class CandidateEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        self.android = self.root / "Xueqing.apk"
        self.msix = self.root / "Xueqing.msix"
        self.setup = self.root / "XueqingSetup.exe"
        for path, data in (
            (self.android, b"android"),
            (self.msix, b"windows-msix"),
            (self.setup, b"windows-setup"),
        ):
            path.write_bytes(data)
        self.source = "a" * 40
        self.cert = "b" * 64

    @mock.patch.dict(os.environ, {
        "GITHUB_REPOSITORY": "qbjsdsb/xueqing-native",
        "GITHUB_RUN_ID": "12345",
        "GITHUB_RUN_ATTEMPT": "2",
    }, clear=False)
    def test_android_evidence_roundtrip(self):
        output = self.root / "android.json"
        write_android(Namespace(
            source_sha=self.source,
            artifact=self.android,
            certificate_sha256=self.cert,
            output=output,
        ))
        value = json.loads(output.read_text(encoding="utf-8"))
        verify(value)
        self.assertEqual("android", value["platform"])
        self.assertEqual({"apk"}, {item["kind"] for item in value["artifacts"]})

    @mock.patch.dict(os.environ, {
        "GITHUB_REPOSITORY": "qbjsdsb/xueqing-native",
        "GITHUB_RUN_ID": "98765",
        "GITHUB_RUN_ATTEMPT": "1",
    }, clear=False)
    def test_windows_evidence_roundtrip(self):
        output = self.root / "windows.json"
        write_windows(Namespace(
            source_sha=self.source,
            msix=self.msix,
            setup=self.setup,
            publisher="CN=Xueqing Native Test",
            certificate_sha256=self.cert,
            signing_mode="pfx",
            output=output,
        ))
        value = json.loads(output.read_text(encoding="utf-8"))
        verify(value)
        self.assertEqual("windows", value["platform"])
        self.assertEqual({"msix", "setup"}, {item["kind"] for item in value["artifacts"]})

    def test_rejects_source_or_certificate_drift(self):
        with self.assertRaises(ValueError):
            verify({
                "contract": "release_candidate_evidence_v1",
                "generated_at": "2026-09-23T00:00:00Z",
                "source_commit": "not-a-sha",
                "release_version": "1.0.0-rc.1",
                "platform": "android",
                "workflow": {"repository": "qbjsdsb/xueqing-native", "run_id": None, "run_attempt": 1},
                "identity": {"application_id": "com.xueqing.app", "version_code": 100001, "version_name": "1.0.0-rc.1"},
                "artifacts": [{"kind": "apk", "name": "Xueqing.apk", "sha256": "c" * 64}],
                "signing": {"mode": "android-release-key", "certificate_sha256": "d" * 64},
            })


if __name__ == "__main__":
    unittest.main()
