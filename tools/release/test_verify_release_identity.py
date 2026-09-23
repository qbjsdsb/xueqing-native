import pathlib
import unittest

from tools.release.verify_release_identity import read_properties, verify


class ReleaseIdentityTests(unittest.TestCase):
    def test_repository_release_identity_is_valid(self):
        verify()

    def test_duplicate_property_is_rejected(self):
        path = pathlib.Path("/tmp/xueqing-release-duplicate.properties")
        path.write_text("a=1\na=2\n", encoding="utf-8")
        self.addCleanup(path.unlink, missing_ok=True)
        with self.assertRaises(ValueError):
            read_properties(path)


if __name__ == "__main__":
    unittest.main()
