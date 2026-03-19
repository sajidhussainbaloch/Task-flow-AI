# ZayFlow AI — Windows PC Management & Automation Assistant

<p align="center">
  <strong>Your local AI-powered productivity assistant for Windows.</strong><br/>
  Organize files, clean your PC, automate tasks — all through natural language.
</p>

---

## Features

- **91 AI Intents** — file organization, PC cleanup, disk analysis, batch rename, secure delete, backup, and more
- **Natural Language Chat** — just type what you want (e.g. "organize my downloads" or "clean my temp files")
- **Preview Before Action** — see exactly what will happen before any destructive operation
- **Action Cards** — one-click shortcuts for common tasks (Organize Downloads, Clean PC, Find Large Files, Backup)
- **Auto Mode** — premium feature for hands-free automation
- **Dark & Light Themes** — modern WinUI-inspired design
- **Chat History** — persistent message history with session restore
- **Undo Support** — undo file operations when possible
- **QR Code Generation** — generate QR codes for text/URLs
- **System Tray** — runs minimized to tray, always accessible

## Tech Stack

| Layer | Technology |
|-------|-----------|
| **UI** | WPF (.NET 8, XAML, MVVM) |
| **AI** | Groq API (Llama 3.3 70B) |
| **Architecture** | Clean Architecture (5 projects) |
| **Themes** | Custom Dark/Light with Segoe Fluent Icons |
| **QR Codes** | QRCoder 1.7.0 |

## Solution Structure

```
ZayFlow.sln
├── ZayFlow.App/          → UI, ViewModels, Views, Themes, Services
├── ZayFlow.Core/         → Domain abstractions & services
├── ZayFlow.Actions/      → Action execution, previews, risk analysis, undo
├── ZayFlow.Planner/      → AI planning & orchestration
├── ZayFlow.Infrastructure/ → File system, external services
└── ZayFlow.Backend/      → Backend contracts, DTOs, persistence
```

## Prerequisites

- **Windows 10/11**
- **.NET 8 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Groq API Key** (free at [console.groq.com](https://console.groq.com))

## Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/YOUR_USERNAME/ZayFlow.git
cd ZayFlow
```

### 2. Build

```bash
dotnet build ZayFlow.App\ZayFlow.App.csproj
```

### 3. Run

```bash
.\ZayFlow.App\bin\Debug\net8.0-windows\ZayFlow.App.exe
```

> **Note:** Use the exe directly — `dotnet run` may show a console window.

---

## API Key Configuration

ZayFlow uses the **Groq API** (free tier available) for AI processing. You need to configure your own API key.

### Option 1: In-App Settings (Recommended)

1. Launch ZayFlow
2. Go to **Settings** (gear icon in the sidebar)
3. Paste your Groq API key in the **API Key** field
4. Click **Save**

The key is stored locally at `%LOCALAPPDATA%\ZayFlow\preferences.json`.

### Option 2: Environment Variable

```powershell
# PowerShell (persistent)
[System.Environment]::SetEnvironmentVariable('ZAYFLOW_GROQ_API_KEY', 'gsk_YOUR_API_KEY_HERE', 'User')
```

```cmd
# CMD (persistent)
setx ZAYFLOW_GROQ_API_KEY gsk_YOUR_API_KEY_HERE
```

### Option 3: Config File

Create or edit `%LOCALAPPDATA%\ZayFlow\config.json`:

```json
{
  "GroqApiKey": "gsk_YOUR_API_KEY_HERE",
  "Model": "llama-3.3-70b-versatile"
}
```

### Getting a Groq API Key

1. Go to [console.groq.com](https://console.groq.com)
2. Sign up / log in
3. Navigate to **API Keys**
4. Click **Create API Key**
5. Copy the key (starts with `gsk_`)

> **⚠️ Never commit your API key to version control.** The key is stored locally and is not included in this repository.

---

## Key Intents (Examples)

| Category | Example Commands |
|----------|-----------------|
| **File Organization** | "organize my downloads", "sort files by type" |
| **PC Cleanup** | "clean temp files", "free up disk space" |
| **Disk Analysis** | "find large files", "show disk usage" |
| **File Operations** | "move files", "rename files", "delete duplicates" |
| **Backup** | "backup my documents", "create a zip archive" |
| **System Info** | "show system info", "check battery" |
| **Utilities** | "generate QR code", "open calculator", "take screenshot" |

## Architecture

```
User Input → Groq AI (Llama 3.3 70B) → JSON Intent → IntentExecutionService → Result
                                                    ↓
                                            Preview (if destructive)
                                                    ↓
                                            User Confirms → Execute
```

- **MVVM Pattern** — Views ↔ ViewModels ↔ Services
- **Clean Architecture** — Core → Actions/Planner → Infrastructure → App
- **DI** — Microsoft.Extensions.DependencyInjection

## Security Notes

- **No API keys are stored in source code**
- Keys are stored in local user preferences (`%LOCALAPPDATA%\ZayFlow\`)
- Destructive operations require user confirmation with preview
- Risk analysis runs before file operations

## License

Private — All rights reserved.

## Author

Sajid
