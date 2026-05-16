import unittest
from unittest import mock

from dnspy_mcp.worker import Worker, WorkerCallError, WorkerStartError
from tests.helpers.fake_plugin import FakePlugin


class WorkerHandshakeTests(unittest.TestCase):
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
        self.plugin.start()
        self.addCleanup(self.plugin.stop)

    def _worker(self, **overrides) -> Worker:
        defaults = dict(
            session_id="sess-a",
            dnspy_exe="dnSpy.exe",
            port=self.plugin.port,
            token="tok",
            spawn=lambda cmd, env: mock.MagicMock(pid=1234, poll=lambda: None),
            health_timeout=1.0,
        )
        defaults.update(overrides)
        return Worker(**defaults)

    def test_start_polls_health_and_caches_tools(self) -> None:
        worker = self._worker()
        worker.start()
        self.addCleanup(worker.stop)
        self.assertTrue(worker.is_active)
        self.assertEqual(worker.tools[0]["name"], "list_assemblies")

    def test_wrong_token_fails(self) -> None:
        worker = self._worker(token="wrong")
        with self.assertRaises(WorkerStartError):
            worker.start()

    def test_health_never_arrives(self) -> None:
        self.plugin.health_status = 500
        worker = self._worker(health_timeout=0.3)
        with self.assertRaises(WorkerStartError):
            worker.start()


class WorkerCallTests(unittest.TestCase):
    def setUp(self) -> None:
        self.plugin = FakePlugin(token="tok", tools=[])
        self.plugin.register(
            "tools/call",
            lambda env: {
                "content": [{"type": "text", "text": "ok"}],
                "isError": False,
            },
        )
        self.plugin.start()
        self.addCleanup(self.plugin.stop)
        self.worker = Worker(
            session_id="s",
            dnspy_exe="dnSpy.exe",
            port=self.plugin.port,
            token="tok",
            spawn=lambda cmd, env: mock.MagicMock(pid=1, poll=lambda: None),
            health_timeout=1.0,
        )
        self.worker.start()
        self.addCleanup(self.worker.stop)

    def test_forward_call(self) -> None:
        envelope = {
            "jsonrpc": "2.0",
            "id": "a",
            "method": "tools/call",
            "params": {"name": "x", "arguments": {}},
        }
        resp = self.worker.call(envelope)
        self.assertEqual(resp["result"]["isError"], False)

    def test_retries_then_fails_when_port_closed(self) -> None:
        self.plugin.stop()
        envelope = {
            "jsonrpc": "2.0",
            "id": "a",
            "method": "tools/call",
            "params": {},
        }
        with self.assertRaises(WorkerCallError):
            self.worker.call(envelope, timeout=0.5)


if __name__ == "__main__":
    unittest.main()
