import json
import os
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

from dnspy_mcp import installer, installer_data


def _args(**overrides) -> SimpleNamespace:
    defaults = dict(host="127.0.0.1", port=8746, dnspy_home="D:/tools/dnSpyEx")
    defaults.update(overrides)
    return SimpleNamespace(**defaults)


class InstallMcpServersTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = Path(self._tmp.name)
        # Build a fake GLOBAL_CONFIGS table that points inside tmp_path.
        self.fake_global = {
            "Claude": (str(self.root / "Claude"), "claude_desktop_config.json"),
            "Cursor": (str(self.root / ".cursor"), "mcp.json"),
            "VS Code": (str(self.root / "Code" / "User"), "settings.json"),
        }
        # Pre-create dirs so install doesn't skip them.
        for d, _ in self.fake_global.values():
            os.makedirs(d, exist_ok=True)

        self._patches = [
            mock.patch.object(installer_data, "GLOBAL_CONFIGS", self.fake_global),
            mock.patch.object(installer, "GLOBAL_CONFIGS", self.fake_global),
        ]
        for p in self._patches:
            p.start()
            self.addCleanup(p.stop)

    def _read(self, name: str) -> dict:
        config_dir, config_file = self.fake_global[name]
        return json.loads(Path(config_dir, config_file).read_text("utf-8"))

    def test_install_creates_mcpServers_entry(self) -> None:
        installer.install_mcp_servers(
            args=_args(), transport="stdio", only=["Claude"]
        )
        cfg = self._read("Claude")
        self.assertIn("dnspy-mcp", cfg["mcpServers"])
        self.assertIn("--stdio", cfg["mcpServers"]["dnspy-mcp"]["args"])

    def test_install_preserves_unrelated_entries(self) -> None:
        path = Path(self.fake_global["Cursor"][0]) / self.fake_global["Cursor"][1]
        path.write_text(
            '{"mcpServers": {"other-mcp": {"url": "http://localhost:1"}}}',
            encoding="utf-8",
        )
        installer.install_mcp_servers(
            args=_args(), transport="streamable-http", only=["Cursor"]
        )
        cfg = self._read("Cursor")
        self.assertIn("other-mcp", cfg["mcpServers"])
        self.assertIn("dnspy-mcp", cfg["mcpServers"])

    def test_install_vscode_uses_nested_mcp_servers_key(self) -> None:
        installer.install_mcp_servers(
            args=_args(), transport="streamable-http", only=["VS Code"]
        )
        cfg = self._read("VS Code")
        self.assertIn("mcp", cfg)
        self.assertIn("servers", cfg["mcp"])
        self.assertIn("dnspy-mcp", cfg["mcp"]["servers"])
        self.assertNotIn("mcpServers", cfg)

    def test_uninstall_removes_entry(self) -> None:
        installer.install_mcp_servers(
            args=_args(), transport="stdio", only=["Claude"]
        )
        installer.install_mcp_servers(
            args=_args(), transport="stdio", only=["Claude"], uninstall=True
        )
        cfg = self._read("Claude")
        self.assertNotIn("dnspy-mcp", cfg["mcpServers"])

    def test_install_idempotent(self) -> None:
        installer.install_mcp_servers(
            args=_args(), transport="stdio", only=["Claude"]
        )
        installer.install_mcp_servers(
            args=_args(), transport="stdio", only=["Claude"]
        )
        cfg = self._read("Claude")
        # Only one entry, no duplicate keys (dict semantics).
        self.assertEqual(list(cfg["mcpServers"].keys()), ["dnspy-mcp"])

    def test_unknown_target_is_reported_not_fatal(self) -> None:
        # Should not raise; should print a message.
        installer.install_mcp_servers(
            args=_args(), transport="stdio", only=["NotARealClient"]
        )


class RunInstallCommandTests(unittest.TestCase):
    def test_empty_targets_prints_help(self) -> None:
        import io
        from contextlib import redirect_stdout
        buf = io.StringIO()
        with redirect_stdout(buf):
            installer.run_install_command(
                uninstall=False, targets_str="", args=_args()
            )
        self.assertIn("--list-clients", buf.getvalue())


if __name__ == "__main__":
    unittest.main()
