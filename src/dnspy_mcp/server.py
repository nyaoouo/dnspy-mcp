"""dnspy-mcp server entry point and CLI."""
from __future__ import annotations

import argparse
import importlib.resources as pkg_resources
import json
import logging
import os
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any

from dnspy_mcp.config import default_dnspy_home_from_env
from dnspy_mcp.installer import (
    install_plugin_dll,
    list_available_clients,
    print_mcp_config,
    run_install_command,
)
from dnspy_mcp.supervisor import Supervisor

logger = logging.getLogger(__name__)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="dnspy-mcp",
        description="Multi-instance MCP supervisor for dnSpyEx.",
    )
    parser.add_argument("--stdio", action="store_true", help="Serve MCP over stdio")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8746)
    parser.add_argument(
        "--max-workers",
        type=int,
        default=int(os.environ.get("DNSPY_MCP_MAX_WORKERS", "4")),
    )
    parser.add_argument(
        "--dnspy-home",
        type=Path,
        default=default_dnspy_home_from_env(),
    )
    parser.add_argument(
        "--isolated-contexts",
        action="store_true",
        help="Per-MCP-session current-instance binding",
    )
    parser.add_argument(
        "--show-worker-io",
        action="store_true",
        help="Inherit dnSpy.exe stdout/stderr (default: silenced)",
    )
    parser.add_argument("--verbose", "-v", action="store_true")
    parser.add_argument(
        "--install",
        default=None,
        help="Install MCP client config: comma-separated client names (e.g. claude,cursor).",
    )
    parser.add_argument(
        "--uninstall",
        default=None,
        help="Remove a previously installed MCP client config entry.",
    )
    parser.add_argument(
        "--config",
        action="store_true",
        help="Print example MCP config JSON (stdio + streamable-http + sse).",
    )
    parser.add_argument(
        "--list-clients",
        action="store_true",
        help="List supported MCP client config targets.",
    )
    parser.add_argument(
        "--transport",
        default=None,
        help="For --install/--config: stdio, streamable-http, sse, or a URL.",
    )
    parser.add_argument(
        "--scope",
        choices=["global", "project"],
        default=None,
        help="Install scope: global (default) or project.",
    )

    sub = parser.add_subparsers(dest="command")
    install_plugin = sub.add_parser(
        "install-plugin",
        help="Copy the plugin DLL into the dnSpy install",
    )
    install_plugin.add_argument("--dnspy-home", type=Path, required=True)
    return parser


def _read_ui_html() -> str:
    return (
        pkg_resources.files("dnspy_mcp.ui")
        .joinpath("instances.html")
        .read_text("utf-8")
    )


class _McpHttpHandler(BaseHTTPRequestHandler):
    supervisor: Supervisor  # set by serve_http() / start_sidecar_ui()

    def log_message(self, *_: Any, **__: Any) -> None:  # quiet
        pass

    def _send_json(self, status: int, payload: Any) -> None:
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _send_html(self, status: int, text: str) -> None:
        body = text.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("X-Frame-Options", "DENY")
        self.send_header(
            "Content-Security-Policy",
            "default-src 'self'; script-src 'self' 'unsafe-inline'; "
            "style-src 'self' 'unsafe-inline'; frame-ancestors 'none'",
        )
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:  # noqa: N802
        if self.path in ("/", "/instances"):
            self._send_html(200, _read_ui_html())
            return
        if self.path == "/api/instances":
            self._send_json(
                200, {"sessions": self.supervisor.list_instances()}
            )
            return
        self._send_json(404, {"error": "not found"})

    def do_POST(self) -> None:  # noqa: N802
        if self.path == "/mcp":
            length = int(self.headers.get("Content-Length", "0"))
            envelope = json.loads(
                self.rfile.read(length).decode("utf-8")
            )
            self._send_json(200, self.supervisor.handle_envelope(envelope))
            return
        if self.path.startswith("/api/instances/") and self.path.endswith(
            "/close"
        ):
            sid = self.path[len("/api/instances/") : -len("/close")]
            try:
                self.supervisor.close(sid)
            except KeyError:
                self._send_json(
                    404, {"error": f"unknown session {sid}"}
                )
                return
            self._send_json(200, {"closed": True})
            return
        self._send_json(404, {"error": "not found"})


