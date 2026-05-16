import json
import threading
import unittest
import urllib.request
from http.server import ThreadingHTTPServer
from pathlib import Path
from unittest import mock

from dnspy_mcp.server import _McpHttpHandler
from dnspy_mcp.supervisor import Supervisor
from dnspy_mcp.worker import Worker

from tests.helpers.fake_plugin import FakePlugin

FIXTURE = (
    Path(__file__).resolve().parents[2]
    / "src" / "dnspy_mcp" / "plugin_assets" / "tools-schema.json"
)


class E2EFakeTests(unittest.TestCase):
    def setUp(self) -> None:
        self.plugin = FakePlugin(
            token="tok",
            tools=[
                {
                    "name": "list_assemblies",
                    "description": "x",
                    "inputSchema": {},
                }
            ],
        )
        self.plugin.register(
            "tools/call",
            lambda env: {
                "content": [{"type": "text", "text": "[]"}],
                "isError": False,
            },
        )
        self.plugin.start()
        self.addCleanup(self.plugin.stop)

        def factory(session_id: str) -> Worker:
            return Worker(
                session_id=session_id,
                dnspy_exe="dnSpy.exe",
                port=self.plugin.port,
                token="tok",
                spawn=lambda cmd, env: mock.MagicMock(
                    pid=99, poll=lambda: None
                ),
                health_timeout=1.0,
            )

        self.sup = Supervisor(
            worker_factory=factory,
            schema_snapshot_path=FIXTURE,
        )
        _McpHttpHandler.supervisor = self.sup
        self.httpd = ThreadingHTTPServer(
            ("127.0.0.1", 0), _McpHttpHandler
        )
        threading.Thread(
            target=self.httpd.serve_forever, daemon=True
        ).start()
        self.addCleanup(self.httpd.shutdown)
        self.addCleanup(self.httpd.server_close)

    def _mcp(self, body: dict) -> dict:
        data = json.dumps(body).encode("utf-8")
        host, port = self.httpd.server_address[:2]
        req = urllib.request.Request(
            f"http://{host}:{port}/mcp",
            data=data,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=3) as resp:
            return json.loads(resp.read().decode("utf-8"))

    def test_tools_list_includes_snapshot_without_open(self) -> None:
        resp = self._mcp(
            {"jsonrpc": "2.0", "id": 1, "method": "tools/list"}
        )
        names = {t["name"] for t in resp["result"]["tools"]}
        self.assertIn("dnspy_open", names)
        self.assertIn("list_assemblies", names)

    def test_full_flow(self) -> None:
        self._mcp(
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "tools/call",
                "params": {
                    "name": "dnspy_open",
                    "arguments": {"session_id": "a"},
                },
            }
        )
        resp = self._mcp(
            {
                "jsonrpc": "2.0",
                "id": 2,
                "method": "tools/call",
                "params": {
                    "name": "list_assemblies",
                    "arguments": {"instance": "a"},
                },
            }
        )
        self.assertFalse(resp["result"]["isError"])


if __name__ == "__main__":
    unittest.main()
