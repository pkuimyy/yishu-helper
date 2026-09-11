# YiShu Split Helper

[简体中文](README.zh-CN.md)

This repository contains the production source code for YiShu Split Helper.

The application uses a Windows service to continuously maintain narrow split routing for YiShu, while a system tray application displays its status and provides enable/disable controls. It manages only Windows routes and WireGuard `AllowedIPs`; it does not manage DNS or the hosts file.

## Repository layout

- `doc`: Design and user documentation.
- `config`: Minimal configuration example.
- `src`: C# source code for the shared core, Windows service, and tray application.
- `tests`: Offline self-tests without an external test framework.
- `scripts`: Build, installation, and uninstallation scripts.

## Development validation

The following commands require PowerShell 7 or later:

```powershell
dotnet build .\YiShuHelper.slnx -c Debug
dotnet run --project .\tests\YiShuHelper.SelfTests\YiShuHelper.SelfTests.csproj -c Debug
```

## Publishing

```powershell
.\scripts\发布.ps1
```

Build output is written to `artifacts`. Use `scripts\install.ps1` to install or upgrade the application from the repository.

The publishing script also creates the distributable archive `artifacts\YiShuHelper-win-x64.zip` and its SHA-256 checksum file. After extracting the archive, PowerShell users can run `install.ps1`; the script requests administrator privileges automatically.

Users who do not use PowerShell can right-click `install.cmd` and select **Run as administrator**. The CMD version uses only built-in Windows tools. The corresponding `uninstall.cmd` performs uninstallation from CMD; pass `--remove-data` to also delete configuration, state, and logs.

The installer starts the background service but does not register the tray application for automatic startup. It prints the installation directory when it finishes. To enable the tray, manually run `C:\Program Files\YiShuHelper\tray\YiShuHelper.Tray.exe` after installation.

## GitHub CI

- Pushes and pull requests run the Release build and offline self-tests on a Windows runner, then produce an installable ZIP.
- Every workflow run provides a `YiShuHelper-win-x64` artifact on its Actions page.
- Pushing a tag such as `v0.1.3` creates a GitHub Release containing the ZIP and its SHA-256 checksum file.
