import unittest

from dnspy_mcp import installer_data


class InstallerDataTests(unittest.TestCase):
    def test_mcp_server_name(self) -> None:
        self.assertEqual(installer_data.MCP_SERVER_NAME, "dnspy-mcp")

    def test_global_configs_has_core_six(self) -> None:
        names = set(installer_data.GLOBAL_CONFIGS.keys())
        self.assertEqual(
            names,
            {"Claude", "Claude Code", "Cursor", "Windsurf",
             "VS Code", "VS Code Insiders"},
        )

    def test_project_configs_excludes_claude_desktop(self) -> None:
        self.assertNotIn("Claude", installer_data.PROJECT_CONFIGS)
        self.assertIn("Claude Code", installer_data.PROJECT_CONFIGS)

    def test_project_level_configs_matches_project_configs(self) -> None:
        self.assertEqual(
            installer_data.PROJECT_LEVEL_CONFIGS,
            set(installer_data.PROJECT_CONFIGS.keys()),
        )

    def test_vscode_global_has_special_nesting(self) -> None:
        self.assertEqual(
            installer_data.SPECIAL_JSON_STRUCTURES["VS Code"],
            ("mcp", "servers"),
        )

    def test_vscode_project_has_flat_servers_key(self) -> None:
        self.assertEqual(
            installer_data.PROJECT_SPECIAL_JSON_STRUCTURES["VS Code"],
            (None, "servers"),
        )

    def test_resolve_client_name_case_insensitive(self) -> None:
        available = ["Claude", "Cursor", "VS Code"]
        self.assertEqual(
            installer_data.resolve_client_name("claude", available), "Claude"
        )
        self.assertEqual(
            installer_data.resolve_client_name("VSCODE", available), "VS Code"
        )

    def test_resolve_client_name_aliases(self) -> None:
        available = ["Claude Code", "VS Code"]
        self.assertEqual(
            installer_data.resolve_client_name("vscode", available), "VS Code"
        )
        self.assertEqual(
            installer_data.resolve_client_name("claude-code", available),
            "Claude Code",
        )

    def test_resolve_client_name_unknown(self) -> None:
        self.assertIsNone(
            installer_data.resolve_client_name("nope", ["Claude"])
        )


if __name__ == "__main__":
    unittest.main()
