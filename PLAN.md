# ZayFlow — Full Business & Technical Plan

> **Last Updated:** March 8, 2026
> **Status:** Phase 1 — App Development (In Progress)

---

## 📋 Project Overview

**ZayFlow** is a WPF desktop AI productivity assistant with 49+ actionable intents (file operations, system control, web search, downloads, reminders, etc.). The goal is to launch it as a commercial SaaS product with a freemium model.

---

## 🏗️ Architecture

```
┌─────────────────┐         ┌──────────────────────┐         ┌─────────────┐
│   ZayFlow App   │────────▶│   Your VPS Backend   │────────▶│  Groq API   │
│   (WPF/.NET 8)  │◀────────│   (ASP.NET Core)     │◀────────│  (LLM)      │
└─────────────────┘         └──────────────────────┘         └─────────────┘
        │                           │
        │                           ├── SQLite/PostgreSQL DB
        │                           ├── Firebase Auth (Google Sign-In)
        │                           └── Paddle Webhooks (Payments)
        │
        └── Custom URI: zayflow://auth?token=xxx
```

**Key principle:** The Groq API key stays on the VPS. The desktop app never sees it. Users authenticate via Google, and the VPS proxies all AI requests.

---

## 💰 Business Model

| Tier | Price | Limits | Features |
|------|-------|--------|----------|
| **Free** | $0 | 10 AI responses/month | All features, limited usage |
| **Premium** | $9/month | Unlimited | Unlimited AI responses |

**Revenue math:**
- 100 paid users = $900/month
- Costs: ~$13/month (VPS + domain) + ~$5 Groq API = ~$18/month
- Profit at 100 users: ~$880/month

---

## 🛠️ Tech Stack

| Component | Technology | Cost |
|-----------|-----------|------|
| Desktop App | WPF / .NET 8 / MVVM | Free |
| AI Provider | Groq API (Llama 3.3 70B) | ~$0.05/1M tokens |
| Backend API | ASP.NET Core on VPS | Free (on VPS) |
| VPS | Hostinger (4 vCPU / 16GB RAM / 200GB SSD) | ~$12/month |
| Domain | zayflow.app (or similar) | ~$10/year |
| Auth | Firebase Auth (Google Sign-In) | Free up to 10K users |
| Payments | Paddle (works in Pakistan) | 5% + $0.50/tx |
| Database | SQLite or PostgreSQL on VPS | Free |
| SSL | Let's Encrypt | Free |
| Website | Static HTML/CSS on VPS (Nginx) | Free |
| Installer | MSIX or Inno Setup | Free |

**Total monthly cost: ~$13/month**

---

## 🗺️ Development Roadmap

### Phase 1: Complete the App ✅ (IN PROGRESS)
- [x] 49 intents implemented (3 core batches + utility expansion)
- [x] Downloads tab with pause/resume/cancel
- [x] Progress bars for long operations
- [x] AI memory system
- [x] **Revert to Groq** (OpenRouter failed — 404 errors) ✅ DONE
- [ ] Add "Sign In" button (UI only, wire in Phase 4)
- [ ] Add "Upgrade to Premium" button (UI only, wire in Phase 6)
- [ ] Add license check logic: app calls `/api/license` on startup
- [ ] Add custom URI scheme `zayflow://` for auth redirect
- [ ] Build final `.exe` installer (MSIX or Inno Setup)
- [ ] Full testing

