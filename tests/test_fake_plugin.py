import json
import unittest
import urllib.error
import urllib.request

from tests.helpers.fake_plugin import FakePlugin


class FakePluginSmokeTests(unittest.TestCase):
    def setUp(self) -> None:
        self.plugin = FakePlugin(
            token="secret",
            tools=[{"name": "ping", "inputSchema": {}}],
        )
        self.plugin.start()
        self.addCleanup(self.plugin.stop)

    def _request(
        self, body: dict, *, token: str = "secret"
    ) -> tuple[int, dict]:
        data = json.dumps(body).encode("utf-8")
        req = urllib.request.Request(
            f"http://127.0.0.1:{self.plugin.port}/",
            data=data,
            headers={
                "Content-Type": "application/json",
                "Authorization": f"Bearer {token}",
            },
            method="POST",
        )
        try:
            with urllib.request.urlopen(req, timeout=2) as resp:
                return resp.status, json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            return exc.code, {}

    def test_initialize(self) -> None:
        status, body = self._request(
            {"jsonrpc": "2.0", "id": 1, "method": "initialize"}
        )
        self.assertEqual(status, 200)
        self.assertEqual(body["result"]["serverInfo"]["name"], "fake")

    def test_tools_list(self) -> None:
        _, body = self._request(
            {"jsonrpc": "2.0", "id": 2, "method": "tools/list"}
        )
        self.assertEqual(body["result"]["tools"][0]["name"], "ping")

    def test_bad_token(self) -> None:
        status, _ = self._request(
            {"jsonrpc": "2.0", "id": 3, "method": "ping"}, token="wrong"
        )
        self.assertEqual(status, 401)


if __name__ == "__main__":
    unittest.main()
