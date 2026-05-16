"""Minimal HTTP server that impersonates a dnspy-mcp plugin for tests."""
from __future__ import annotations

import json
import threading
import time
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Callable


class FakePlugin:
    """A thread-backed fake plugin.

    - ``GET /health`` always 200 if the bearer token matches.
    - ``POST /`` routes by JSON-RPC method to the registered handlers.
    """

    def __init__(
        self,
        *,
        token: str,
        tools: list[dict[str, Any]] | None = None,
    ) -> None:
        self.token = token
        self.tools = tools or []
        self.handlers: dict[str, Callable[[dict[str, Any]], Any]] = {}
        self.calls: list[tuple[str, dict[str, Any]]] = []
        self.health_status = HTTPStatus.OK
        self.health_delay = 0.0  # seconds to sleep before responding
        self._httpd: ThreadingHTTPServer | None = None
        self._thread: threading.Thread | None = None

    def register(
        self, method: str, handler: Callable[[dict[str, Any]], Any]
    ) -> None:
        self.handlers[method] = handler

    @property
    def port(self) -> int:
        assert self._httpd is not None
        return self._httpd.server_address[1]

    def start(self, *, port: int = 0) -> None:
        plugin = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_: Any, **__: Any) -> None:  # silence
                pass

            def _check_auth(self) -> bool:
                value = self.headers.get("Authorization", "")
                if value != f"Bearer {plugin.token}":
                    self.send_response(HTTPStatus.UNAUTHORIZED)
                    self.end_headers()
                    return False
                return True

            def do_GET(self) -> None:  # noqa: N802
                if self.path != "/health":
                    self.send_response(HTTPStatus.NOT_FOUND)
                    self.end_headers()
                    return
                if plugin.health_delay:
                    time.sleep(plugin.health_delay)
                if not self._check_auth():
                    return
                self.send_response(plugin.health_status)
                body = json.dumps(
                    {"ok": True, "mode": "supervised"}
                ).encode("utf-8")
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def do_POST(self) -> None:  # noqa: N802
                if not self._check_auth():
                    return
                length = int(self.headers.get("Content-Length", "0"))
                raw = self.rfile.read(length) if length else b"{}"
                envelope = json.loads(raw.decode("utf-8"))
                method = envelope.get("method", "")
                req_id = envelope.get("id")
                plugin.calls.append((method, envelope))
                if method == "initialize":
                    payload: Any = {
                        "protocolVersion": "2025-03-26",
                        "serverInfo": {"name": "fake"},
                    }
                elif method == "tools/list":
                    payload = {"tools": plugin.tools}
                elif method in plugin.handlers:
                    payload = plugin.handlers[method](envelope)
                else:
                    body = json.dumps(
                        {
                            "jsonrpc": "2.0",
                            "id": req_id,
                            "error": {
                                "code": -32601,
                                "message": "method not found",
                            },
                        }
                    ).encode("utf-8")
                    self.send_response(HTTPStatus.OK)
                    self.send_header("Content-Type", "application/json")
                    self.send_header("Content-Length", str(len(body)))
                    self.end_headers()
                    self.wfile.write(body)
                    return
                body = json.dumps(
                    {"jsonrpc": "2.0", "id": req_id, "result": payload}
                ).encode("utf-8")
                self.send_response(HTTPStatus.OK)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

        self._httpd = ThreadingHTTPServer(("127.0.0.1", port), Handler)
        self._thread = threading.Thread(
            target=self._httpd.serve_forever, daemon=True
        )
        self._thread.start()

    def stop(self) -> None:
        if self._httpd is not None:
            self._httpd.shutdown()
            self._httpd.server_close()
            self._httpd = None
        if self._thread is not None:
            self._thread.join(timeout=2)
            self._thread = None
