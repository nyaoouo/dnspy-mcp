"""Vendored MCP client config-file locations and JSON shape rules.

This module is import-time pure: paths are computed once from env vars and
the user's home directory. It avoids any external dependency so dnspy-mcp
can stay zero-deps.

Each client maps to (config_dir, config_file). For global scope, config_dir
is absolute. For project scope, config_dir is relative to the user's CWD
(joined at call time).

Most clients put servers under top-level "mcpServers". VS Code is the only
exception — see SPECIAL_JSON_STRUCTURES.
"""
from __future__ import annotations

import os
from pathlib import Path

MCP_SERVER_NAME = "dnspy-mcp"

# Hook for future server-name migrations. Empty for v1.
OLD_SERVER_NAMES: set[str] = set()


def _home() -> str:
    return str(Path.home())


def _appdata() -> str:
    return os.environ.get("APPDATA", str(Path.home() / "AppData" / "Roaming"))


GLOBAL_CONFIGS: dict[str, tuple[str, str]] = {
    "Claude":           (os.path.join(_appdata(), "Claude"),                    "claude_desktop_config.json"),
    "Claude Code":      (_home(),                                               ".claude.json"),
    "Cursor":           (os.path.join(_home(), ".cursor"),                      "mcp.json"),
    "Windsurf":         (os.path.join(_home(), ".codeium", "windsurf"),         "mcp_config.json"),
    "VS Code":          (os.path.join(_appdata(), "Code", "User"),              "settings.json"),
    "VS Code Insiders": (os.path.join(_appdata(), "Code - Insiders", "User"),   "settings.json"),
}

PROJECT_CONFIGS: dict[str, tuple[str, str]] = {
    # Claude Code's project config is the literal dotfile ".mcp.json" in the project root.
    "Claude Code":      (".",                              ".mcp.json"),
    "Cursor":           (os.path.join(".", ".cursor"),   "mcp.json"),
    "Windsurf":         (os.path.join(".", ".windsurf"), "mcp.json"),
    "VS Code":          (os.path.join(".", ".vscode"),   "mcp.json"),
    "VS Code Insiders": (os.path.join(".", ".vscode"),   "mcp.json"),
}

PROJECT_LEVEL_CONFIGS: set[str] = set(PROJECT_CONFIGS.keys())

# Clients whose MCP entries are NOT under top-level "mcpServers".
# Mapping: client_name -> (top_key | None, nested_key)
#   - If top_key is None: config[nested_key]
#   - Else:               config[top_key][nested_key]
SPECIAL_JSON_STRUCTURES: dict[str, tuple[str | None, str]] = {
    "VS Code":          ("mcp", "servers"),
    "VS Code Insiders": ("mcp", "servers"),
}

PROJECT_SPECIAL_JSON_STRUCTURES: dict[str, tuple[str | None, str]] = {
    "VS Code":          (None, "servers"),
    "VS Code Insiders": (None, "servers"),
}

# Allow aliases like "vscode" and "claude-code" to resolve to canonical names.
_ALIASES: dict[str, str] = {
    "vscode":           "VS Code",
    "vs-code":          "VS Code",
    "vscode-insiders":  "VS Code Insiders",
    "vs-code-insiders": "VS Code Insiders",
    "claude-code":      "Claude Code",
    "claudecode":       "Claude Code",
    "claude-desktop":   "Claude",
}


def resolve_client_name(target: str, available: list[str]) -> str | None:
    """Case-insensitive client-name lookup with hyphen-insensitive aliases.

    Returns the canonical name from `available`, or None when no match.
    """
    target_norm = target.strip().lower()
    if not target_norm:
        return None
    by_lower = {name.lower(): name for name in available}
    if target_norm in by_lower:
        return by_lower[target_norm]
    aliased = _ALIASES.get(target_norm.replace(" ", "-"))
    if aliased is not None and aliased in available:
        return aliased
    return None
