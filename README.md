# dnspy-mcp

Multi-instance MCP server for [dnSpyEx](https://github.com/dnSpyEx/dnSpy). A Python supervisor manages dnSpy.exe child processes; an in-process C# plugin in each child speaks Model Context Protocol over local-loopback HTTP. The supervisor aggregates everything into one MCP endpoint that AI agents can use to inspect and modify .NET assemblies.

## Status

22 tools, all verified end-to-end against dnSpyEx v6.5.1.

**Supervisor management (5):** `dnspy_open` · `dnspy_close` · `dnspy_list` · `dnspy_switch` · `dnspy_health`

**Plugin tools (17):**

| Category | Tools |
|---|---|
| Assembly | `list_assemblies` · `load_assembly` · `unload_assembly` · `get_assembly_info` · `save_assembly` |
| Types | `list_types` · `get_type_info` · `search_types` · `find_path_to_type` |
| Members | `list_methods` · `get_type_fields` · `get_type_property` |
| Code | `decompile_method` · `get_method_il` · `patch_method_il` · `revert_method_il` · `revert_all_pending_patches` |

中文说明见 [README.zh-CN.md](README.zh-CN.md).

## Prerequisites

- Windows (dnSpyEx is a Windows GUI app)
- Python 3.11+
- .NET SDK 8.0+
- [dnSpyEx v6.5.1+](https://github.com/dnSpyEx/dnSpy/releases) — either the **.NET Framework 4.8** build *or* the **.NET 8** build (`dnSpy-net-win64.zip`). The C# plugin auto-targets `net48` or `net8.0-windows` to match. Extract somewhere (e.g. `C:\tools\dnSpy\`) and confirm `<dnspy-home>\dnSpy.exe` and `<dnspy-home>\bin\dnSpy.Contracts.DnSpy.dll` both exist.

## Build

```powershell
git clone https://github.com/nyaoouo/dnspy-mcp
cd dnspy-mcp

# 1. Python supervisor
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -e .

# 2. C# plugin — set DnspyHome to your dnSpyEx install.
#    The TargetFramework is auto-selected from the dnSpy variant:
#      dnSpy-net-framework  =>  net48
#      dnSpy-net-win64      =>  net8.0-windows
#    After build, the DLLs are auto-staged into src/dnspy_mcp/plugin_assets/<tfm>/.
$DNSPY = "C:\tools\dnSpy"
dotnet build plugin\dnspy-mcp.sln -p:DnspyHome=$DNSPY -c Release
```

## Install plugin into dnSpyEx

```powershell
dnspy-mcp install-plugin --dnspy-home C:\tools\dnSpy
```

Copies the plugin DLL plus 10 dependency DLLs to `<dnspy-home>\bin\Extensions\dnspy-mcp\`. The installer picks the `plugin_assets/<tfm>/` folder that matches `--dnspy-home`. After rebuilds, just rerun this command.

## Run

HTTP mode (default):

```powershell
dnspy-mcp --dnspy-home C:\tools\dnSpy --host 127.0.0.1 --port 8746
```

- MCP endpoint: `http://127.0.0.1:8746/mcp`
- Browser instance manager: `http://127.0.0.1:8746/instances`

Stdio mode (for clients that prefer stdio MCP transport):

```powershell
dnspy-mcp --stdio --dnspy-home C:\tools\dnSpy
```

In stdio mode a sidecar HTTP server still hosts the browser UI; its URL is printed to stderr at startup.

## Connect from an MCP client

Auto-install the config into a supported client:

```powershell
dnspy-mcp --install claude --transport stdio --dnspy-home C:\tools\dnSpy
dnspy-mcp --install cursor,vscode --scope project --dnspy-home C:\tools\dnSpy
dnspy-mcp --uninstall claude
dnspy-mcp --list-clients
```

Supported clients (case-insensitive, accepts the same names with hyphens or no spaces):

| Name | Global config location |
|---|---|
| `claude` | `%APPDATA%\Claude\claude_desktop_config.json` |
| `claude-code` | `%USERPROFILE%\.claude.json` (also supports `--scope project` → `.mcp.json`) |
| `cursor` | `%USERPROFILE%\.cursor\mcp.json` |
| `windsurf` | `%USERPROFILE%\.codeium\windsurf\mcp_config.json` |
| `vscode` | `%APPDATA%\Code\User\settings.json` (under `mcp.servers`) |
| `vscode-insiders` | `%APPDATA%\Code - Insiders\User\settings.json` |

Flags:

- `--transport stdio` (default) | `streamable-http` | `sse` | any URL — picks the snippet shape written into the config.
- `--scope global` (default) | `project` — global writes to the user-level config above; `project` writes a per-project config in CWD (`.mcp.json` / `.cursor/mcp.json` / `.vscode/mcp.json` / `.windsurf/mcp.json`). Claude Desktop is global-only.
- `--dnspy-home <path>` — baked into the stdio invocation so the spawned subprocess doesn't need `DNSPY_HOME` set.

For unsupported clients, print copy-paste-ready snippets for all three transports:

```powershell
dnspy-mcp --config --dnspy-home C:\tools\dnSpy
```

A typical stdio snippet looks like:

```json
{
  "mcpServers": {
    "dnspy-mcp": {
      "command": "C:\\path\\to\\python.exe",
      "args": ["-m", "dnspy_mcp", "--stdio", "--dnspy-home", "C:\\tools\\dnSpy"]
    }
  }
}
```

The `command` is auto-resolved to the active venv's `python.exe` (falls back to `sys.executable`) so the install keeps working when `dnspy-mcp` isn't on `PATH`.

## Validate

```powershell
# Python supervisor — no dnSpy needed
python -m unittest discover -s tests -v

# C# plugin — no dnSpy needed (conditional Compile Remove excludes the MEF entry)
dotnet test plugin\dnspy-mcp.sln -c Release

# Live end-to-end (needs the supervisor running)
.\.venv\Scripts\python.exe scripts\smoke_supervisor.py --url http://127.0.0.1:8746/mcp
```

## Layout

```
src/dnspy_mcp/         Python supervisor — CLI, MCP transports, instance pool, browser UI
plugin/src/dnspy_mcp/  C# plugin — HTTP server, MCP protocol, dnSpy service wiring, 17 tools
plugin/tools/SchemaDump/  Build-time reflection of the plugin's IMcpTool surface to JSON
tests/ + plugin/tests/  Python and xUnit test suites
scripts/smoke_supervisor.py  Live end-to-end driver
```
