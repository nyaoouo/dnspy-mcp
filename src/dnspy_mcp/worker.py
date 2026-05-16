"""One Worker = one dnSpy.exe process + its plugin HTTP MCP client."""
from __future__ import annotations

import json
import logging
import os
import subprocess
import threading
import time
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from typing import Any, Callable

logger = logging.getLogger(__name__)


class WorkerStartError(RuntimeError):
    """Raised when a Worker fails to come up."""


class WorkerCallError(RuntimeError):
    """Raised when a forwarded MCP call cannot reach the worker."""


SpawnFn = Callable[[list[str], dict[str, str]], "subprocess.Popen[bytes]"]


def _default_spawn(cmd: list[str], env: dict[str, str]) -> "subprocess.Popen[bytes]":
    return subprocess.Popen(
        cmd,
        env=env,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )


@dataclass
class Worker:
    session_id: str
    dnspy_exe: str
    port: int
    token: str
    bind: str = "127.0.0.1"
    spawn: SpawnFn = _default_spawn
    health_timeout: float = 60.0
    process: "subprocess.Popen[bytes] | None" = None
    tools: list[dict[str, Any]] = field(default_factory=list)
    _lock: threading.RLock = field(default_factory=threading.RLock)

    @property
    def is_active(self) -> bool:
        proc = self.process
        return proc is not None and proc.poll() is None

    @property
    def base_url(self) -> str:
        return f"http://{self.bind}:{self.port}"

    def start(self) -> None:
        env = os.environ.copy()
        env["DNSPY_MCP_PORT"] = str(self.port)
        env["DNSPY_MCP_TOKEN"] = self.token
        env["DNSPY_MCP_BIND"] = self.bind
        self.process = self.spawn([self.dnspy_exe], env)
        self._wait_for_health()
        self._fetch_tool_list()

    def stop(self) -> None:
        proc = self.process
        if proc is None:
            return
        if proc.poll() is None:
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()

    def call(
        self, envelope: dict[str, Any], *, timeout: float = 30.0
    ) -> dict[str, Any]:
        """Forward a JSON-RPC envelope to the plugin and return its response."""
        last_exc: Exception | None = None
        for attempt in range(2):
            try:
                return self._post(envelope, timeout=timeout)
            except (urllib.error.URLError, ConnectionError, TimeoutError) as exc:
                last_exc = exc
                logger.warning(
                    "Worker %s POST failed (attempt %d): %s",
                    self.session_id,
                    attempt + 1,
                    exc,
                )
                time.sleep(0.2)
        raise WorkerCallError(str(last_exc)) from last_exc

    def _post(self, envelope: dict[str, Any], *, timeout: float) -> dict[str, Any]:
        data = json.dumps(envelope).encode("utf-8")
        req = urllib.request.Request(
            self.base_url + "/",
            data=data,
            headers={
                "Content-Type": "application/json",
                "Authorization": f"Bearer {self.token}",
            },
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return json.loads(resp.read().decode("utf-8"))

    def _wait_for_health(self) -> None:
        deadline = time.monotonic() + self.health_timeout
        last_err: Exception | None = None
        while time.monotonic() < deadline:
            if not self.is_active:
                raise WorkerStartError("dnSpy process exited before health came up")
            try:
                req = urllib.request.Request(
                    self.base_url + "/health",
                    headers={"Authorization": f"Bearer {self.token}"},
                )
                with urllib.request.urlopen(req, timeout=2) as resp:
                    if resp.status == 200:
                        return
            except urllib.error.HTTPError as exc:
                if exc.code == 401:
                    raise WorkerStartError(
                        f"Plugin rejected token for session {self.session_id}"
                    ) from exc
                last_err = exc
            except (urllib.error.URLError, ConnectionError, TimeoutError) as exc:
                last_err = exc
            time.sleep(0.5)
        raise WorkerStartError(
            f"Plugin /health never reached 200 within {self.health_timeout}s "
            f"(last error: {last_err})"
        )

    def _fetch_tool_list(self) -> None:
        envelope: dict[str, Any] = {
            "jsonrpc": "2.0",
            "id": "init",
            "method": "initialize",
        }
        self._post(envelope, timeout=5)
        envelope = {
            "jsonrpc": "2.0",
            "id": "list",
            "method": "tools/list",
        }
        resp = self._post(envelope, timeout=5)
        self.tools = list(resp.get("result", {}).get("tools", []))