### Phase 2: Buy VPS + Domain
- [ ] Buy Hostinger VPS (~$12/month)
- [ ] Buy domain (e.g., zayflow.app)
- [ ] Point DNS to VPS IP
- [ ] Install Ubuntu + Nginx + .NET 8 runtime
- [ ] Set up SSL (Let's Encrypt)

### Phase 3: Backend API
- [ ] Create ASP.NET Core Web API project
- [ ] Build endpoints:
  - `POST /api/auth/google` — Verify Google token, create/return user
  - `GET /api/license` — Check user plan (free/premium)
  - `POST /api/chat` — Proxy AI requests to Groq
  - `POST /api/webhook/paddle` — Receive payment notifications
- [ ] Database tables:
  - `Users` (id, email, name, google_id, plan, created_at)
  - `Transactions` (id, user_id, paddle_id, amount, status, date)
  - `Usage` (id, user_id, month, response_count)
- [ ] Deploy to VPS
- [ ] Test all endpoints

### Phase 4: Firebase Auth (Google Sign-In)
- [ ] Create Firebase project
- [ ] Enable Google Sign-In provider
- [ ] Create sign-in page at `zayflow.app/auth`
- [ ] "Sign in with Google" button on page
- [ ] After sign-in → redirect to `zayflow://auth?token=xxx`
- [ ] App receives token → verifies with VPS → stores session
- [ ] Test complete auth flow

### Phase 5: Landing Website
- [ ] Build static HTML/CSS site:
  - Home page: Features, screenshots, "Download Free" button
  - Pricing page: Free vs Premium comparison
  - Download page: Direct `.exe` download link
- [ ] Host on VPS with Nginx
- [ ] Live at `zayflow.app`

### Phase 6: Paddle Payments
- [ ] Create Paddle account (approval: 1-3 days)
- [ ] Create product: "ZayFlow Premium — $9/month"
- [ ] Add Paddle checkout button on pricing page
- [ ] Set up webhook: Paddle → `zayflow.app/api/webhook/paddle`
- [ ] Backend receives webhook → updates user to premium
- [ ] App checks `/api/license` → gets premium → unlocks
- [ ] Test with Paddle sandbox

### Phase 7: Launch
- [ ] Final end-to-end testing
- [ ] Post on Product Hunt / Reddit / social media
- [ ] Monitor VPS logs and signups

---

## 🔄 User Flow

```
1. User visits zayflow.app
2. Clicks "Download Free"
3. Installs ZayFlow.exe
4. App launches → "Sign up free" popup
5. Opens browser → zayflow.app/auth → "Sign in with Google"
6. Google auth → redirect to zayflow://auth?token=xxx
7. App is now signed in (Free: 10 responses/month)
8. User uses app, loves it
9. Clicks "Upgrade to Premium" in app
10. Opens Paddle checkout → pays $9/month
11. Paddle webhook → VPS marks user as premium
12. App auto-updates to premium (unlimited responses)
```

---

## 📁 Current App State

### Working Features (49 intents):
- **Batch 1:** create_file, edit_file, search_web, run_command, system_info
- **Batch 2:** set_reminder, compress_files, clipboard_action, translate_text, download_file
- **Batch 3:** screenshot, text_to_speech, wifi_info, hash_file, schedule_shutdown, convert_units
- **Utility Expansion:** date_time, generate_password, quick_math, ping_host, process_action
- **Core:** open_application, open_file, open_url, AI memory (AddNote), chat, history
- **Downloads tab:** pause/resume/cancel with progress bars
- **Plus:** all original intents (create_folder, move_file, delete_file, rename_file, etc.)

### Known Issues:
- ~~OpenRouter migration FAILED (404 errors for all free models)~~ **RESOLVED — Reverted to Groq**
- ~~App currently has NO working AI~~ **RESOLVED — Groq is active**

### AI Provider Status:
- **Groq:** GroqProvider.cs — ACTIVE, working
  - API Key: `gsk_YOUR_API_KEY_HERE` (configure via Settings or environment variable)
  - Model: `llama-3.3-70b-versatile`
- **OpenRouter:** OpenRouterProvider.cs exists but unused (kept for future)

### Files Modified for OpenRouter (REVERTED TO GROQ ✅):
All 7 files reverted on March 8, 2026:
1. `App.xaml.cs` — registers GroqProvider
2. `AIService.cs` — takes GroqProvider
3. `AppPreferencesService.cs` — has GroqApiKey field
4. `SettingsViewModel.cs` — has Groq API Key UI binding
5. `SettingsView.xaml` — shows Groq API Key field
6. `AccountView.xaml` — shows "Groq (Llama 3.3 70B)"
7. `NavigationViewModel.cs` — takes GroqProvider

---

## 🔧 Build & Run

```powershell
# Kill running instance
Stop-Process -Name "ZayFlow.App" -Force

# Build
dotnet build "d:\Task flow AI\ZayFlow.sln" -c Debug

# Run
Start-Process "d:\Task flow AI\ZayFlow.App\bin\x64\Debug\net8.0-windows\ZayFlow.App.exe"
```

---

## 📝 Key Decisions Made

1. **Paddle over Shopify** — Paddle is a payment platform for SaaS subscriptions, handles tax/billing, works in Pakistan. Shopify is for e-commerce stores ($39/month unnecessary).
2. **One VPS for everything** — Website + API + database all on one Hostinger VPS (~$12/month).
3. **Firebase Auth** — Free Google Sign-In for up to 10K users.
4. **Groq for AI** — Fast, cheap (~$0.05/1M tokens), Llama 3.3 70B quality. Proxied through VPS.
5. **No local LLM on VPS** — Llama 32B needs 20-24GB + GPU. Use Groq API instead.
6. **Custom URI scheme** — `zayflow://` for browser-to-app auth redirect.
7. **Static website** — No WordPress/framework needed. Simple HTML/CSS served by Nginx.

---

## 💡 Future Ideas
- Microsoft Store distribution
- Mac version (Avalonia or Electron)
- Mobile companion app
- Team/Enterprise plans
- Plugin system for custom intents
- Voice input/output
