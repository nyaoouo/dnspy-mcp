import time
import unittest

from dnspy_mcp.supervisor import Supervisor

from tests.test_supervisor_lifecycle import FakeWorker


class ContextBindingTests(unittest.TestCase):
    def _make(self, *, isolated: bool) -> Supervisor:
        workers: dict[str, FakeWorker] = {}

        def factory(session_id: str) -> FakeWorker:
            w = FakeWorker(session_id, port=22000 + len(workers))
            workers[session_id] = w
            return w

        return Supervisor(worker_factory=factory, isolated_contexts=isolated)

    def test_default_shared_binding(self) -> None:
        sup = self._make(isolated=False)
        sup.open(session_id="a")
        sup.bind_current(context_id="ctx-1", session_id="a")
        envelope = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/call",
            "params": {"name": "list_assemblies", "arguments": {}},
            "_context_id": "ctx-2",
        }
        resp = sup.dispatch_tools_call(envelope)
        self.assertFalse(resp["result"].get("isError"))

    def test_isolated_contexts(self) -> None:
        sup = self._make(isolated=True)
        sup.open(session_id="a")
        sup.bind_current(context_id="ctx-1", session_id="a")
        envelope = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/call",
            "params": {"name": "list_assemblies", "arguments": {}},
            "_context_id": "ctx-2",
        }
        resp = sup.dispatch_tools_call(envelope)
        self.assertTrue(resp["result"]["isError"])


class ReaperTests(unittest.TestCase):
    def test_reaper_removes_dead(self) -> None:
        workers: dict[str, FakeWorker] = {}

        def factory(session_id: str) -> FakeWorker:
            w = FakeWorker(session_id, port=23000 + len(workers))
            workers[session_id] = w
            return w

        sup = Supervisor(worker_factory=factory, reaper_interval=0.05)
        sup.start_reaper()
        try:
            sup.open(session_id="a")
            workers["a"].kill()
            for _ in range(40):
                if not any(
                    i["session_id"] == "a" for i in sup.list_instances()
                ):
                    break
                time.sleep(0.05)
        finally:
            sup.stop_reaper()
        self.assertEqual(sup.list_instances(), [])


if __name__ == "__main__":
    unittest.main()
