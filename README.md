# Gaspar System Health

Desktop tool for Windows built with C# and WPF for diagnostics, system integrity checks, network analysis, updates, security checks, and hardware monitoring.

## Features

- compact dashboard with CPU, memory, disk, uptime, and temperatures
- full diagnostic flow with step-by-step progress
- tools for network, firewall, updates, integrity, storage, and logs
- antivirus definition status and Microsoft Defender quick scan
- hardware sensor support through LibreHardwareMonitor

## Requirements

- Windows 10 or Windows 11
- administrator privileges
- compatible .NET Desktop Runtime

## Temperature Sensors

If LibreHardwareMonitor is not already present next to the executable, the app can try to download it automatically on first launch.

## Project Structure

- `App.xaml`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `Models/`
- `Services/`
- `app.manifest`
- `GasparSystemHealth.csproj`

## Build

```powershell
dotnet build .\GasparSystemHealth.csproj -c Release
dotnet publish .\GasparSystemHealth.csproj -c Release -o .\publish
```

## Release

The recommended way to distribute the app is through GitHub or GitLab releases with a packaged build attached as a zip file.
