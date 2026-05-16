import shutil
import tempfile
import unittest
from pathlib import Path

from dnspy_mcp.installer import install_plugin_dll, plugin_dll_source


class PluginDllSourceTests(unittest.TestCase):
    def test_returns_packaged_dll_path(self) -> None:
        path = plugin_dll_source()
        self.assertEqual(path.parent.name, "net48")
        self.assertTrue(path.name.endswith(".x.dll"))

    def test_returns_net48_for_framework_home(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            home = Path(tmp) / "dnSpy"
            (home / "bin").mkdir(parents=True)
            (home / "dnSpy.exe").write_bytes(b"")
            path = plugin_dll_source(home)
            self.assertEqual(path.parent.name, "net48")
            self.assertTrue(path.name.endswith(".x.dll"))

    def test_returns_net8_for_modern_home(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            home = Path(tmp) / "dnSpy"
            (home / "bin").mkdir(parents=True)
            (home / "dnSpy.exe").write_bytes(b"")
            (home / "bin" / "netstandard.dll").write_bytes(b"\x4d\x5a")
            path = plugin_dll_source(home)
            self.assertEqual(path.parent.name, "net8.0-windows")
            self.assertTrue(path.name.endswith(".x.dll"))


class InstallPluginDllTests(unittest.TestCase):
    def _fake_home(self, parent: Path) -> Path:
        home = parent / "dnSpy"
        home.mkdir(parents=True)
        (home / "dnSpy.exe").write_bytes(b"")
        return home

    def _fake_dll(self, parent: Path) -> Path:
        src = parent / "fake_dnspy_mcp.x.dll"
        src.write_bytes(b"\x4d\x5a")  # MZ header bytes; content doesn't matter
        return src

    def test_copies_to_extensions_dir(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            home = self._fake_home(tmp_path)
            src = self._fake_dll(tmp_path)
            dest = install_plugin_dll(home, source_dll=src)
            self.assertTrue(dest.exists())
            self.assertEqual(
                dest,
                home / "bin" / "Extensions" / "dnspy-mcp" / "dnspy_mcp.x.dll",
            )

    def test_overwrites_existing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            home = self._fake_home(tmp_path)
            src = self._fake_dll(tmp_path)
            dest = install_plugin_dll(home, source_dll=src)
            dest.write_bytes(b"OLD")
            install_plugin_dll(home, source_dll=src)
            self.assertEqual(dest.read_bytes()[:2], b"\x4d\x5a")

    def test_rejects_invalid_home(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            src = self._fake_dll(Path(tmp))
            with self.assertRaises(ValueError):
                install_plugin_dll(Path(tmp), source_dll=src)


class GenerateMcpConfigBackwardCompatTests(unittest.TestCase):
    """Pin the shape so callers that used to use build_client_config still work."""

    def test_stdio_has_dnspy_home_arg(self) -> None:
        from dnspy_mcp.installer import generate_mcp_config
        cfg = generate_mcp_config(
            client_name="Claude",
            transport="stdio",
            dnspy_home=Path("C:/dnSpy"),
        )
        self.assertIn("--stdio", cfg["args"])
        self.assertIn("--dnspy-home", cfg["args"])

    def test_http_has_url(self) -> None:
        from dnspy_mcp.installer import generate_mcp_config
        cfg = generate_mcp_config(
            client_name="Claude",
            transport="streamable-http",
            host="127.0.0.1",
            port=8746,
        )
        self.assertEqual(cfg["url"], "http://127.0.0.1:8746/mcp")


class InstallPluginDirectoryTests(unittest.TestCase):
    def _fake_home(self, parent: Path) -> Path:
        home = parent / "dnSpy"
        home.mkdir(parents=True)
        (home / "dnSpy.exe").write_bytes(b"")
        return home

    def _fake_source_dir(self, parent: Path) -> Path:
        src = parent / "build"
        src.mkdir()
        (src / "dnspy_mcp.x.dll").write_bytes(b"\x4d\x5a")
        (src / "System.Text.Json.dll").write_bytes(b"\x4d\x5a")
        (src / "dnlib.dll").write_bytes(b"\x4d\x5a")
        return src

    def test_copies_all_dlls(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            home = self._fake_home(tmp_path)
            src = self._fake_source_dir(tmp_path)
            dest = install_plugin_dll(home, source_dir=src)
            dest_dir = home / "bin" / "Extensions" / "dnspy-mcp"
            installed = sorted(p.name for p in dest_dir.glob("*.dll"))
            self.assertEqual(
                installed,
                ["System.Text.Json.dll", "dnlib.dll", "dnspy_mcp.x.dll"],
            )
            self.assertEqual(dest, dest_dir / "dnspy_mcp.x.dll")

    def test_rejects_both_source_args(self) -> None:
        with self.assertRaises(ValueError):
            install_plugin_dll(
                Path("Z:/nope"),
                source_dll=Path("a.dll"),
                source_dir=Path("b/"),
            )

    def test_rejects_empty_dir(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            home = self._fake_home(tmp_path)
            empty = tmp_path / "empty"
            empty.mkdir()
            with self.assertRaises(FileNotFoundError):
                install_plugin_dll(home, source_dir=empty)


if __name__ == "__main__":
    unittest.main()
