"""Supervisor: manages a pool of Worker instances and routes MCP calls."""
from __future__ import annotations

import json
import logging
import threading
from pathlib import Path
from typing import Any, Callable, Protocol

from dnspy_mcp.snapshot import (
    diff_against_live,
    inject_instance_field,
    load_snapshot,
)

logger = logging.getLogger(__name__)

DEFAULT_CONTEXT_ID = "__default__"


class InstanceLimitExceeded(RuntimeError):
    """Raised when --max-workers would be exceeded."""


def _is_error_envelope(req_id: Any, text: str) -> dict[str, Any]:
    return {
        "jsonrpc": "2.0",
        "id": req_id,
        "result": {
            "isError": True,
            "content": [{"type": "text", "text": text}],
        },
    }


class WorkerLike(Protocol):
    session_id: str
    tools: list[dict[str, Any]]

    @property
    def is_active(self) -> bool: ...

    def start(self) -> None: ...

    def stop(self) -> None: ...

    def call(
        self, envelope: dict[str, Any], *, timeout: float = ...
    ) -> dict[str, Any]: ...


WorkerFactory = Callable[[str], WorkerLike]


class Supervisor:
    def __init__(
        self,
        *,
        worker_factory: WorkerFactory,
        max_workers: int = 4,
        schema_snapshot_path: Path | None = None,
        isolated_contexts: bool = False,
        reaper_interval: float = 2.0,
    ) -> None:
        self._factory = worker_factory
        self._max_workers = max_workers
        self._workers: dict[str, WorkerLike] = {}
        self._bindings: dict[str, str] = {}
        self._lock = threading.RLock()
        self._snapshot = (
            load_snapshot(schema_snapshot_path)
            if schema_snapshot_path
            else {"tools": []}
        )
        self._advertised = self._render_advertised()
        self._isolated = isolated_contexts
        self._reaper_interval = reaper_interval
        self._reaper_thread: threading.Thread | None = None
        self._reaper_stop = threading.Event()

    @property
    def max_workers(self) -> int:
        return self._max_workers

    def open(self, *, session_id: str) -> dict[str, Any]:
        with self._lock:
            if session_id in self._workers:
                raise ValueError(f"Instance already exists: {session_id}")
            if self._max_workers and len(self._workers) >= self._max_workers:
                raise InstanceLimitExceeded(
                    f"max-workers={self._max_workers} reached"
                )
            worker = self._factory(session_id)
            worker.start()
            self._workers[session_id] = worker
            return {
                "session_id": session_id,
                "port": getattr(worker, "port", None),
            }

    def close(self, session_id: str) -> None:
        with self._lock:
            worker = self._workers.pop(session_id, None)
        if worker is None:
            raise KeyError(session_id)
        worker.stop()

    def get(self, session_id: str) -> WorkerLike:
        with self._lock:
            try:
                return self._workers[session_id]
            except KeyError:
                raise KeyError(f"Unknown instance: {session_id}") from None

    def list_instances(self) -> list[dict[str, Any]]:
        with self._lock:
            return [
                {
                    "session_id": worker.session_id,
                    "is_active": worker.is_active,
                    "port": getattr(worker, "port", None),
                }
                for worker in self._workers.values()
            ]

    def _render_advertised(self) -> list[dict[str, Any]]:
        management: list[dict[str, Any]] = [
            {
                "name": "dnspy_open",
                "description": (
                    "Spawn a new dnSpy instance; optionally pre-load an "
                    "assembly."
                ),
                "inputSchema": {
                    "type": "object",
                    "properties": {
                        "input_path": {"type": "string"},
                        "session_id": {"type": "string"},
                    },
                },
            },
            {
                "name": "dnspy_close",
                "description": (
                    "Close an instance, optionally saving dirty modules first."
                ),
                "inputSchema": {
                    "type": "object",
                    "properties": {
                        "session_id": {"type": "string"},
                        "save": {"type": "boolean", "default": False},
                        "force": {"type": "boolean", "default": False},
                    },
                    "required": ["session_id"],
                },
            },
            {
                "name": "dnspy_list",
                "description": "Enumerate active dnSpy instances.",
                "inputSchema": {"type": "object", "properties": {}},
            },
            {
                "name": "dnspy_switch",
                "description": (
                    "Change the current-instance binding for this context."
                ),
                "inputSchema": {
                    "type": "object",
                    "properties": {"session_id": {"type": "string"}},
                    "required": ["session_id"],
                },
            },
            {
                "name": "dnspy_health",
                "description": (
                    "Supervisor (and optional worker) health probe."
                ),
                "inputSchema": {
                    "type": "object",
                    "properties": {"session_id": {"type": "string"}},
                },
            },
        ]
        plugin_tools = [
            inject_instance_field(tool) for tool in self._snapshot["tools"]
        ]
        return management + plugin_tools

    def list_tools(self) -> list[dict[str, Any]]:
        return list(self._advertised)

    def audit_live_tools(self, *, session_id: str) -> list[str]:
        worker = self.get(session_id)
        diff = diff_against_live(self._snapshot, worker.tools)
        if diff:
            logger.warning(
                "Schema drift on session %s: %s",
                session_id,
                "; ".join(diff),
            )
        return diff

    def start_reaper(self) -> None:
        if self._reaper_thread is not None:
            return
        self._reaper_stop.clear()
        self._reaper_thread = threading.Thread(
            target=self._reap_loop, daemon=True
        )
        self._reaper_thread.start()

    def stop_reaper(self) -> None:
        self._reaper_stop.set()
        if self._reaper_thread is not None:
            self._reaper_thread.join(timeout=self._reaper_interval * 2)
            self._reaper_thread = None

    def _reap_loop(self) -> None:
        while not self._reaper_stop.wait(self._reaper_interval):
            with self._lock:
                dead = [
                    sid
                    for sid, w in self._workers.items()
                    if not w.is_active
                ]
                for sid in dead:
                    logger.info(
                        "Reaper: instance %s exited, removing", sid
                    )
                    self._workers.pop(sid, None)
                    for ctx, bound in list(self._bindings.items()):
                        if bound == sid:
                            del self._bindings[ctx]

    def handle_envelope(self, envelope: dict[str, Any]) -> dict[str, Any]:
        method = envelope.get("method", "")
        req_id = envelope.get("id")
        if method == "initialize":
            return {
                "jsonrpc": "2.0",
                "id": req_id,
                "result": {
                    "protocolVersion": "2025-03-26",
                    "serverInfo": {"name": "dnspy-mcp", "version": "0.1.0"},
                    "capabilities": {"tools": {"listChanged": True}},
                },
            }
        if method == "tools/list":
            return {
                "jsonrpc": "2.0",
                "id": req_id,
                "result": {"tools": self.list_tools()},
            }
        if method == "tools/call":
            name = (envelope.get("params") or {}).get("name", "")
            if name.startswith("dnspy_"):
                return self._dispatch_management(envelope)
            return self.dispatch_tools_call(envelope)
        if method == "ping":
            return {"jsonrpc": "2.0", "id": req_id, "result": {}}
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "error": {
                "code": -32601,
                "message": f"method not found: {method}",
            },
        }

    def _dispatch_management(
        self, envelope: dict[str, Any]
    ) -> dict[str, Any]:
        req_id = envelope.get("id")
        params = envelope.get("params") or {}
        args = dict(params.get("arguments") or {})
        name = params.get("name")
        try:
            if name == "dnspy_list":
                payload: Any = {"sessions": self.list_instances()}
            elif name == "dnspy_open":
                sid = args.get("session_id") or f"sess-{len(self._workers) + 1}"
                payload = self.open(session_id=sid)
            elif name == "dnspy_close":
                self.close(args["session_id"])
                payload = {"closed": True}
            elif name == "dnspy_switch":
                self.bind_current(
                    context_id=envelope.get(
                        "_context_id", DEFAULT_CONTEXT_ID
                    ),
                    session_id=args["session_id"],
                )
                payload = {"bound": args["session_id"]}
            elif name == "dnspy_health":
                payload = {"ok": True, "instances": len(self._workers)}
            else:
                return {
                    "jsonrpc": "2.0",
                    "id": req_id,
                    "error": {
                        "code": -32601,
                        "message": f"unknown management tool {name}",
                    },
                }
        except Exception as exc:  # noqa: BLE001
            return {
                "jsonrpc": "2.0",
                "id": req_id,
                "result": {
                    "isError": True,
                    "content": [{"type": "text", "text": str(exc)}],
                },
            }
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "result": {
                "isError": False,
                "content": [
                    {"type": "text", "text": json.dumps(payload)}
                ],
                "structuredContent": payload,
            },
        }

    def bind_current(self, *, context_id: str, session_id: str) -> None:
        with self._lock:
            if session_id not in self._workers:
                raise KeyError(session_id)
            self._bindings[context_id] = session_id

    def _current_for(self, context_id: str) -> str | None:
        with self._lock:
            bound = self._bindings.get(context_id)
            if bound is not None:
                return bound
            if self._isolated:
                return None
            # shared mode: fall back to any other context's binding
            for sid in self._bindings.values():
                return sid
            return None

    def dispatch_tools_call(self, envelope: dict[str, Any]) -> dict[str, Any]:
        req_id = envelope.get("id")
        params = envelope.get("params") or {}
        arguments = dict(params.get("arguments") or {})
        context_id = envelope.get("_context_id", DEFAULT_CONTEXT_ID)
        instance = arguments.pop("instance", None) or self._current_for(context_id)
        if not instance:
            return _is_error_envelope(
                req_id,
                "No dnSpy instance bound. Call dnspy_open(input_path=...) first.",
            )
        try:
            worker = self.get(instance)
        except KeyError:
            return _is_error_envelope(
                req_id,
                f"Unknown instance '{instance}'. Call dnspy_list to enumerate.",
            )
        forwarded = dict(envelope)
        forwarded["params"] = dict(params)
        forwarded["params"]["arguments"] = arguments
        forwarded.pop("_context_id", None)
        try:
            return worker.call(forwarded)
        except Exception as exc:  # noqa: BLE001
            logger.warning("Worker call failed for %s: %s", instance, exc)
            return _is_error_envelope(req_id, f"Worker call failed: {exc}")