def serve_http(supervisor: Supervisor, *, host: str, port: int) -> None:
    _McpHttpHandler.supervisor = supervisor
    httpd = ThreadingHTTPServer((host, port), _McpHttpHandler)
    try:
        httpd.serve_forever()
    finally:
        httpd.server_close()


def serve_stdio(supervisor: Supervisor) -> None:
    """Read newline-delimited JSON-RPC envelopes from stdin, write to stdout."""
    while True:
        line = sys.stdin.readline()
        if not line:
            return
        line = line.strip()
        if not line:
            continue
        try:
            envelope = json.loads(line)
        except json.JSONDecodeError:
            err = {
                "jsonrpc": "2.0",
                "id": None,
                "error": {"code": -32700, "message": "parse error"},
            }
            sys.stdout.write(json.dumps(err) + "\n")
            sys.stdout.flush()
            continue
        resp = supervisor.handle_envelope(envelope)
        sys.stdout.write(json.dumps(resp) + "\n")
        sys.stdout.flush()


def start_sidecar_ui(
    supervisor: Supervisor, *, host: str, port: int
) -> str:
    """Start the HTTP UI on a background thread; return the bound URL."""
    _McpHttpHandler.supervisor = supervisor
    try:
        httpd = ThreadingHTTPServer((host, port), _McpHttpHandler)
    except OSError:
        # Fall back to an ephemeral port if the configured one is busy.
        httpd = ThreadingHTTPServer((host, 0), _McpHttpHandler)
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    actual_host, actual_port = httpd.server_address[:2]
    return f"http://{actual_host}:{actual_port}/instances"


def main(argv: list[str] | None = None) -> None:
    parser = build_parser()
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.DEBUG if args.verbose else logging.INFO)

    if args.command == "install-plugin":
        dest = install_plugin_dll(args.dnspy_home)
        print(f"Plugin installed to {dest}")
        return

    if args.list_clients:
        list_available_clients()
        return

    is_install = args.install is not None
    is_uninstall = args.uninstall is not None
    if args.scope and not (is_install or is_uninstall):
        raise SystemExit("--scope requires --install or --uninstall")
    if is_install and is_uninstall:
        raise SystemExit("Cannot install and uninstall at the same time")
    if is_install or is_uninstall:
        run_install_command(
            uninstall=is_uninstall,
            targets_str=args.install if is_install else args.uninstall,
            args=args,
        )
        return

    if args.config:
        print_mcp_config(args)
        return

    from dnspy_mcp.config import generate_token, pick_free_port
    from dnspy_mcp.worker import Worker

    def worker_factory(session_id: str) -> Worker:
        port = pick_free_port(start=args.port + 1)
        token = generate_token()
        dnspy_exe = (
            str(args.dnspy_home / "dnSpy.exe")
            if args.dnspy_home
            else "dnSpy.exe"
        )
        return Worker(
            session_id=session_id,
            dnspy_exe=dnspy_exe,
            port=port,
            token=token,
        )

    snapshot_path = (
        Path(__file__).parent / "plugin_assets" / "tools-schema.json"
    )
    supervisor = Supervisor(
        worker_factory=worker_factory,
        max_workers=args.max_workers,
        schema_snapshot_path=snapshot_path,
        isolated_contexts=args.isolated_contexts,
    )
    supervisor.start_reaper()
    try:
        if args.stdio:
            url = start_sidecar_ui(
                supervisor, host=args.host, port=args.port
            )
            print(f"Instance UI: {url}", file=sys.stderr, flush=True)
            serve_stdio(supervisor)
        else:
            print(
                f"Instance UI: http://{args.host}:{args.port}/instances",
                file=sys.stderr,
                flush=True,
            )
            serve_http(supervisor, host=args.host, port=args.port)
    finally:
        supervisor.stop_reaper()
