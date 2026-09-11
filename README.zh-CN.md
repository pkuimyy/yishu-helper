# 翼枢分流助手

[English](README.md)

本仓库是翼枢分流助手的正式开发目录。

项目使用 Windows 后台服务持续维护翼枢窄分流，并通过托盘程序显示状态和接受启用、关闭操作。正式应用只管理 Windows 路由和 WireGuard `AllowedIPs`，不管理 DNS 或 hosts。

## 目录说明

- `doc`：正式方案和设计说明。
- `config`：精简配置样例。
- `src`：正式 C# 源代码，包括共享核心、Windows 服务和托盘程序。
- `tests`：不依赖外部测试框架的离线自检。
- `scripts`：发布、安装和卸载脚本。

## 开发验证

以下命令需要使用 PowerShell 7 或更高版本运行：

```powershell
dotnet build .\YiShuHelper.slnx -c Debug
dotnet run --project .\tests\YiShuHelper.SelfTests\YiShuHelper.SelfTests.csproj -c Debug
```

## 发布

```powershell
.\scripts\发布.ps1
```

发布结果位于 `artifacts`。从仓库安装或升级正式应用时，使用 `scripts\install.ps1`。

发布脚本还会生成可分发文件 `artifacts\YiShuHelper-win-x64.zip` 及其 SHA-256 校验文件。解压 ZIP 后，PowerShell 用户可以运行其中的 `install.ps1`；脚本会自动请求管理员权限。

不使用 PowerShell 的用户可以右键单击 `install.cmd` 并选择“以管理员身份运行”。CMD 版本仅使用 Windows 自带工具。对应的 `uninstall.cmd` 支持原生 CMD 卸载；添加 `--remove-data` 参数可同时删除配置、状态和日志。

安装程序会启动后台服务，但不会再为托盘程序注册登录自启动。安装结束时会显示安装目录；如需启用托盘，请手动运行 `C:\Program Files\YiShuHelper\tray\YiShuHelper.Tray.exe`。

## GitHub CI

- 推送和拉取请求会在 Windows runner 上执行 Release 构建、离线自检并生成可安装 ZIP。
- 每次运行均可从 Actions 页面下载 `YiShuHelper-win-x64` 制品。
- 推送形如 `v0.1.3` 的标签时，会创建 GitHub Release，并附加 ZIP 和 SHA-256 校验文件。
