import unittest

from dnspy_mcp.supervisor import Supervisor

from tests.test_supervisor_lifecycle import FakeWorker


class SupervisorRoutingTests(unittest.TestCase):
    def setUp(self) -> None:
        self.workers: dict[str, FakeWorker] = {}

        def factory(session_id: str) -> FakeWorker:
            w = FakeWorker(session_id, port=20000 + len(self.workers))
            self.workers[session_id] = w
            return w

        self.sup = Supervisor(worker_factory=factory)

    def test_routes_to_named_instance(self) -> None:
        self.sup.open(session_id="a")
        self.sup.open(session_id="b")
        envelope = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/call",
            "params": {
                "name": "list_assemblies",
                "arguments": {"instance": "b"},
            },
        }
        self.sup.dispatch_tools_call(envelope)
        self.assertEqual(len(self.workers["a"].calls), 0)
        self.assertEqual(len(self.workers["b"].calls), 1)

    def test_strips_instance_field(self) -> None:
        self.sup.open(session_id="a")
        envelope = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/call",
            "params": {
                "name": "list_assemblies",
                "arguments": {"instance": "a", "cursor": "next"},
            },
        }
        self.sup.dispatch_tools_call(envelope)
        fwd = self.workers["a"].calls[0]
        self.assertEqual(fwd["params"]["arguments"], {"cursor": "next"})

    def test_missing_instance_falls_back_to_current(self) -> None:
        self.sup.open(session_id="a")
        self.sup.bind_current(context_id="ctx-1", session_id="a")
        envelope = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/call",
            "params": {"name": "list_assemblies", "arguments": {}},
            "_context_id": "ctx-1",
        }
        self.sup.dispatch_tools_call(envelope)
        self.assertEqual(len(self.workers["a"].calls), 1)

    def test_no_instance_no_binding_returns_is_error(self) -> None:
        envelope = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/call",
            "params": {"name": "list_assemblies", "arguments": {}},
        }
        resp = self.sup.dispatch_tools_call(envelope)
        self.assertTrue(resp["result"]["isError"])
        self.assertIn("dnspy_open", resp["result"]["content"][0]["text"])

    def test_unknown_instance_returns_is_error(self) -> None:
        envelope = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/call",
            "params": {
                "name": "list_assemblies",
                "arguments": {"instance": "ghost"},
            },
        }
        resp = self.sup.dispatch_tools_call(envelope)
        self.assertTrue(resp["result"]["isError"])


if __name__ == "__main__":
    unittest.main()
