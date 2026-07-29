# 翼枢分流助手

本目录是翼枢分流助手的正式开发目录。

项目使用 Windows 后台服务持续维护翼枢窄分流，并通过托盘程序显示状态和接受启用、关闭操作。正式应用只管理 Windows 路由和 WireGuard `AllowedIPs`，不管理 DNS 或 hosts。

## 目录说明

- `doc`：正式方案和设计说明。
- `config`：精简配置样例。
- `src`：正式 C# 源代码，包括共享核心、Windows 服务和托盘程序。
- `tests`：不依赖外部测试框架的离线自检。
- `scripts`：发布、安装和卸载脚本。

## 开发验证

以下脚本统一使用 PowerShell 7 或更高版本运行。

```powershell
dotnet build .\YiShuHelper.slnx -c Debug
dotnet run --project .\tests\YiShuHelper.SelfTests\YiShuHelper.SelfTests.csproj -c Debug
```

## 发布

```powershell
.\scripts\发布.ps1
```

发布结果位于 `artifacts`。使用 `scripts\安装.ps1` 安装或升级正式应用。
