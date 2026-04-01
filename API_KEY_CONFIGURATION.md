# ZayFlow AI Configuration Guide

## Overview

ZayFlow AI now uses **Cloudflare Workers AI** as its remote AI provider.

Default routing in this repo:

- Chat: `@cf/meta/llama-3.3-70b-instruct-fp8-fast`
- Code: `@cf/qwen/qwen2.5-coder-32b-instruct`
- Vision: `@cf/google/gemma-3-12b-it`

The desktop app stores your preferences locally in `%LOCALAPPDATA%\ZayFlow\`.

## Recommended Setup

### 1. Create a Cloudflare Workers AI token

Open:

- `https://developers.cloudflare.com/workers-ai/get-started/rest-api/`

Create a token with:

- `Account -> Workers AI -> Read`
- `Account -> Workers AI -> Edit`

Copy both:

- the API token
- the Cloudflare account ID from the same Workers AI page

### 2. Add it in ZayFlow

In the app:

1. Open **Settings**
2. Go to **AI & Tokens**
3. Paste the token into **Cloudflare API Token**
4. Paste the account ID into **Cloudflare Account ID**
5. Click **Save Settings**

## Configuration Options

### Option 1: Environment variables

PowerShell:

```powershell
[System.Environment]::SetEnvironmentVariable('CLOUDFLARE_API_TOKEN', 'cfut_YOUR_TOKEN_HERE', 'User')
[System.Environment]::SetEnvironmentVariable('CLOUDFLARE_ACCOUNT_ID', 'YOUR_ACCOUNT_ID_HERE', 'User')
```

Command Prompt:

```cmd
setx CLOUDFLARE_API_TOKEN cfut_YOUR_TOKEN_HERE
setx CLOUDFLARE_ACCOUNT_ID YOUR_ACCOUNT_ID_HERE
```

App-specific fallback variables are also supported:

```powershell
[System.Environment]::SetEnvironmentVariable('ZAYFLOW_CLOUDFLARE_API_TOKEN', 'cfut_YOUR_TOKEN_HERE', 'User')
[System.Environment]::SetEnvironmentVariable('ZAYFLOW_CLOUDFLARE_ACCOUNT_ID', 'YOUR_ACCOUNT_ID_HERE', 'User')
```

### Option 2: Backend config file

Edit:

- `%LOCALAPPDATA%\ZayFlow\backend-config.json`

Example:

```json
{
  "CloudflareApiToken": "cfut_YOUR_TOKEN_HERE",
  "CloudflareAccountId": "YOUR_ACCOUNT_ID_HERE",
  "Model": "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
  "MaxTokensPerRequest": 8000,
  "Temperature": 0.2,
  "SpeedMode": false,
  "SpeedModeModel": "@cf/meta/llama-3.1-8b-instruct"
}
```

### Option 3: App preferences

If you enter the values in Settings, they are stored locally in:

- `%LOCALAPPDATA%\ZayFlow\preferences.json`

## Resolution Order

ZayFlow resolves Cloudflare settings in this order:

1. `CLOUDFLARE_API_TOKEN`
2. `ZAYFLOW_CLOUDFLARE_API_TOKEN`
3. `%LOCALAPPDATA%\ZayFlow\backend-config.json`
4. `%LOCALAPPDATA%\ZayFlow\preferences.json`

The account ID is resolved in this order:

1. `CLOUDFLARE_ACCOUNT_ID`
2. `ZAYFLOW_CLOUDFLARE_ACCOUNT_ID`
3. `%LOCALAPPDATA%\ZayFlow\backend-config.json`
4. `%LOCALAPPDATA%\ZayFlow\preferences.json`

## Data Locations

- Preferences: `%LOCALAPPDATA%\ZayFlow\preferences.json`
- Backend config: `%LOCALAPPDATA%\ZayFlow\backend-config.json`
- Crash log: `%LOCALAPPDATA%\ZayFlow\crash.log`

## Troubleshooting

### "Cloudflare token or account ID is missing"

- Make sure both values are filled in.
- Restart ZayFlow after setting new environment variables.

### "Invalid Cloudflare token"

- Recreate the token from the Workers AI page.
- Make sure the token has Workers AI `Read` and `Edit` permissions.
- Check for extra spaces at the start or end.

### "Cloudflare rejected this account or permission set"

- Confirm the token and account ID come from the same Cloudflare account.
- Confirm the token was created from the Workers AI REST API flow.

## Notes

- Existing old provider selections are normalized to `Cloudflare` on load.
- All files and settings remain local unless you explicitly send content to the AI provider.
