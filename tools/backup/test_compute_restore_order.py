#!/usr/bin/env python3
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / "tools" / "backup" / "compute_restore_order.py"
spec = importlib.util.spec_from_file_location("compute_restore_order", PATH)
assert spec is not None and spec.loader is not None
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class RestoreOrderTests(unittest.TestCase):
    def test_parent_precedes_child(self) -> None:
        value = {
            "tables": ["child", "parent", "leaf"],
            "foreign_keys": [
                {"child": "child", "parent": "parent"},
                {"child": "leaf", "parent": "child"},
            ],
        }
        self.assertEqual(["parent", "child", "leaf"], module.compute(value))

    def test_independent_tables_are_deterministic(self) -> None:
        value = {
            "tables": ["zeta", "alpha", "beta"],
            "foreign_keys": [],
        }
        self.assertEqual(["alpha", "beta", "zeta"], module.compute(value))

    def test_cycle_fails_closed(self) -> None:
        value = {
            "tables": ["a", "b"],
            "foreign_keys": [
                {"child": "a", "parent": "b"},
                {"child": "b", "parent": "a"},
            ],
        }
        with self.assertRaisesRegex(ValueError, "cycle"):
            module.compute(value)

    def test_self_reference_fails_closed(self) -> None:
        value = {
            "tables": ["node"],
            "foreign_keys": [{"child": "node", "parent": "node"}],
        }
        with self.assertRaisesRegex(ValueError, "self-referential"):
            module.compute(value)

    def test_unknown_table_fails_closed(self) -> None:
        value = {
            "tables": ["known"],
            "foreign_keys": [{"child": "known", "parent": "missing"}],
        }
        with self.assertRaisesRegex(ValueError, "unknown table"):
            module.compute(value)


if __name__ == "__main__":
    unittest.main()
