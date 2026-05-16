# dnspy-mcp

[dnSpyEx](https://github.com/dnSpyEx/dnSpy) 的多实例 MCP 服务器。Python supervisor 管理 dnSpy.exe 子进程；每个子进程内的 C# 插件通过本机 loopback HTTP 讲 Model Context Protocol。Supervisor 把所有 instance 聚合成一个对外的 MCP endpoint，AI agent 可用来检视、修改 .NET assembly。

## 现状

22 个工具，全部在真实 dnSpyEx v6.5.1 上端到端验证通过。

**Supervisor 管理工具 (5)**：`dnspy_open` · `dnspy_close` · `dnspy_list` · `dnspy_switch` · `dnspy_health`

**Plugin 工具 (17)**：

| 类别 | 工具 |
|---|---|
| Assembly | `list_assemblies` · `load_assembly` · `unload_assembly` · `get_assembly_info` · `save_assembly` |
| Types | `list_types` · `get_type_info` · `search_types` · `find_path_to_type` |
| Members | `list_methods` · `get_type_fields` · `get_type_property` |
| Code | `decompile_method` · `get_method_il` · `patch_method_il` · `revert_method_il` · `revert_all_pending_patches` |

English instructions: [README.md](README.md).

## 前置条件

- Windows（dnSpyEx 是 Windows GUI 程序）
- Python 3.11+
- .NET SDK 8.0+
- [dnSpyEx v6.5.1+](https://github.com/dnSpyEx/dnSpy/releases)，**.NET Framework 4.8** 构建 **或** **.NET 8** 构建（`dnSpy-net-win64.zip`）均可，C# 插件会自动选择 `net48` 或 `net8.0-windows` 来匹配。例如解压到 `C:\tools\dnSpy\`，确认 `<dnspy-home>\dnSpy.exe` 和 `<dnspy-home>\bin\dnSpy.Contracts.DnSpy.dll` 都存在。

## 构建

```powershell
git clone https://github.com/nyaoouo/dnspy-mcp
cd dnspy-mcp

# 1. Python supervisor
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -e .

# 2. C# 插件（DnspyHome 指向你的 dnSpyEx 安装目录）。
#    TargetFramework 会按 dnSpy 版本自动选择：
#      dnSpy-net-framework  =>  net48
#      dnSpy-net-win64      =>  net8.0-windows
#    构建结束会自动把 DLL stage 进 src/dnspy_mcp/plugin_assets/<tfm>/。
$DNSPY = "C:\tools\dnSpy"
dotnet build plugin\dnspy-mcp.sln -p:DnspyHome=$DNSPY -c Release
```

## 把插件装进 dnSpyEx

```powershell
dnspy-mcp install-plugin --dnspy-home C:\tools\dnSpy
```

这会把插件 DLL 加上 10 个依赖 DLL 复制到 `<dnspy-home>\bin\Extensions\dnspy-mcp\`。install 步骤会自动按 `--dnspy-home` 的版本挑 `plugin_assets/<tfm>/` 目录。重新构建后只需重跑这一步。

## 运行

HTTP 模式（默认）：

```powershell
dnspy-mcp --dnspy-home C:\tools\dnSpy --host 127.0.0.1 --port 8746
```

- MCP endpoint：`http://127.0.0.1:8746/mcp`
- 浏览器 instance 管理页：`http://127.0.0.1:8746/instances`

stdio 模式（适配只支持 stdio 的 MCP 客户端）：

```powershell
dnspy-mcp --stdio --dnspy-home C:\tools\dnSpy
```

stdio 模式下浏览器 UI 仍以 sidecar HTTP 形式启动；URL 在 stderr 上打印。

## 从 MCP 客户端连接

把配置自动写进受支持的客户端：

```powershell
dnspy-mcp --install claude --transport stdio --dnspy-home C:\tools\dnSpy
dnspy-mcp --install cursor,vscode --scope project --dnspy-home C:\tools\dnSpy
dnspy-mcp --uninstall claude
dnspy-mcp --list-clients
```

支持的客户端（名称大小写不敏感，连字符 / 去掉空格的写法都接受）：

| 名称 | 全局配置位置 |
|---|---|
| `claude` | `%APPDATA%\Claude\claude_desktop_config.json` |
| `claude-code` | `%USERPROFILE%\.claude.json`（也支持 `--scope project` → `.mcp.json`） |
| `cursor` | `%USERPROFILE%\.cursor\mcp.json` |
| `windsurf` | `%USERPROFILE%\.codeium\windsurf\mcp_config.json` |
| `vscode` | `%APPDATA%\Code\User\settings.json`（写在 `mcp.servers` 下） |
| `vscode-insiders` | `%APPDATA%\Code - Insiders\User\settings.json` |

参数说明：

- `--transport stdio`（默认）| `streamable-http` | `sse` | 任意 URL —— 决定写进配置里的 snippet 形态。
- `--scope global`（默认）| `project` —— global 写到上面列出的用户级配置；project 写到 CWD 下的工程级配置（`.mcp.json` / `.cursor/mcp.json` / `.vscode/mcp.json` / `.windsurf/mcp.json`）。Claude Desktop 仅支持 global。
- `--dnspy-home <路径>` —— 烧进 stdio 启动命令的 `args` 里，子进程无须再设 `DNSPY_HOME`。

对不在表里的客户端，可以打印三种 transport 的可粘贴片段：

```powershell
dnspy-mcp --config --dnspy-home C:\tools\dnSpy
```

典型的 stdio snippet 长这样：

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

`command` 会自动解析成当前 venv 里的 `python.exe`（找不到 venv 时回退到 `sys.executable`），所以即使 `dnspy-mcp` 不在 `PATH` 上，安装出来的配置依然可用。

## 验证

```powershell
# Python supervisor（不需要 dnSpy）
python -m unittest discover -s tests -v

# C# 插件（不需要 dnSpy；条件 Compile Remove 自动排除 dnSpy-only 文件）
dotnet test plugin\dnspy-mcp.sln -c Release

# 端到端实测（需要 supervisor 在跑）
.\.venv\Scripts\python.exe scripts\smoke_supervisor.py --url http://127.0.0.1:8746/mcp
```

## 仓库布局

```
src/dnspy_mcp/         Python supervisor — CLI、MCP 传输、instance 池、浏览器 UI
plugin/src/dnspy_mcp/  C# 插件 — HTTP server、MCP 协议、dnSpy 服务接线、17 工具
plugin/tools/SchemaDump/  构建期工具：反射出插件的 IMcpTool 接口生成 JSON snapshot
tests/ + plugin/tests/  Python + xUnit 测试套件
scripts/smoke_supervisor.py  实时端到端 driver
```
