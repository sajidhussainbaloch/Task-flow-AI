# ZayFlow AI Configuration Guide

## Overview

ZayFlow AI is a local AI productivity command center that enables users to control their computer, manage files, analyze content, and automate tasks using natural language. 

**Key architectural principle**: The AI model selection and API key management are controlled **in the backend only**, not in the UI. This ensures the desktop app has no direct control over which AI model runs.

---

## Groq API Key Configuration

ZayFlow AI uses the **Groq free API** with the Llama 3.3 model for fast, local-first inference.

### Getting Your Free API Key

1. Visit **https://console.groq.com**
2. Sign up for a free account
3. Copy your API key (format: `gsk_...`)
4. Store it securely using one of the methods below

---

## Three Ways to Configure the API Key

### **Method 1: Backend Configuration File (Recommended)**

1. Open `%LOCALAPPDATA%\ZayFlow\backend-config.json`
   - **Alternative path**: `C:\Users\<YourUsername>\AppData\Local\ZayFlow\backend-config.json`

2. Add your API key to the file:

```json
{
  "GroqApiKey": "gsk_YOUR_API_KEY_HERE",
  "Model": "llama-3.3-70b-versatile",
  "MaxTokensPerRequest": 8000,
  "Temperature": 0.7,
  "SpeedMode": false,
  "SpeedModeModel": "llama-3.1-8b-instant"
}
```

3. Save the file and restart ZayFlow

**Advantages:**
- Centralized configuration
- Survives UI updates
- Easy to manage multiple settings
- Can be shared via configuration management tools

---

### **Method 2: Environment Variable (Most Secure)**

Set an environment variable on your system:

**Windows (Command Prompt):**
```bash
setx ZAYFLOW_GROQ_API_KEY gsk_YOUR_API_KEY_HERE
```

**Windows (PowerShell - Admin):**
```powershell
[System.Environment]::SetEnvironmentVariable('ZAYFLOW_GROQ_API_KEY', 'gsk_YOUR_API_KEY_HERE', 'User')
```

**Windows (Environment Variables GUI):**
1. Right-click **This PC** or **My Computer** → **Properties**
2. Click **Advanced system settings**
3. Click **Environment Variables**
4. Under "User variables for [username]", click **New**
5. Variable name: `ZAYFLOW_GROQ_API_KEY`
6. Variable value: `gsk_YOUR_API_KEY_HERE`
7. Click **OK** and restart ZayFlow

**Advantages:**
- Most secure method
- Keeps key out of config files
- Standard practices in CI/CD environments
- Survives application reinstalls

---

### **Method 3: Settings Tab in Application**

1. Open ZayFlow AI
2. Navigate to **Settings** tab
3. Go to **AI Configuration** section
4. Paste your API key in the **Groq API Key** field
5. Click **Save Settings**

**Advantages:**
- No file editing required
- User-friendly interface
- Immediate availability

**Limitations:**
- Key is stored in user preferences JSON file
- Less secure than environment variables
- Fallback option if config file is missing

---

## Configuration Priority Order

ZayFlow resolves the API key in this order (first match wins):

1. **Environment Variable**: `ZAYFLOW_GROQ_API_KEY` 
2. **Backend Config File**: `%LOCALAPPDATA%\ZayFlow\backend-config.json`
3. **App Preferences**: Settings Tab (UI entry)

This means the environment variable takes precedence, followed by the config file.

---

## Backend Configuration Details

The backend configuration file (`backend-config.json`) supports these options:

```json
{
  "GroqApiKey": "gsk_YOUR_API_KEY_HERE",
  "Model": "llama-3.3-70b-versatile",
  "MaxTokensPerRequest": 8000,
  "Temperature": 0.7,
  "SpeedMode": false,
  "SpeedModeModel": "llama-3.1-8b-instant"
}
```

### Configuration Options:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `GroqApiKey` | string | (empty) | Your Groq API key. **Required**. |
| `Model` | string | `llama-3.3-70b-versatile` | The AI model to use. Free on Groq. Other options: `llama-3.1-8b-instant`, `mixtral-8x7b-32768` |
| `MaxTokensPerRequest` | int | `8000` | Maximum tokens per AI request (output limit). |
| `Temperature` | double | `0.7` | AI creativity level: 0.0 (deterministic) to 1.0 (creative). |
| `SpeedMode` | bool | `false` | Enable speed mode (uses faster, smaller model). |
| `SpeedModeModel` | string | `llama-3.1-8b-instant` | The model to use when Speed Mode is enabled. |

