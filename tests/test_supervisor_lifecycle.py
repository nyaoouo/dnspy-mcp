import unittest

from dnspy_mcp.supervisor import InstanceLimitExceeded, Supervisor


class FakeWorker:
    """Minimal stand-in for Worker that the supervisor can hold."""

    def __init__(self, session_id: str, *, port: int) -> None:
        self.session_id = session_id
        self.port = port
        self.tools = [
            {"name": "list_assemblies", "description": "x", "inputSchema": {}}
        ]
        self._alive = True
        self.calls: list[dict] = []

    @property
    def is_active(self) -> bool:
        return self._alive

    def start(self) -> None:
        pass

    def stop(self) -> None:
        self._alive = False

    def kill(self) -> None:
        self._alive = False

    def call(self, env: dict, *, timeout: float = 30.0) -> dict:
        self.calls.append(env)
        return {
            "jsonrpc": "2.0",
            "id": env.get("id"),
            "result": {"content": []},
        }


class SupervisorLifecycleTests(unittest.TestCase):
    def _make(self, max_workers: int = 4) -> Supervisor:
        seen = {"i": 0}

        def factory(session_id: str) -> FakeWorker:
            seen["i"] += 1
            return FakeWorker(session_id, port=18000 + seen["i"])

        return Supervisor(worker_factory=factory, max_workers=max_workers)

    def test_open_creates_instance(self) -> None:
        sup = self._make()
        info = sup.open(session_id="a")
        self.assertEqual(info["session_id"], "a")
        self.assertEqual(len(sup.list_instances()), 1)

    def test_open_rejects_duplicate(self) -> None:
        sup = self._make()
        sup.open(session_id="a")
        with self.assertRaises(ValueError):
            sup.open(session_id="a")

    def test_close_removes_instance(self) -> None:
        sup = self._make()
        sup.open(session_id="a")
        sup.close("a")
        self.assertEqual(len(sup.list_instances()), 0)

    def test_close_unknown_raises(self) -> None:
        sup = self._make()
        with self.assertRaises(KeyError):
            sup.close("nope")

    def test_max_workers_enforced(self) -> None:
        sup = self._make(max_workers=2)
        sup.open(session_id="a")
        sup.open(session_id="b")
        with self.assertRaises(InstanceLimitExceeded):
            sup.open(session_id="c")


if __name__ == "__main__":
    unittest.main()
