"""Install the plugin DLL into a dnSpyEx installation."""
from __future__ import annotations

import json
import os
import shutil
import sys
import tempfile
from pathlib import Path
from typing import Any
from urllib.parse import urlparse, urlunparse

from dnspy_mcp.config import validate_dnspy_home
from dnspy_mcp.installer_data import (
    MCP_SERVER_NAME,
    GLOBAL_CONFIGS,
    PROJECT_CONFIGS,
    PROJECT_LEVEL_CONFIGS,
    PROJECT_SPECIAL_JSON_STRUCTURES,
    SPECIAL_JSON_STRUCTURES,
    OLD_SERVER_NAMES,
    resolve_client_name,
)

PLUGIN_DLL_NAME = "dnspy_mcp.x.dll"
"""dnSpy recognizes extension DLLs by the ``.x.dll`` suffix."""

EXTENSION_DIR_NAME = "dnspy-mcp"

PLUGIN_TFM_NET8 = "net8.0-windows"
PLUGIN_TFM_NET48 = "net48"


def detect_plugin_tfm(dnspy_home: str | Path | None) -> str:
    """Pick the plugin asset TFM that matches a dnSpy install.

    The modern .NET dnSpy build (e.g. ``dnSpy-net-win64``) is
    self-contained and ships ``netstandard.dll`` in ``<home>/bin/``;
    the .NET Framework 4.8 build does not. ``None`` falls back to the
    legacy ``net48`` path to preserve back-compat for callers that
    don't yet pass a home.
    """
    if dnspy_home is None:
        return PLUGIN_TFM_NET48
    home = Path(dnspy_home)
    if (home / "bin" / "netstandard.dll").is_file():
        return PLUGIN_TFM_NET8
    return PLUGIN_TFM_NET48


def plugin_dll_source(dnspy_home: str | Path | None = None) -> Path:
    """Return the path to the bundled plugin DLL (may not yet exist in dev tree).

    When ``dnspy_home`` is given, the asset folder matching that dnSpy
    variant is returned; otherwise the legacy ``net48`` folder is used
    to preserve existing call sites.
    """
    package_root = Path(__file__).resolve().parent
    tfm = detect_plugin_tfm(dnspy_home)
    return package_root / "plugin_assets" / tfm / PLUGIN_DLL_NAME


def install_plugin_dll(
    dnspy_home: str | Path,
    *,
    source_dll: Path | None = None,
    source_dir: Path | None = None,
) -> Path:
    """Copy the bundled plugin DLL(s) into ``<dnspy-home>\\bin\\Extensions\\dnspy-mcp\\``.

    Returns the path of the main plugin DLL after copy. Overwrites existing files.

    Modes:
    - ``source_dll`` (single file): legacy/test path; copies just that one DLL.
    - ``source_dir`` (directory): copies every ``*.dll`` in the directory.
    - Neither: defaults to the bundled ``plugin_assets/<tfm>/`` directory matching
      the variant of ``dnspy_home`` (modern .NET 8 vs .NET Framework 4.8). The
      directory is expected to contain the plugin DLL plus its dependency DLLs.
    """
    if source_dll is not None and source_dir is not None:
        raise ValueError("Pass either source_dll or source_dir, not both")

    home = validate_dnspy_home(dnspy_home)
    dest_dir = home / "bin" / "Extensions" / EXTENSION_DIR_NAME
    dest_dir.mkdir(parents=True, exist_ok=True)

    if source_dll is not None:
        if not source_dll.is_file():
            raise FileNotFoundError(
                f"Plugin DLL not found at {source_dll}; build the C# plugin first."
            )
        dest = dest_dir / PLUGIN_DLL_NAME
        shutil.copyfile(source_dll, dest)
        return dest

    # Directory mode
    src_dir = source_dir if source_dir is not None else plugin_dll_source(home).parent
    if not src_dir.is_dir():
        raise FileNotFoundError(
            f"Plugin source directory not found at {src_dir}; "
            "build the C# plugin first."
        )
    dlls = sorted(src_dir.glob("*.dll"))
    if not dlls:
        raise FileNotFoundError(
            f"No DLLs found in {src_dir}; build the C# plugin first."
        )
    plugin_dest = dest_dir / PLUGIN_DLL_NAME
    for src in dlls:
        shutil.copyfile(src, dest_dir / src.name)
    return plugin_dest