---

## Available Groq Models

Groq offers these free models:

| Model | Speed | Quality | Use Case |
|-------|-------|---------|----------|
| `llama-3.3-70b-versatile` | ⚡ Moderate | ⭐⭐⭐⭐ Excellent | Default – best all-around |
| `llama-3.1-8b-instant` | ⚡⚡ Very Fast | ⭐⭐⭐ Good | Speed-optimized, lighter tasks |
| `mixtral-8x7b-32768` | ⚡ Fast | ⭐⭐⭐⭐ Good | Fast, capable reasoning |

Change the `Model` field in `backend-config.json` to switch models.

---

## Verifying Configuration

### Check from Settings Tab:
1. Open ZayFlow AI
2. Navigate to **Settings** tab
3. Look at **AI Provider** field
4. Should show: "Groq (Free) ✓ Connected"

### Check Configuration File:
```powershell
cat $env:LOCALAPPDATA\ZayFlow\backend-config.json
```

### Check Environment Variable:
```powershell
$env:ZAYFLOW_GROQ_API_KEY
```

---

## Troubleshooting

### "Groq API key not configured"
- Verify the API key is present in one of the three locations
- Restart the application after configuration
- Check for typos in the API key (should start with `gsk_`)

### "API Error 401"
- Your API key is invalid or expired
- Get a new key from https://console.groq.com
- Verify you copied the entire key

### "Network error"
- Check your internet connection
- Verify Groq servers are accessible: https://console.groq.com
- Check firewall/proxy settings

### Configuration file not creating
- Ensure `C:\Users\<Username>\AppData\Local\ZayFlow\` folder exists
- Run ZayFlow once to auto-create the folder
- If the folder still doesn't exist, create it manually

---

## Data Storage Locations

- **Settings & Preferences**: `%LOCALAPPDATA%\ZayFlow\preferences.json`
- **Backend Configuration**: `%LOCALAPPDATA%\ZayFlow\backend-config.json`
- **Logs**: `%LOCALAPPDATA%\ZayFlow\crash.log`
- **Cache & Data**: `%LOCALAPPDATA%\ZayFlow\`

All data is stored **locally on your machine**. Nothing is uploaded unless you explicitly send file content for analysis.

---

## Security Notes

1. ✅ **Local Processing**: All file operations happen on your machine
2. ✅ **Privacy Mode**: Enabled by default. Requires confirmation before sending file content to AI
3. ✅ **No Auto-Upload**: Files are never sent without explicit user action
4. ✅ **API Key Protection**: 
   - Don't share your API key in version control
   - Use environment variables for shared machines
   - Regenerate keys periodically from Groq console
5. ✅ **Minimal Network**: Only text (and optional file content) is sent to Groq

---

## Architecture

```
┌─────────────────────────────────────┐
│   Desktop App (WPF Frontend)        │
│   - 3 tabs: ZayFlow AI, Settings    │
│   - Chat interface                  │
│   - File operations                 │
└──────────────┬──────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│   Local Backend Service              │
│   - Backend AI Configuration         │
│   - Intent execution                 │
│   - File management                  │
└──────────────┬───────────────────────┘
               │
               ▼
┌──────────────────────────────────────┐
│   Groq API (Free, Remote)            │
│   - Llama 3.3 Model                  │
│   - Ultra-fast inference             │
│   - No model choice in UI            │
└──────────────────────────────────────┘
```

The **backend controls AI model selection**, not the frontend. This ensures consistency and security.

---

## Getting Help

For issues with:
- **API Key**: Visit https://console.groq.com
- **ZayFlow**: Check %LOCALAPPDATA%\ZayFlow\crash.log for error details
- **Model Selection**: Edit backend-config.json and restart

---

## Version Info

- **ZayFlow Version**: 1.0.0
- **Framework**: .NET 8 / WPF
- **Default Model**: Llama 3.3 70B (Groq)
- **Configuration Priority**: Env Variable > Config File > UI Preferences
