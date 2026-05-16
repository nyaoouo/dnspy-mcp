import io
import json
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

from dnspy_mcp import installer


class ReadConfigFileTests(unittest.TestCase):
    def test_reads_valid_json(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "cfg.json"
            path.write_text('{"mcpServers": {"x": 1}}', encoding="utf-8")
            self.assertEqual(
                installer._read_config_file(str(path)),
                {"mcpServers": {"x": 1}},
            )

    def test_empty_file_returns_empty_dict(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "cfg.json"
            path.write_text("", encoding="utf-8")
            self.assertEqual(installer._read_config_file(str(path)), {})

    def test_invalid_json_returns_none(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "cfg.json"
            path.write_text("{not json", encoding="utf-8")
            self.assertIsNone(installer._read_config_file(str(path)))


class WriteConfigFileTests(unittest.TestCase):
    def test_writes_atomically(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "cfg.json"
            installer._write_config_file(str(path), {"a": 1})
            self.assertEqual(json.loads(path.read_text("utf-8")), {"a": 1})
            # No .tmp_ leftover next to the file.
            leftovers = [p for p in Path(tmp).iterdir() if p.name.startswith(".tmp_")]
            self.assertEqual(leftovers, [])

    def test_creates_parent_dir(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "nested" / "cfg.json"
            installer._write_config_file(str(path), {"a": 1})
            self.assertTrue(path.exists())


class GetMcpServersViewTests(unittest.TestCase):
    def test_default_top_level(self) -> None:
        config: dict = {}
        view = installer._get_mcp_servers_view(
            config, client_name="Claude", special_json_structures={}
        )
        view["dnspy-mcp"] = {"command": "x"}
        self.assertEqual(config, {"mcpServers": {"dnspy-mcp": {"command": "x"}}})

    def test_special_nested(self) -> None:
        config: dict = {}
        view = installer._get_mcp_servers_view(
            config,
            client_name="VS Code",
            special_json_structures={"VS Code": ("mcp", "servers")},
        )
        view["dnspy-mcp"] = {"command": "x"}
        self.assertEqual(
            config, {"mcp": {"servers": {"dnspy-mcp": {"command": "x"}}}}
        )

    def test_special_flat_servers_key(self) -> None:
        config: dict = {}
        view = installer._get_mcp_servers_view(
            config,
            client_name="VS Code",
            special_json_structures={"VS Code": (None, "servers")},
        )
        view["dnspy-mcp"] = {"command": "x"}
        self.assertEqual(config, {"servers": {"dnspy-mcp": {"command": "x"}}})


class GetScopeConfigSpecTests(unittest.TestCase):
    def test_global_returns_global_table(self) -> None:
        configs, special = installer._get_scope_config_spec(project=False)
        self.assertIn("Claude", configs)
        self.assertIn("VS Code", special)

    def test_project_returns_project_table_and_excludes_claude(self) -> None:
        configs, special = installer._get_scope_config_spec(
            project=True, project_dir="/tmp/test"
        )
        self.assertNotIn("Claude", configs)
        self.assertIn("Claude Code", configs)
        # Project-scope special-structure for VS Code is flat servers key.
        self.assertEqual(special["VS Code"], (None, "servers"))


class IsClientInstalledTests(unittest.TestCase):
    def test_returns_false_when_config_file_missing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            self.assertFalse(
                installer.is_client_installed("Claude", tmp, "claude_desktop_config.json")
            )

    def test_returns_true_when_entry_present(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "claude_desktop_config.json"
            path.write_text(
                '{"mcpServers": {"dnspy-mcp": {"command": "py"}}}', encoding="utf-8"
            )
            self.assertTrue(
                installer.is_client_installed("Claude", tmp, "claude_desktop_config.json")
            )

    def test_returns_false_when_entry_absent(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "claude_desktop_config.json"
            path.write_text('{"mcpServers": {"other": {}}}', encoding="utf-8")
            self.assertFalse(
                installer.is_client_installed("Claude", tmp, "claude_desktop_config.json")
            )

    def test_returns_true_for_vscode_nested_entry(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "settings.json"
            path.write_text(
                '{"mcp": {"servers": {"dnspy-mcp": {"command": "py"}}}}',
                encoding="utf-8",
            )
            self.assertTrue(
                installer.is_client_installed("VS Code", tmp, "settings.json")
            )


class ListAvailableClientsTests(unittest.TestCase):
    def test_lists_all_core_six_clients(self) -> None:
        buf = io.StringIO()
        with redirect_stdout(buf):
            installer.list_available_clients()
        out = buf.getvalue()
        for name in ("Claude", "Claude Code", "Cursor", "Windsurf",
                     "VS Code", "VS Code Insiders"):
            self.assertIn(name, out)
        self.assertIn("--install", out)  # usage examples shown


if __name__ == "__main__":
    unittest.main()
