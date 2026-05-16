import unittest
from pathlib import Path
from unittest import mock

from dnspy_mcp.server import build_parser


class BuildParserTests(unittest.TestCase):
    def test_defaults(self) -> None:
        parser = build_parser()
        args = parser.parse_args([])
        self.assertFalse(args.stdio)
        self.assertEqual(args.host, "127.0.0.1")
        self.assertEqual(args.port, 8746)
        self.assertEqual(args.max_workers, 4)

    def test_install_target(self) -> None:
        parser = build_parser()
        args = parser.parse_args(["--install", "claude"])
        self.assertEqual(args.install, "claude")

    def test_install_plugin_subcmd(self) -> None:
        parser = build_parser()
        args = parser.parse_args(
            ["install-plugin", "--dnspy-home", "C:/dnSpy"]
        )
        self.assertEqual(args.command, "install-plugin")
        self.assertEqual(args.dnspy_home, Path("C:/dnSpy"))


class NewCliFlagsTests(unittest.TestCase):
    def test_config_flag(self) -> None:
        parser = build_parser()
        args = parser.parse_args(["--config"])
        self.assertTrue(args.config)

    def test_list_clients_flag(self) -> None:
        parser = build_parser()
        args = parser.parse_args(["--list-clients"])
        self.assertTrue(args.list_clients)

    def test_transport_flag(self) -> None:
        parser = build_parser()
        args = parser.parse_args(["--transport", "stdio"])
        self.assertEqual(args.transport, "stdio")

    def test_scope_flag_choices(self) -> None:
        parser = build_parser()
        args = parser.parse_args(["--install", "claude", "--scope", "global"])
        self.assertEqual(args.scope, "global")
        args = parser.parse_args(["--install", "claude", "--scope", "project"])
        self.assertEqual(args.scope, "project")

    def test_invalid_scope_rejected(self) -> None:
        parser = build_parser()
        with self.assertRaises(SystemExit):
            parser.parse_args(["--install", "claude", "--scope", "userwide"])

    def test_main_list_clients_returns_without_supervisor(self) -> None:
        # Smoke test: --list-clients must not touch supervisor / workers.
        import io
        from contextlib import redirect_stdout
        from dnspy_mcp.server import main

        buf = io.StringIO()
        with redirect_stdout(buf):
            main(["--list-clients"])
        self.assertIn("Claude", buf.getvalue())

    def test_main_config_returns_without_supervisor(self) -> None:
        import io
        from contextlib import redirect_stdout
        from dnspy_mcp.server import main

        buf = io.StringIO()
        with redirect_stdout(buf):
            main(["--config"])
        self.assertIn("[STDIO MCP CONFIGURATION]", buf.getvalue())

    def test_main_install_calls_run_install_command(self) -> None:
        # Patching install_mcp_servers (called by run_install_command) is the
        # simplest way to assert main() reaches the install path.
        from dnspy_mcp.server import main

        with mock.patch("dnspy_mcp.installer.install_mcp_servers") as m:
            main(["--install", "claude"])
        m.assert_called_once()


if __name__ == "__main__":
    unittest.main()
