import json
import logging
import unittest
from pathlib import Path

from dnspy_mcp.snapshot import (
    diff_against_live,
    inject_instance_field,
    load_snapshot,
)

FIXTURE = (
    Path(__file__).resolve().parent.parent
    / "src" / "dnspy_mcp" / "plugin_assets" / "tools-schema.json"
)


class LoadSnapshotTests(unittest.TestCase):
    def test_loads_tools(self) -> None:
        snap = load_snapshot(FIXTURE)
        self.assertIn("tools", snap)
        self.assertGreaterEqual(len(snap["tools"]), 1)

    def test_rejects_missing_file(self) -> None:
        with self.assertRaises(FileNotFoundError):
            load_snapshot(Path("Z:/nope.json"))


class InjectInstanceFieldTests(unittest.TestCase):
    def test_adds_instance_property(self) -> None:
        tool = {
            "name": "list_assemblies",
            "description": "x",
            "inputSchema": {
                "type": "object",
                "properties": {"cursor": {"type": "string"}},
            },
        }
        injected = inject_instance_field(tool)
        self.assertIn("instance", injected["inputSchema"]["properties"])
        self.assertEqual(
            injected["inputSchema"]["properties"]["instance"]["type"], "string"
        )

    def test_preserves_other_fields(self) -> None:
        tool = {
            "name": "x",
            "description": "y",
            "inputSchema": {
                "type": "object",
                "properties": {"a": {"type": "integer"}},
            },
        }
        injected = inject_instance_field(tool)
        self.assertIn("a", injected["inputSchema"]["properties"])
        self.assertEqual(injected["description"], "y")

    def test_does_not_overwrite_existing_instance(self) -> None:
        tool = {
            "name": "x",
            "description": "y",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "instance": {"type": "string", "enum": ["a"]},
                },
            },
        }
        injected = inject_instance_field(tool)
        self.assertEqual(
            injected["inputSchema"]["properties"]["instance"]["enum"], ["a"]
        )


class DiffAgainstLiveTests(unittest.TestCase):
    def test_no_diff(self) -> None:
        snap = {"tools": [{"name": "t", "inputSchema": {}}]}
        live = [{"name": "t", "inputSchema": {}}]
        self.assertEqual(diff_against_live(snap, live), [])

    def test_reports_missing_tool(self) -> None:
        snap = {"tools": [{"name": "a"}, {"name": "b"}]}
        live = [{"name": "a"}]
        diff = diff_against_live(snap, live)
        self.assertTrue(any("b" in entry for entry in diff))

    def test_reports_extra_tool(self) -> None:
        snap = {"tools": [{"name": "a"}]}
        live = [{"name": "a"}, {"name": "z"}]
        diff = diff_against_live(snap, live)
        self.assertTrue(any("z" in entry for entry in diff))


if __name__ == "__main__":
    unittest.main()
