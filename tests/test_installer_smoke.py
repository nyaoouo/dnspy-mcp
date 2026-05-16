"""End-to-end smoke test for the canonical install command.

Uses a real-file fake Claude config in tmp_path and exercises the full path
from `server.main` argv parsing down through atomic JSON write.
"""
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from dnspy_mcp import installer, installer_data, server


class CanonicalInstallSmokeTest(unittest.TestCase):
    def test_install_claude_stdio_global_writes_expected_entry(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            claude_dir = root / "Claude"
            claude_dir.mkdir()
            fake_global = {
                "Claude": (str(claude_dir), "claude_desktop_config.json"),
            }
            with mock.patch.object(installer_data, "GLOBAL_CONFIGS", fake_global), \
                 mock.patch.object(installer, "GLOBAL_CONFIGS", fake_global):
                server.main([
                    "--install", "claude",
                    "--transport", "stdio",
                    "--scope", "global",
                    "--dnspy-home", "D:/tools/dnSpyEx",
                ])

            cfg_path = claude_dir / "claude_desktop_config.json"
            self.assertTrue(cfg_path.exists())
            cfg = json.loads(cfg_path.read_text("utf-8"))
            entry = cfg["mcpServers"]["dnspy-mcp"]
            self.assertEqual(entry["args"][:3], ["-m", "dnspy_mcp", "--stdio"])
            self.assertIn("--dnspy-home", entry["args"])
            idx = entry["args"].index("--dnspy-home")
            self.assertEqual(
                Path(entry["args"][idx + 1]),
                Path("D:/tools/dnSpyEx"),
            )


if __name__ == "__main__":
    unittest.main()
