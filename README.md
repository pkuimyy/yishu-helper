# 翼枢分流助手

本目录是翼枢分流助手的正式开发目录。

项目使用 Windows 后台服务持续维护翼枢窄分流，并通过托盘程序显示状态和接受启用、关闭操作。正式应用只管理 Windows 路由和 WireGuard `AllowedIPs`，不管理 DNS 或 hosts。

## 目录说明

- `doc`：正式方案和设计说明。
- `config`：精简配置样例。
- `legacy\powershell`：当前 PowerShell 原型，仅供迁移和行为对照。
- `legacy\service-host`：早期 Windows 服务宿主原型。
- `src`：正式 C# 源代码，包括共享核心、Windows 服务和托盘程序。
- `tests`：不依赖外部测试框架的离线自检。
- `scripts`：发布、安装和卸载脚本。

现有原型采用复制方式归档，原运行目录不会被删除或修改。正式实现以 `doc\翼枢分流应用设计方案.md` 为准。

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

发布结果位于 `artifacts`。开发阶段不要直接安装服务，以免与当前运行中的旧版 `YiShuSplit` 服务同时修改路由。