def get_python_executable() -> str:
    """Return the venv's python.exe when running inside a venv, else sys.executable.

    Used so the MCP client's spawned subprocess can find the dnspy_mcp install
    even when the user installed dnspy-mcp into a venv that isn't on PATH.
    """
    venv = os.environ.get("VIRTUAL_ENV")
    if venv:
        python = Path(venv) / ("Scripts/python.exe" if sys.platform == "win32" else "bin/python3")
        if python.exists():
            return str(python)
    return sys.executable


_PYTHON_ENV_VARS = (
    "PYTHONHOME",
    "PYTHONPATH",
    "PYTHONSAFEPATH",
    "PYTHONPLATLIBDIR",
    "PYTHONPYCACHEPREFIX",
    "PYTHONNOUSERSITE",
    "PYTHONUSERBASE",
)


def copy_python_env(env: dict[str, str]) -> bool:
    """Copy set Python-runtime env vars into `env`. Returns True if any were copied."""
    copied = False
    for var in _PYTHON_ENV_VARS:
        value = os.environ.get(var)
        if value:
            env[var] = value
            copied = True
    return copied


def normalize_transport_url(transport: str) -> str:
    """Validate a user-supplied URL and force a non-empty path (default /mcp)."""
    url = urlparse(transport)
    if url.hostname is None or url.port is None:
        raise ValueError(f"Invalid transport URL: {transport}")
    path = url.path or "/mcp"
    if path == "/":
        path = "/mcp"
    return urlunparse((url.scheme, f"{url.hostname}:{url.port}", path, "", "", ""))


def force_mcp_path(transport_url: str) -> str:
    """Replace whatever path a URL has with /mcp (used for clients that ignore /sse)."""
    url = urlparse(transport_url)
    return urlunparse((url.scheme, f"{url.hostname}:{url.port}", "/mcp", "", "", ""))


def resolve_transport_url(transport: str | None, *, host: str, port: int) -> str:
    """Turn a transport name or URL into a concrete URL.

    None / "http" / "streamable-http" / "streamable" → http://host:port/mcp
    "sse"                                            → http://host:port/sse
    anything else                                    → treated as a URL, validated.
    """
    if transport in (None, "http", "streamable-http", "streamable"):
        return f"http://{host}:{port}/mcp"
    if transport == "sse":
        return f"http://{host}:{port}/sse"
    return normalize_transport_url(transport)


DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 8746


def _stdio_args(*, dnspy_home: str | Path | None) -> list[str]:
    args = ["-m", "dnspy_mcp", "--stdio"]
    if dnspy_home is not None:
        args.extend(["--dnspy-home", str(dnspy_home)])
    return args


def _infer_http_transport_type(transport_url: str) -> str:
    return "sse" if urlparse(transport_url).path.rstrip("/") == "/sse" else "http"


def generate_mcp_config(
    *,
    client_name: str,
    transport: str = "stdio",
    host: str = DEFAULT_HOST,
    port: int = DEFAULT_PORT,
    dnspy_home: str | Path | None = None,
) -> dict[str, Any]:
    """Return the per-client JSON snippet for one MCP server entry.

    Stdio uses the venv's python.exe so the install survives venv-only setups.
    HTTP / SSE snippets are tuned per-client (Claude gets "type", Cursor doesn't).
    """
    if transport == "stdio":
        args = _stdio_args(dnspy_home=dnspy_home)
        mcp_config: dict[str, Any] = {
            "command": get_python_executable(),
            "args": args,
        }
        env: dict[str, str] = {}
        if copy_python_env(env):
            mcp_config["env"] = env
        return mcp_config

    transport_url = resolve_transport_url(transport, host=host, port=port)

    if client_name in ("Claude", "Claude Code"):
        return {"type": _infer_http_transport_type(transport_url), "url": transport_url}
    if client_name in ("VS Code", "VS Code Insiders"):
        return {"type": _infer_http_transport_type(transport_url), "url": force_mcp_path(transport_url)}
    # Cursor, Windsurf, generic fallback.
    return {"url": transport_url}


def _read_config_file(config_path: str) -> dict | None:
    """Return parsed JSON dict, empty dict for empty file, or None on invalid JSON / IO error."""
    try:
        with open(config_path, "r", encoding="utf-8") as f:
            data = f.read().strip()
        return json.loads(data) if data else {}
    except (json.JSONDecodeError, OSError):
        return None


