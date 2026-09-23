<img src="docs/icon.png" width="72" align="right" alt="">

# Claude Usage Bar

Your Claude plan limits, live in the **Windows taskbar** or the **macOS menu bar**. See how much of your **5-hour session** and **weekly** limit you've used without opening Claude. Click for the full breakdown.

Use ChatGPT or Gemini instead? The app can show those limits too. Claude is the default. See [ChatGPT](#chatgpt) and [Gemini](#gemini).

**Jump to:** [Windows setup](#windows-setup) · [macOS setup](#macos) · [ChatGPT](#chatgpt) · [Gemini](#gemini) · [Privacy](PRIVACY.md)

![Taskbar sizes](docs/strip-sizes-dark.png)

<p>
  <img src="docs/popup-dark.png" width="300" alt="Details popup (dark)">
  <img src="docs/popup-light.png" width="300" alt="Details popup (light)">
</p>

## Features

- **Sits in the taskbar itself.** It isn't a floating window: it goes into a free gap on the taskbar and blends with its background.
- **Always finds space.** It measures the real taskbar layout and picks the largest of four sizes that fits: **Full → Compact → Mini → Micro**. As you open and close apps, it resizes and moves by itself.
- **Details popup.** Session, weekly, per-model (Sonnet/Opus) and extra-usage limits, with reset times. A small tick on each bar shows where your usage would be if spread evenly across the window, so you can tell if you're ahead or behind.
- **Colour coded:** green, then amber at 70%, then red at 90%.
- **Light and dark** themes that follow your system.
- **Light on resources.** It checks every 5 minutes, backs off when rate limited and keeps the last reading so it shows numbers instantly on startup.

## Windows setup

### 1. Install Claude Code and log in

The widget reads your usage with the login from [Claude Code](https://docs.claude.com/en/docs/claude-code/setup), so you need to sign in there once.

Install it (PowerShell):

```powershell
irm https://claude.ai/install.ps1 | iex
```

Then open a terminal and run:

```powershell
claude
```

Inside Claude Code, type `/login`. Choose **Claude account with subscription** (Pro / Max / Team) and finish signing in in your browser. After that you can close the terminal.

> Already running the widget? It notices the login within a few seconds, so there's no need to restart it. If you aren't signed in, clicking the widget shows a setup card with an **Open terminal** button that does step 1 for you.

### 2. Install the widget

**Option A: download.** Get `ClaudeUsageBar.exe` from [Releases](../../releases). Save it somewhere permanent, such as `C:\Users\<you>\Apps\`, because Start with Windows points at wherever the exe is. Then run it.

Because the app is new and isn't code-signed, your browser and Windows will probably warn you the first time. That's expected. See [Getting past download and security warnings](#getting-past-download-and-security-warnings) below.

**Option B: build from source** (needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)):

```powershell
git clone https://github.com/ANGLT12345/claudestatus.git
cd claudestatus
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
.\dist\ClaudeUsageBar.exe
```

### Getting past download and security warnings

The exe is new and isn't signed with a paid code-signing certificate, so Windows and browsers don't "know" it yet and warn you. This is normal for small open-source apps, and the warnings get rarer as more people download it. If you'd like to check the file first, compare its checksum with the `.sha256` file on the release page (see [Security & privacy](#security--privacy)), or build it yourself (Option B).

**Microsoft Edge: "ClaudeUsageBar.exe isn't commonly downloaded"**
1. In the downloads panel, hover over the file and click **⋯** (More actions).
2. Choose **Keep**.
3. If a second warning appears, click **Show more** → **Keep anyway**.

**Google Chrome: "Suspicious download" / "This file isn't commonly downloaded"**
1. In the downloads panel, click the warning, or open `chrome://downloads`.
2. Choose **Download suspicious file**, or **Keep**.

**Firefox** usually downloads it without asking. If it's blocked, open the downloads list (↓), right-click the file and choose **Allow download**.

**Windows: "Windows protected your PC" (Microsoft Defender SmartScreen)**
1. Click **More info**.
2. Click **Run anyway**.

You only need to do this once.

**Alternatively, unblock the file before running it:** right-click `ClaudeUsageBar.exe` → **Properties** → at the bottom of the **General** tab, tick **Unblock** → **OK**.

**"To run this application, you must install .NET"**

The app needs the free .NET 8 Desktop Runtime. Click **Yes**, download the **.NET Desktop Runtime 8 (x64)** installer, run it, then start the app again. Or install it straight from [Microsoft](https://dotnet.microsoft.com/download/dotnet/8.0).

**Smart App Control blocked it with no "Run anyway" option**

Windows 11's Smart App Control blocks unsigned apps and has no per-app exception. Your options are to build from source (Option B), or to turn Smart App Control off in **Windows Security → App & browser control → Smart App Control settings**. Be aware that on current Windows versions it can't be turned back on without resetting Windows.

**Your antivirus flags it**

Some antivirus tools are suspicious of new, unsigned apps, especially ones that read a credentials file. The app only sends your login token to Anthropic; see [Security & privacy](#security--privacy). You can check the checksum, read the source, or build it yourself. If you get a false positive, please [open an issue](../../issues) naming the antivirus product.

### 3. That's it

It starts with Windows automatically from the first launch. To turn that off, right-click the widget and untick **Start with Windows**; it stays off after that. If you move the exe, the startup entry follows it the next time you run it.

## macOS

A native menu bar app for macOS 13 (Ventura) or later, on both Apple Silicon and Intel Macs. It shows the same numbers, popover and colours as the Windows version.

### 1. Install Claude Code and log in

```bash
curl -fsSL https://claude.ai/install.sh | bash
claude
```

Inside Claude Code, type `/login`. Choose **Claude account with subscription** and finish signing in in your browser.

### 2. Install the app

1. Download `ClaudeUsageBar-macOS.zip` from [Releases](../../releases) and double-click it to unzip.
2. Drag **ClaudeUsageBar.app** into your **Applications** folder.
3. Open it. The first time, macOS will refuse, because the app isn't notarised by Apple (that needs a paid developer account). To allow it:
   - **macOS 15 (Sequoia) and later:** click **Done** on the warning. Open **System Settings → Privacy & Security**, scroll down to "ClaudeUsageBar was blocked…", click **Open Anyway** and confirm with your password.
   - **macOS 13–14:** right-click the app in Applications, choose **Open**, then click **Open** again.
   - **Or, in Terminal:**
     ```bash
     xattr -dr com.apple.quarantine /Applications/ClaudeUsageBar.app
     ```
4. **Allow Keychain access.** Claude Code keeps your login in the macOS Keychain, so macOS asks whether ClaudeUsageBar may use "Claude Code-credentials". Enter your Mac password and click **Always Allow**. If you click **Allow** instead, you'll be asked again next time.

The app opens at login automatically. You can turn that off with right-click → **Open at Login**.

### Using it on macOS

- **Click** the menu bar item for the details popover. Press ⌘R in the popover to refresh.
- **Right-click** (or Control-click) for the menu: Show Details, Refresh Now, Show Usage For (Claude / ChatGPT / Gemini), Size, Open at Login and Quit.
- **Size.** macOS doesn't let apps see how much menu bar space is free, so there's no Auto size. The default is **Compact**. If the item disappears behind the notch on a MacBook, switch to **Mini** or **Micro**.
- **Updating to a new version:** after replacing the app, macOS may ask for Keychain access again. Click **Always Allow** again.

### Build from source (macOS)

Needs the Xcode Command Line Tools (`xcode-select --install`):

```bash
git clone https://github.com/ANGLT12345/claudestatus.git
cd claudestatus
bash macos/build.sh
open macos/build/ClaudeUsageBar.app
```

## ChatGPT

The app shows one service at a time, Claude by default. To switch, right-click it and choose **Show usage for → ChatGPT** (or **Gemini**, or back to **Claude**). Your choice is remembered.

For ChatGPT it shows the **Codex usage limits** of your ChatGPT plan (Plus, Pro, Business…): a 5-hour window and a weekly window, the same numbers Codex's `/status` shows. ChatGPT doesn't publish message limits for the chat app itself, so those can't be shown.

It reads your ChatGPT login from [Codex CLI](https://developers.openai.com/codex/cli), so sign in there once:

```bash
npm install -g @openai/codex     # or on macOS: brew install --cask codex
codex login
```

Choose **Sign in with ChatGPT** and finish in your browser. If you aren't signed in, clicking the widget shows a setup card with an **Open terminal** button that runs `codex login` for you. Codex keeps the login in `~/.codex/auth.json` (or `$CODEX_HOME/auth.json`). The app only reads it.

If the widget later says **Sign in**, your Codex login has expired: run `codex` or `codex login` once to renew it.

## Gemini

Choose **Show usage for → Gemini** to see your Gemini daily request quotas: one row for the Pro models and one for the Flash models (the busiest model of each), with the time they reset. On the taskbar the rows are labelled **P** and **F**.

It reads your Google login from [Gemini CLI](https://github.com/google-gemini/gemini-cli), so sign in there once:

```bash
npm install -g @google/gemini-cli     # or on macOS: brew install gemini-cli
gemini
```

Choose **Login with Google** and finish in your browser. Logging in with a Gemini API key isn't supported, because API keys have no quota to read. If your account needs a Google Cloud project, set `GOOGLE_CLOUD_PROJECT` as you would for Gemini CLI.

**Good to know:** Gemini CLI's login only lasts about an hour, and only Gemini CLI can renew it. The app never renews or changes it. So if you haven't used Gemini CLI for a while, the widget says **Sign in**. Run `gemini` for a moment (the setup card's **Open terminal** button does this) and it updates by itself.

## Using it

| Action | What it does |
| --- | --- |
| **Left-click** | Opens the details popup (Esc or clicking elsewhere closes it) |
| **Right-click** | Menu: Show details, Refresh now, Show usage for (Claude / ChatGPT / Gemini), Size, Start with Windows, Quit |
| **F5** in the popup | Refreshes now |

**Sizes.** Auto picks one for you. To pin a size, right-click and choose **Size**.

| Size | Shows |
| --- | --- |
| Full | Segmented bars, % and time until reset |
| Compact | Thin bars and % |
| Mini | % only (coloured by status) |
| Micro | Two tiny vertical gauges (5h, 7d) |

On a centred taskbar it prefers the empty space left of your apps, next to Widgets, because that spot doesn't move as apps open and close. If there isn't room, it uses the gap next to the tray. If no size fits anywhere, it squeezes Micro into the biggest gap.

## Troubleshooting

- **"Set up" / "Sign in" on the taskbar.** No valid Claude Code login was found (or Codex login for ChatGPT, or Gemini CLI login for Gemini). Do step 1 above (or the [ChatGPT](#chatgpt) or [Gemini](#gemini) setup), or click the widget and use **Open terminal**.
- **"Rate limited".** The usage endpoints limit how often they can be called. The widget waits for the time the server asks for and then retries.
- **The widget disappeared after Explorer restarted.** It re-attaches automatically within a second or two. If it doesn't, restart the app.
- **Custom Claude config folder.** If you set `CLAUDE_CONFIG_DIR`, the widget uses it too.

## Security & privacy

- **No telemetry.** The app makes one kind of network request: `GET https://api.anthropic.com/api/oauth/usage`, the same endpoint behind Claude Code's `/usage` command. When showing ChatGPT it instead calls `GET https://chatgpt.com/backend-api/wham/usage`, the endpoint behind Codex's `/status`. For Gemini it calls `POST https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist` and `:retrieveUserQuota`, the endpoints Gemini CLI uses. All of these are undocumented and could change.
- **Your login token stays put.** It's read from Claude Code's `~/.claude/.credentials.json` (or Codex's `~/.codex/auth.json` for ChatGPT, or Gemini CLI's `~/.gemini/oauth_creds.json` for Gemini) and sent only to that service's address, over HTTPS. Redirects are refused, so the token can never be forwarded to another host. The app never copies the token, logs it, caches it or displays it, and never writes to your credentials file.
- **What's stored.** Only the last usage numbers (percentages and reset times, no token) and your settings. On Windows these are in `%LOCALAPPDATA%\ClaudeUsageBar\last.json` (`last-chatgpt.json` and `last-gemini.json` for the others) and `HKCU\Software\ClaudeUsageBar`, plus the optional autostart entry in `HKCU\...\Run`. On macOS they're in `~/Library/Application Support/ClaudeUsageBar/last.json` and the app's preferences. No admin rights are needed.
- **macOS Keychain.** On a Mac the token is read from the Keychain item "Claude Code-credentials", only after you allow it. It is kept in memory and never written anywhere.
- **What it runs.** Only when you click **Open terminal** in the setup card, it launches `claude` (or `codex login`, or `gemini`) from an absolute PATH entry or from `%USERPROFILE%\.local\bin` (or `%APPDATA%\npm` for Codex and Gemini CLI).
- **No third-party packages.** It uses only .NET and Windows APIs.
- **Verifiable releases.** Release builds are produced by [GitHub Actions](.github/workflows/release.yml) from the tagged source, with a `.sha256` checksum next to the exe. To check a download:
  ```powershell
  (Get-FileHash .\ClaudeUsageBar.exe -Algorithm SHA256).Hash
  ```
- Found a security problem? Please see [SECURITY.md](SECURITY.md).
- Full details: [Privacy statement](PRIVACY.md).

## How it works

- The taskbar strip is a layered child window placed inside `Shell_TrayWnd`. It finds free space by reading the real button positions through UI Automation, because on Windows 11 the older taskbar window bounds are out of date.
- It checks every 5 minutes and follows the server's `Retry-After` when rate limited. Once a token has been rejected, it stops calling the API until Claude Code writes a new one.

## Development

```powershell
dotnet build
dotnet run                               # run it
dotnet run -- --render docs              # re-render the README screenshots with sample data
dotnet run -- --icon app.ico             # regenerate the app icon
```

| File | Purpose |
| --- | --- |
| `StripPainter.cs` | Draws the four taskbar sizes |
| `Strip.cs` | Taskbar embedding, placement, mouse input |
| `TaskbarLayout.cs` | Finds free taskbar gaps via UI Automation |
| `Popup.cs` | Details popup and setup card |
| `Usage.cs` | API client, parsing, cache, formatting |
| `AppController.cs` | Polling, backoff, menus, startup |

### Releasing

Push a version tag and GitHub Actions builds the exe and attaches it (with its checksum) to a new Release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```
---

Not affiliated with or endorsed by Anthropic, OpenAI or Google. "Claude" is a trademark of Anthropic. "ChatGPT" and "Codex" are trademarks of OpenAI. "Gemini" is a trademark of Google.
