import io
import json
import os
import sys
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

from dnspy_mcp import installer


class GetPythonExecutableTests(unittest.TestCase):
    def test_falls_back_to_sys_executable_without_venv(self) -> None:
        with mock.patch.dict(os.environ, {}, clear=False):
            os.environ.pop("VIRTUAL_ENV", None)
            self.assertEqual(installer.get_python_executable(), sys.executable)

    def test_uses_venv_python_when_VIRTUAL_ENV_set_and_exists(self) -> None:
        # The dnspy-mcp project venv always exists in dev environments.
        venv = Path(sys.executable).resolve().parent.parent
        with mock.patch.dict(os.environ, {"VIRTUAL_ENV": str(venv)}):
            got = installer.get_python_executable()
            # Resolve both sides — venv path may differ in case on Windows.
            self.assertEqual(Path(got).resolve(), Path(sys.executable).resolve())


class CopyPythonEnvTests(unittest.TestCase):
    def test_copies_only_set_vars(self) -> None:
        with mock.patch.dict(
            os.environ,
            {"PYTHONHOME": "C:/py", "PYTHONPATH": "C:/lib"},
            clear=False,
        ):
            env: dict[str, str] = {}
            copied = installer.copy_python_env(env)
            self.assertTrue(copied)
            self.assertEqual(env["PYTHONHOME"], "C:/py")
            self.assertEqual(env["PYTHONPATH"], "C:/lib")

    def test_returns_false_when_no_vars_set(self) -> None:
        with mock.patch.dict(os.environ, {}, clear=True):
            env: dict[str, str] = {}
            copied = installer.copy_python_env(env)
            self.assertFalse(copied)
            self.assertEqual(env, {})


class ResolveTransportUrlTests(unittest.TestCase):
    def test_stdio_http_default(self) -> None:
        self.assertEqual(
            installer.resolve_transport_url("http", host="127.0.0.1", port=8746),
            "http://127.0.0.1:8746/mcp",
        )

    def test_streamable_http_alias(self) -> None:
        self.assertEqual(
            installer.resolve_transport_url("streamable-http", host="0.0.0.0", port=9001),
            "http://0.0.0.0:9001/mcp",
        )

    def test_sse_path(self) -> None:
        self.assertEqual(
            installer.resolve_transport_url("sse", host="127.0.0.1", port=8746),
            "http://127.0.0.1:8746/sse",
        )

    def test_passthrough_full_url(self) -> None:
        self.assertEqual(
            installer.resolve_transport_url(
                "http://example.com:1234/mcp", host="ignored", port=0
            ),
            "http://example.com:1234/mcp",
        )

    def test_none_treated_as_http(self) -> None:
        self.assertEqual(
            installer.resolve_transport_url(None, host="127.0.0.1", port=8746),
            "http://127.0.0.1:8746/mcp",
        )


class GenerateMcpConfigTests(unittest.TestCase):
    def test_stdio_claude(self) -> None:
        cfg = installer.generate_mcp_config(
            client_name="Claude",
            transport="stdio",
            dnspy_home="D:/tools/dnSpyEx",
        )
        self.assertIn("command", cfg)
        self.assertEqual(cfg["args"][:3], ["-m", "dnspy_mcp", "--stdio"])
        self.assertIn("--dnspy-home", cfg["args"])
        self.assertIn("D:/tools/dnSpyEx", cfg["args"])

    def test_stdio_without_dnspy_home(self) -> None:
        cfg = installer.generate_mcp_config(
            client_name="Cursor",
            transport="stdio",
            dnspy_home=None,
        )
        self.assertNotIn("--dnspy-home", cfg["args"])

    def test_stdio_omits_env_when_no_python_vars_set(self) -> None:
        with mock.patch.dict(os.environ, {}, clear=True):
            cfg = installer.generate_mcp_config(
                client_name="Claude",
                transport="stdio",
            )
        self.assertNotIn("env", cfg)

    def test_streamable_http_claude(self) -> None:
        cfg = installer.generate_mcp_config(
            client_name="Claude",
            transport="streamable-http",
            host="127.0.0.1",
            port=8746,
        )
        self.assertEqual(cfg, {"type": "http", "url": "http://127.0.0.1:8746/mcp"})

    def test_streamable_http_cursor_has_no_type(self) -> None:
        cfg = installer.generate_mcp_config(
            client_name="Cursor",
            transport="streamable-http",
            host="127.0.0.1",
            port=8746,
        )
        self.assertEqual(cfg, {"url": "http://127.0.0.1:8746/mcp"})

    def test_sse_claude(self) -> None:
        cfg = installer.generate_mcp_config(
            client_name="Claude",
            transport="sse",
            host="127.0.0.1",
            port=8746,
        )
        self.assertEqual(cfg, {"type": "sse", "url": "http://127.0.0.1:8746/sse"})

    def test_vscode_http_uses_type_http(self) -> None:
        cfg = installer.generate_mcp_config(
            client_name="VS Code",
            transport="streamable-http",
            host="127.0.0.1",
            port=8746,
        )
        self.assertEqual(cfg, {"type": "http", "url": "http://127.0.0.1:8746/mcp"})


class PrintMcpConfigTests(unittest.TestCase):
    def _args(self, **overrides) -> SimpleNamespace:
        defaults = dict(host="127.0.0.1", port=8746, dnspy_home=None)
        defaults.update(overrides)
        return SimpleNamespace(**defaults)

    def test_prints_all_three_transports(self) -> None:
        buf = io.StringIO()
        with redirect_stdout(buf):
            installer.print_mcp_config(self._args(dnspy_home="D:/dnSpyEx"))
        out = buf.getvalue()
        self.assertIn("[STDIO MCP CONFIGURATION]", out)
        self.assertIn("[STREAMABLE HTTP MCP CONFIGURATION]", out)
        self.assertIn("[SSE MCP CONFIGURATION]", out)

    def test_stdio_block_is_valid_json(self) -> None:
        buf = io.StringIO()
        with redirect_stdout(buf):
            installer.print_mcp_config(self._args(dnspy_home="D:/dnSpyEx"))
        out = buf.getvalue()
        # Extract first JSON object after [STDIO ...].
        marker = "[STDIO MCP CONFIGURATION]\n"
        idx = out.find(marker) + len(marker)
        # Read until first blank line.
        end = out.find("\n\n", idx)
        snippet = out[idx:end]
        parsed = json.loads(snippet)
        self.assertIn("mcpServers", parsed)
        self.assertIn("dnspy-mcp", parsed["mcpServers"])


if __name__ == "__main__":
    unittest.main()