def _write_config_file(config_path: str, config: dict) -> None:
    """Atomically write `config` as pretty-printed JSON to `config_path`.

    Uses tempfile.mkstemp in the same directory followed by os.replace so
    readers never observe a half-written file. Creates parent dirs.
    """
    config_dir = os.path.dirname(config_path) or "."
    os.makedirs(config_dir, exist_ok=True)
    fd, temp_path = tempfile.mkstemp(
        dir=config_dir, prefix=".tmp_", suffix=".json", text=True
    )
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(config, f, indent=2)
        os.replace(temp_path, config_path)
    except Exception:
        try:
            os.unlink(temp_path)
        except OSError:
            pass
        raise


def _get_mcp_servers_view(
    config: dict,
    *,
    client_name: str,
    special_json_structures: dict[str, tuple[str | None, str]],
) -> dict:
    """Return the (mutable) dict where this client expects MCP servers to live."""
    if client_name in special_json_structures:
        top_key, nested_key = special_json_structures[client_name]
        if top_key is None:
            return config.setdefault(nested_key, {})
        return config.setdefault(top_key, {}).setdefault(nested_key, {})
    return config.setdefault("mcpServers", {})


def _get_scope_config_spec(
    *, project: bool, project_dir: str | None = None,
) -> tuple[dict[str, tuple[str, str]], dict[str, tuple[str | None, str]]]:
    """Return (configs, special_structures) for the requested scope.

    For project scope, config_dir entries are made absolute by joining
    onto `project_dir` (defaults to os.getcwd()).
    """
    if project:
        base = project_dir or os.getcwd()
        resolved = {
            name: (os.path.normpath(os.path.join(base, rel_dir)), file)
            for name, (rel_dir, file) in PROJECT_CONFIGS.items()
        }
        return resolved, PROJECT_SPECIAL_JSON_STRUCTURES
    return GLOBAL_CONFIGS, SPECIAL_JSON_STRUCTURES


def is_client_installed(
    name: str, config_dir: str, config_file: str, *, project: bool = False
) -> bool:
    """Return True if this client's config currently contains a dnspy-mcp entry."""
    config_path = os.path.join(config_dir, config_file)
    if not os.path.exists(config_path):
        return False
    config = _read_config_file(config_path)
    if config is None:
        return False
    _, special = _get_scope_config_spec(project=project)
    mcp_servers = _get_mcp_servers_view(
        config, client_name=name, special_json_structures=special
    )
    return MCP_SERVER_NAME in mcp_servers


def list_available_clients() -> None:
    """Print every supported client and whether its config directory exists."""
    if not GLOBAL_CONFIGS:
        print(f"Unsupported platform: {sys.platform}")
        return

    print("Available installation targets:\n")
    print("  MCP Clients:")
    for name, (config_dir, _) in GLOBAL_CONFIGS.items():
        supports_project = name in PROJECT_LEVEL_CONFIGS
        project_marker = " [supports --scope project]" if supports_project else ""
        status = "found" if os.path.exists(config_dir) else "not found"
        print(f"    {name:<20} ({status}){project_marker}")

    print()
    print("Usage examples:")
    print("  dnspy-mcp --install cursor --scope project --dnspy-home D:/tools/dnSpyEx")
    print("  dnspy-mcp --install claude --transport stdio --dnspy-home D:/tools/dnSpyEx")
    print("  dnspy-mcp --uninstall cursor --scope project")
    print("  dnspy-mcp --config --dnspy-home D:/tools/dnSpyEx")


def _args_kwargs(args) -> dict[str, Any]:
    return {
        "host": getattr(args, "host", DEFAULT_HOST),
        "port": getattr(args, "port", DEFAULT_PORT),
        "dnspy_home": getattr(args, "dnspy_home", None),
    }


def print_mcp_config(args) -> None:
    """Print copy-paste-ready MCP config JSON for each transport."""
    kwargs = _args_kwargs(args)
    for title, transport in (
        ("STDIO MCP CONFIGURATION", "stdio"),
        ("STREAMABLE HTTP MCP CONFIGURATION", "streamable-http"),
        ("SSE MCP CONFIGURATION", "sse"),
    ):
        print(f"[{title}]")
        print(
            json.dumps(
                {
                    "mcpServers": {
                        MCP_SERVER_NAME: generate_mcp_config(
                            client_name="Generic",
                            transport=transport,
                            **kwargs,
                        )
                    }
                },
                indent=2,
            )
        )
        print()


