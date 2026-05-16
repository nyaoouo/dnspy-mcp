import re
import socket
import tempfile
import unittest
from pathlib import Path

from dnspy_mcp.config import (
    default_dnspy_home_from_env,
    generate_token,
    pick_free_port,
    validate_dnspy_home,
)


class GenerateTokenTests(unittest.TestCase):
    def test_token_is_url_safe(self) -> None:
        token = generate_token()
        self.assertRegex(token, r"^[A-Za-z0-9_-]+$")

    def test_token_minimum_length(self) -> None:
        token = generate_token()
        self.assertGreaterEqual(len(token), 32)

    def test_tokens_are_unique(self) -> None:
        seen = {generate_token() for _ in range(100)}
        self.assertEqual(len(seen), 100)


class PickFreePortTests(unittest.TestCase):
    def test_returns_free_port(self) -> None:
        port = pick_free_port(start=self._reserve_free_port())
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            sock.bind(("127.0.0.1", port))

    def test_skips_busy_port(self) -> None:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as busy:
            busy.bind(("127.0.0.1", 0))
            busy.listen(1)
            busy_port = busy.getsockname()[1]
            picked = pick_free_port(start=busy_port)
            self.assertGreater(picked, busy_port)

    def test_raises_when_range_exhausted(self) -> None:
        sockets: list[socket.socket] = []
        try:
            for _ in range(5):
                sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                sock.bind(("127.0.0.1", 0))
                sock.listen(1)
                sockets.append(sock)
            ports = sorted(s.getsockname()[1] for s in sockets)
            if ports != list(range(ports[0], ports[0] + 5)):
                self.skipTest(
                    "OS did not allocate five contiguous free ports; rerun"
                )
            with self.assertRaises(RuntimeError):
                pick_free_port(start=ports[0], max_attempts=5)
        finally:
            for sock in sockets:
                sock.close()

    @staticmethod
    def _reserve_free_port() -> int:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
            probe.bind(("127.0.0.1", 0))
            return probe.getsockname()[1]


class ValidateDnspyHomeTests(unittest.TestCase):
    def _make_fake_home(self, parent: Path) -> Path:
        home = parent / "dnSpy"
        home.mkdir(parents=True)
        (home / "dnSpy.exe").write_bytes(b"")
        return home

    def test_returns_resolved_path(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            home = self._make_fake_home(Path(tmp))
            result = validate_dnspy_home(str(home))
            self.assertEqual(result, home.resolve())

    def test_rejects_missing_dir(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            missing = Path(tmp) / "no_such_subdir"
            with self.assertRaises(ValueError):
                validate_dnspy_home(missing)

    def test_rejects_dir_without_dnspy_exe(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ValueError):
                validate_dnspy_home(tmp)

    def test_default_from_env(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            home = self._make_fake_home(Path(tmp))
            env = {"DNSPY_HOME": str(home)}
            self.assertEqual(default_dnspy_home_from_env(env), home)

    def test_default_from_env_missing(self) -> None:
        self.assertIsNone(default_dnspy_home_from_env({}))


if __name__ == "__main__":
    unittest.main()
