import unittest
from pathlib import Path

from dnspy_mcp.supervisor import Supervisor

from tests.test_supervisor_lifecycle import FakeWorker

FIXTURE = (
    Path(__file__).resolve().parent.parent
    / "src" / "dnspy_mcp" / "plugin_assets" / "tools-schema.json"
)


class AdvertisedToolsTests(unittest.TestCase):
    def _make(self) -> Supervisor:
        workers: dict[str, FakeWorker] = {}

        def factory(session_id: str) -> FakeWorker:
            w = FakeWorker(session_id, port=21000 + len(workers))
            workers[session_id] = w
            return w

        return Supervisor(
            worker_factory=factory, schema_snapshot_path=FIXTURE
        )

    def test_empty_pool_lists_management_plus_snapshot(self) -> None:
        sup = self._make()
        listing = sup.list_tools()
        names = [tool["name"] for tool in listing]
        self.assertIn("dnspy_open", names)
        self.assertIn("dnspy_list", names)
        self.assertIn("dnspy_close", names)
        self.assertIn("dnspy_switch", names)
        self.assertIn("dnspy_health", names)
        self.assertIn("list_assemblies", names)

    def test_snapshot_tools_have_instance_field(self) -> None:
        sup = self._make()
        listing = sup.list_tools()
        list_assemblies = next(
            t for t in listing if t["name"] == "list_assemblies"
        )
        self.assertIn(
            "instance", list_assemblies["inputSchema"]["properties"]
        )

    def test_live_drift_logs_warning(self) -> None:
        sup = self._make()
        sup.open(session_id="a")
        worker = sup.get("a")
        worker.tools = [
            {"name": "list_assemblies", "inputSchema": {}},
            {"name": "extra", "inputSchema": {}},
        ]
        with self.assertLogs("dnspy_mcp.supervisor", level="WARNING") as captured:
            sup.audit_live_tools(session_id="a")
        joined = "\n".join(captured.output)
        self.assertIn("extra", joined)


if __name__ == "__main__":
    unittest.main()