def _resolve_client_targets(
    configs: dict[str, tuple[str, str]], only: list[str] | None
) -> dict[str, tuple[str, str]]:
    if only is None:
        return configs
    available = list(configs.keys())
    filtered: dict[str, tuple[str, str]] = {}
    for target in only:
        resolved = resolve_client_name(target, available)
        if resolved is None:
            print(f"Unknown client: {target!r}. Use --list-clients to see available targets.")
        elif resolved not in filtered:
            filtered[resolved] = configs[resolved]
    return filtered


def install_mcp_servers(
    *,
    args,
    transport: str = "streamable-http",
    uninstall: bool = False,
    quiet: bool = False,
    only: list[str] | None = None,
    project: bool = False,
) -> None:
    """Install or remove the dnspy-mcp entry in each named client's config file."""
    configs, special_json_structures = _get_scope_config_spec(project=project)
    if not configs:
        print(f"Unsupported platform: {sys.platform}")
        return

    configs = _resolve_client_targets(configs, only)
    if not configs:
        return

    changed = 0
    for name, (config_dir, config_file) in configs.items():
        config_path = os.path.join(config_dir, config_file)

        if not os.path.exists(config_dir):
            if project and not uninstall:
                os.makedirs(config_dir, exist_ok=True)
            else:
                action = "uninstall" if uninstall else "installation"
                if not quiet:
                    print(f"Skipping {name} {action}\n  Config: {config_path} (not found)")
                continue

        config: dict[str, Any] = {}
        if os.path.exists(config_path):
            loaded = _read_config_file(config_path)
            if loaded is None:
                if not quiet:
                    action = "uninstall" if uninstall else "installation"
                    print(f"Skipping {name} {action}\n  Config: {config_path} (invalid JSON)")
                continue
            config = loaded

        mcp_servers = _get_mcp_servers_view(
            config,
            client_name=name,
            special_json_structures=special_json_structures,
        )
        # Future-proof rename hook: migrate any legacy key to the canonical name.
        for old_name in OLD_SERVER_NAMES:
            if old_name in mcp_servers:
                mcp_servers[MCP_SERVER_NAME] = mcp_servers.pop(old_name)

        if uninstall:
            if MCP_SERVER_NAME not in mcp_servers:
                if not quiet:
                    print(f"Skipping {name} uninstall\n  Config: {config_path} (not installed)")
                continue
            del mcp_servers[MCP_SERVER_NAME]
        else:
            mcp_servers[MCP_SERVER_NAME] = generate_mcp_config(
                client_name=name,
                transport=transport,
                **_args_kwargs(args),
            )

        _write_config_file(config_path, config)
        if not quiet:
            verb = "Uninstalled" if uninstall else "Installed"
            print(f"{verb} {name} MCP server (restart required)\n  Config: {config_path}")
        changed += 1

    if not uninstall and changed == 0 and only is None:
        print("No MCP servers installed. For unsupported MCP clients, use this config:\n")
        print_mcp_config(args)


def _resolve_transport(value: str | None) -> str:
    if value is None:
        return "streamable-http"
    lowered = value.strip().lower()
    if lowered == "stdio":
        return "stdio"
    if lowered == "sse":
        return "sse"
    if lowered in ("http", "streamable-http", "streamable"):
        return "streamable-http"
    return value  # treat as URL; resolve_transport_url validates later


def _parse_client_targets(targets_str: str) -> list[str]:
    return [t.strip() for t in targets_str.split(",") if t.strip()]


def run_install_command(*, uninstall: bool, targets_str: str, args) -> None:
    """Top-level orchestrator called from server.main()."""
    if not targets_str:
        action = "uninstall" if uninstall else "install"
        print(
            f"No targets specified for --{action}. "
            f"Use --{action} claude,cursor or run --list-clients to see options."
        )
        return

    transport = "stdio" if uninstall else _resolve_transport(getattr(args, "transport", None))
    scope = getattr(args, "scope", None) or "global"

    install_mcp_servers(
        args=args,
        transport=transport,
        uninstall=uninstall,
        only=_parse_client_targets(targets_str),
        project=(scope == "project"),
    )
