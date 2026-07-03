# Service Manager (sam)

![Service Manager app icon](assets/app-image.png)

A client-server service manager for running multiple background services (npm, PowerShell, Electron, etc.) with a CLI, a WPF Dashboard, a Terminal.Gui TUI, and an HTTP/WebSocket API.

## Architecture

```
sam (CLI) ─────────┐
                    ├──HTTP/WS──> Server (background) ──Process──> npm, PowerShell, Electron
Dashboard (WPF) ───┤
TUI Client ────────┘
```

- **sam** — CLI tool for managing the server and services from the command line
- **Server** — background process that manages services, watches config for changes, exposes API on `localhost:14040`
- **Dashboard** — WPF desktop app with dark theme for managing the server and services visually
- **Client** — Terminal.Gui TUI that connects to the server for a visual interface

## Requirements

- .NET 9 SDK
- Windows (Dashboard requires WPF)

## Quick Start

```bash
sam server              # start the server in background
sam list                # show services and their status
sam client              # open the TUI
```

Or launch the Dashboard — it can start/stop the server itself.

## Dashboard

The WPF Dashboard provides a visual interface for managing services:

- **Start/Stop Server** — launch or shut down the server directly from the dashboard
- **Service list** — shows all services with running/stopped status indicators
- **Service controls** — start, stop, restart individual services or all at once
- **Live output** — real-time service output with ANSI color support
- **Copy/Clear output** — copy output to clipboard or clear the output window
- **Edit Config** — edit a service's command, working directory, and auto-start setting
- **New/Delete Service** — add or remove services from the config
- **Server status indicator** — shows whether the server is online or offline
- **Persistent window state** — remembers window position, size, and maximized state across restarts
- **Dark theme** — dark title bar and VS Code-inspired color scheme

## CLI Reference (sam)

```
sam                     Show help
sam status              Show server status
sam list                List services and their status
sam start <name|all>    Start a service
sam stop <name|all>     Stop a service
sam restart <name>      Restart a service
sam output <name> [n]   Show last n output lines (default 30)
sam client              Start TUI client
sam server              Start server in background
sam server stop         Stop the server (kills all managed services)
sam server restart      Restart the server
sam server foreground   Run server in foreground (for debugging)
sam server install      Auto-start server at Windows login
sam server uninstall    Remove auto-start
sam help                Show help
```

## TUI Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `↑` / `↓` | Select service |
| `s` | Start selected service |
| `x` | Stop selected service |
| `r` | Restart selected service |
| `S` | Start all services |
| `X` | Stop all services |
| `q` | Quit client |

## Configuration

`services.yaml`:

```yaml
port: 14040

services:
  - name: my-frontend
    command: npm run dev
    workingDirectory: ../my-frontend
    autoStart: true

  - name: my-backend
    command: npm run dev
    workingDirectory: ../my-backend
    autoStart: false
```

| Field | Required | Description |
|-------|----------|-------------|
| `port` | no | API port (defaults to `14040`) |
| `name` | yes | Unique service identifier |
| `command` | yes | Shell command to run (executed via `cmd.exe /c`) |
| `workingDirectory` | no | Working directory (relative to config file location) |
| `autoStart` | no | Start automatically when server launches (defaults to `false`) |

The server watches `services.yaml` for changes and hot-reloads the config (including updated commands and working directories for existing services). Invalid YAML is ignored.

## API

The server exposes a REST + WebSocket API on `http://localhost:{port}`.

### REST Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/health` | Server status |
| `GET` | `/services` | List all services with status, PID, uptime |
| `POST` | `/services` | Create a new service |
| `DELETE` | `/services/{name}` | Delete a service |
| `POST` | `/services/{name}/start` | Start a service |
| `POST` | `/services/{name}/stop` | Stop a service |
| `POST` | `/services/{name}/restart` | Restart a service |
| `POST` | `/services/start-all` | Start all autoStart services |
| `POST` | `/services/stop-all` | Stop all services |
| `GET` | `/services/{name}/output?lines=100` | Get last N output lines |
| `POST` | `/services/{name}/output/clear` | Clear output buffer |
| `GET` | `/services/{name}/config` | Get service config |
| `PUT` | `/services/{name}/config` | Update service config |
| `GET` | `/config-path` | Get the config file path |

All responses are JSON. Example:

```bash
curl http://localhost:14040/services
curl -X POST http://localhost:14040/services/my-frontend/restart
curl http://localhost:14040/services/my-frontend/output?lines=20
```

### WebSocket

Connect to `ws://localhost:{port}/services/{name}/ws` for live output streaming. Each new output line is sent as a text message.

## Project Structure

```
ServiceManager/
├── ServiceManager.sln
├── services.yaml
├── CLI/             # sam CLI entry point
├── Shared/          # Config models and DTOs
├── Server/          # Background server with API
├── Dashboard/       # WPF desktop app
└── Client/          # Terminal.Gui TUI client
```
