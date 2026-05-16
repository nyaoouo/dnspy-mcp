"""End-to-end smoke driver: hits supervisor over HTTP, exercises the full chain.

Assumes dnspy-mcp is running on the host/port given via CLI (default 127.0.0.1:8746).
"""
from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.request


def rpc(url: str, body: dict, timeout: float = 30.0) -> dict:
    data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default="http://127.0.0.1:8746/mcp")
    parser.add_argument(
        "--target",
        default=r"C:\Data\projects\dnspy-mcp\.claude\tools\dnSpy\bin\dnSpy.Contracts.DnSpy.dll",
    )
    args = parser.parse_args()

    print(f"=== supervisor at {args.url} ===\n")

    print("[1] tools/list (before open)")
    resp = rpc(args.url, {"jsonrpc": "2.0", "id": 1, "method": "tools/list"})
    tools = resp["result"]["tools"]
    print(f"    {len(tools)} tools advertised")
    for t in tools:
        print(f"      - {t['name']}")
    print()

    print("[2] dnspy_open")
    t0 = time.monotonic()
    resp = rpc(
        args.url,
        {
            "jsonrpc": "2.0",
            "id": 2,
            "method": "tools/call",
            "params": {
                "name": "dnspy_open",
                "arguments": {"session_id": "smoke-a"},
            },
        },
        timeout=90.0,
    )
    open_dt = time.monotonic() - t0
    print(f"    response in {open_dt:.1f}s: {json.dumps(resp['result'])[:200]}")
    print()

    print("[3] dnspy_list")
    resp = rpc(args.url, {
        "jsonrpc": "2.0", "id": 3,
        "method": "tools/call",
        "params": {"name": "dnspy_list", "arguments": {}},
    })
    print(f"    {json.dumps(resp['result']['structuredContent'])}")
    print()

    print(f"[4] load_assembly {args.target}")
    resp = rpc(args.url, {
        "jsonrpc": "2.0", "id": 4,
        "method": "tools/call",
        "params": {
            "name": "load_assembly",
            "arguments": {"instance": "smoke-a", "path": args.target},
        },
    })
    print(f"    isError={resp['result']['isError']}")
    print(f"    text={resp['result']['content'][0]['text'][:200]}")
    print()

    print("[5] list_assemblies (after load)")
    resp = rpc(args.url, {
        "jsonrpc": "2.0", "id": 5,
        "method": "tools/call",
        "params": {
            "name": "list_assemblies",
            "arguments": {"instance": "smoke-a"},
        },
    })
    asms = json.loads(resp["result"]["content"][0]["text"])
    print(f"    {len(asms)} assemblies loaded:")
    for a in asms:
        print(f"      {a['name']:32s} <- {a['path']}")
    print()

    print("[6] dnspy_close")
    resp = rpc(args.url, {
        "jsonrpc": "2.0", "id": 6,
        "method": "tools/call",
        "params": {
            "name": "dnspy_close",
            "arguments": {"session_id": "smoke-a"},
        },
    })
    print(f"    {json.dumps(resp['result'])}")
    print()

    print("=== smoke complete ===")
    return 0


if __name__ == "__main__":
    sys.exit(main())
