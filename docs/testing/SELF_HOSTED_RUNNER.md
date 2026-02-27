# Self-Hosted Runner Setup Guide

## Requirements

The self-hosted runner is needed for:
- **Visual regression tests** — WPF `RenderTargetBitmap` requires active desktop session
- **E2E tests** — FlaUI, tray icon, hotkey registration, focus tracking, SendInput
- **Packaging smoke tests** — launch app, verify DB migration, clean shutdown

## Hardware & OS

- Windows 10/11 (x64)
- Interactive login session (NOT service mode)
- Label: `self-hosted-windows-interactive`

## Software Prerequisites

### 1. .NET 8 SDK
```powershell
winget install Microsoft.DotNet.SDK.8
```

### 2. sqlite3 CLI (for packaging smoke DB verification)
```powershell
winget install sqlite.sqlite
```
Or via Chocolatey:
```powershell
choco install sqlite
```

### 3. GitHub Actions Runner
Follow [GitHub docs](https://docs.github.com/en/actions/hosting-your-own-runners/adding-self-hosted-runners).

**Important:** Configure as interactive process, NOT Windows service:
```powershell
.\config.cmd --url https://github.com/OWNER/REPO --token TOKEN --labels self-hosted-windows-interactive
.\run.cmd   # NOT install as service
```

## Runner Configuration

1. Add label `self-hosted-windows-interactive` during setup
2. Ensure the runner starts with an interactive desktop session
3. Verify `.NET SDK` is on PATH: `dotnet --version`
4. Verify `sqlite3` is on PATH: `sqlite3 --version`

## Verification

Run locally on the runner machine:
```powershell
# Visual tests
dotnet test tests/PromptClipboard.App.Tests --filter "Category=Visual"

# E2E tests
dotnet build
dotnet test tests/PromptClipboard.E2E.Tests --filter "Category=E2E"
```

## Troubleshooting

| Issue | Solution |
|-------|---------|
| Visual tests produce blank images | Runner is in service mode; switch to interactive |
| E2E tests can't find tray icon | Desktop not active; ensure logged-in session |
| sqlite3 not found | Install via winget/choco, restart runner |
| Single-instance mutex blocks E2E | Kill `PromptClipboard.App.exe` before test |
