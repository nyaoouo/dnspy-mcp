"""Plugin tool-schema snapshot loader and live-diff utilities."""
from __future__ import annotations

import copy
import json
from pathlib import Path
from typing import Any


def load_snapshot(path: Path) -> dict[str, Any]:
    """Read a ``tools-schema.json`` snapshot. Raises on malformed input."""
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    if "tools" not in data or not isinstance(data["tools"], list):
        raise ValueError(f"Malformed schema snapshot: {path}")
    return data


def inject_instance_field(tool: dict[str, Any]) -> dict[str, Any]:
    """Return a copy of ``tool`` with an optional ``instance`` property added.

    If the tool already defines ``instance``, leave it alone.
    """
    out = copy.deepcopy(tool)
    schema = out.setdefault("inputSchema", {"type": "object", "properties": {}})
    props = schema.setdefault("properties", {})
    props.setdefault(
        "instance",
        {
            "type": "string",
            "description": (
                "Optional session id selecting a dnSpy instance. Defaults to the "
                "current bound instance for this context."
            ),
        },
    )
    return out


def diff_against_live(
    snapshot: dict[str, Any], live_tools: list[dict[str, Any]]
) -> list[str]:
    """Return a list of human-readable diff lines.

    Empty list means snapshot and live are aligned (by name only).
    """
    snap_names = {tool["name"] for tool in snapshot["tools"]}
    live_names = {tool["name"] for tool in live_tools}
    out: list[str] = []
    for missing in sorted(snap_names - live_names):
        out.append(f"snapshot has '{missing}' but plugin does not expose it")
    for extra in sorted(live_names - snap_names):
        out.append(f"plugin exposes '{extra}' but snapshot does not advertise it")
    return out
