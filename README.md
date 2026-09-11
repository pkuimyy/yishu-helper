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

发布脚本还会生成可分发文件 `artifacts\YiShuHelper-win-x64.zip` 及其 SHA-256 校验文件。
解压 ZIP 后，以 PowerShell 7 运行其中的 `安装.ps1` 即可安装或升级；脚本会自动请求管理员权限。
不使用 PowerShell 的用户可以右键单击 `安装.cmd` 并选择“以管理员身份运行”。CMD 版本仅使用 Windows 自带工具。
对应的 `卸载.cmd` 支持原生 CMD 卸载；添加 `--remove-data` 参数可同时删除配置、状态和日志。

## GitHub CI

- 推送和拉取请求会在 Windows runner 上执行 Release 构建、离线自检并生成可安装 ZIP。
- 每次运行均可从 Actions 页面下载 `YiShuHelper-win-x64` 制品。
- 推送形如 `v0.1.3` 的标签时，会创建 GitHub Release，并附加 ZIP 和 SHA-256 校验文件。
