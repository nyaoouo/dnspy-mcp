"""Configuration helpers: tokens, ports, dnspy-home validation."""
from __future__ import annotations

import secrets
import socket
from pathlib import Path
from typing import MutableMapping


def generate_token() -> str:
    """Return a URL-safe random token of at least 32 chars (24 bytes of entropy)."""
    return secrets.token_urlsafe(24)


def pick_free_port(start: int = 8746, max_attempts: int = 256) -> int:
    """Scan from ``start`` upward and return the first port that binds.

    Raises ``RuntimeError`` if no free port is found within ``max_attempts``.
    """
    for offset in range(max_attempts):
        port = start + offset
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            try:
                sock.bind(("127.0.0.1", port))
            except OSError:
                continue
            return port
    raise RuntimeError(
        f"No free port in [{start}, {start + max_attempts}) for dnspy-mcp worker"
    )


def validate_dnspy_home(dnspy_home: str | Path) -> Path:
    """Resolve and validate a dnSpyEx installation directory.

    Requires the directory to exist and contain ``bin\\dnSpy.exe``.
    """
    home = Path(dnspy_home).expanduser().resolve()
    if not home.is_dir():
        raise ValueError(f"dnSpy home is not a directory: {home}")
    exe = home / "dnSpy.exe"
    if not exe.is_file():
        raise ValueError(f"dnSpy home does not contain dnSpy.exe: {home}")
    return home


def default_dnspy_home_from_env(
    env: MutableMapping[str, str] | None = None,
) -> Path | None:
    """Return ``DNSPY_HOME`` or ``DNSPYEX_HOME`` as ``Path`` if set, else None."""
    import os

    target = env if env is not None else os.environ
    value = target.get("DNSPY_HOME") or target.get("DNSPYEX_HOME")
    return Path(value) if value else None
