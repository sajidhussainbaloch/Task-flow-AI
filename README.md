# ZayFlow AI

Windows productivity and automation assistant built with WPF, MVVM, and Cloudflare Workers AI.

## Features

- Natural-language desktop assistant for file tasks, utilities, and automation
- Code-aware assistant flow with planning, review, refactor, and artifact previews
- Persistent chat history and privacy confirmation flow
- OCR-first image handling before cloud upload when enabled
- System tray app with light and dark themes

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI | WPF (.NET 8, XAML, MVVM) |
| AI | Cloudflare Workers AI |
| Architecture | Clean Architecture (5 projects) |
| Themes | Custom dark/light styling |
| QR Codes | QRCoder |

## Solution Structure

```text
ZayFlow.sln
|- ZayFlow.App
|- ZayFlow.Core
|- ZayFlow.Actions
|- ZayFlow.Planner
|- ZayFlow.Infrastructure
`- ZayFlow.Backend
```

## Prerequisites

- Windows 10/11
- .NET 8 SDK
- Cloudflare Workers AI token
- Cloudflare account ID

Cloudflare setup docs:
- https://developers.cloudflare.com/workers-ai/get-started/rest-api/

## Build

```powershell
dotnet build ZayFlow.sln
```

## Run

```powershell
.\ZayFlow.App\bin\x64\Debug\net8.0-windows\ZayFlow.App.exe
```

## Cloudflare Defaults

The app is currently wired to these default models:

- Chat: `@cf/meta/llama-3.3-70b-instruct-fp8-fast`
- Code: `@cf/qwen/qwen2.5-coder-32b-instruct`
- Vision: `@cf/google/gemma-3-12b-it`

Vision requests automatically fall back to OCR/text context if Cloudflare rejects raw image input.

## In-App Setup

1. Launch ZayFlow.
2. Open **Settings**.
3. Enter **Cloudflare API Token**.
4. Enter **Cloudflare Account ID**.
5. Save settings.

Settings are stored locally in:
- `%LOCALAPPDATA%\ZayFlow\preferences.json`

## Environment Variables

```powershell
[System.Environment]::SetEnvironmentVariable('CLOUDFLARE_API_TOKEN', 'cfut_YOUR_TOKEN_HERE', 'User')
[System.Environment]::SetEnvironmentVariable('CLOUDFLARE_ACCOUNT_ID', 'YOUR_ACCOUNT_ID_HERE', 'User')
```

App-specific fallbacks:

```powershell
[System.Environment]::SetEnvironmentVariable('ZAYFLOW_CLOUDFLARE_API_TOKEN', 'cfut_YOUR_TOKEN_HERE', 'User')
[System.Environment]::SetEnvironmentVariable('ZAYFLOW_CLOUDFLARE_ACCOUNT_ID', 'YOUR_ACCOUNT_ID_HERE', 'User')
```

## Backend Config

Optional backend config file:
- `%LOCALAPPDATA%\ZayFlow\backend-config.json`

Example:

```json
{
  "CloudflareApiToken": "cfut_YOUR_TOKEN_HERE",
  "CloudflareAccountId": "YOUR_ACCOUNT_ID_HERE",
  "Model": "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
  "MaxTokensPerRequest": 8000,
  "Temperature": 0.2
}
```

## Request Flow

```text
User Input -> Cloudflare Workers AI -> JSON Intent -> IntentExecutionService -> Result
```

## Security Notes

- No API tokens are stored in source control.
- Tokens are stored in local user preferences or local backend config.
- Destructive operations require confirmation.
- Privacy mode can block sensitive uploads until approved.

## License

Private - All rights reserved.
