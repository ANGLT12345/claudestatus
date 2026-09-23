# Privacy statement

_Last updated: 23 September 2026 (added ChatGPT)_

Claude Usage Bar is a free, open-source app for Windows and macOS that shows your Claude or ChatGPT plan usage in the taskbar or menu bar. This statement explains what data it handles. In short: **the developer collects nothing.**

## What the developer collects

**Nothing.** Claude Usage Bar has no website, server, accounts, analytics, telemetry or tracking. No information about you or your use of the app is ever sent to the developer.

## What the app uses on your device

The app runs entirely on your computer and uses the following only to show you your own usage:

| Data | Where it comes from | What it's used for | Where it goes |
| --- | --- | --- | --- |
| Claude Code login token | Windows: `~/.claude/.credentials.json`. macOS: the Keychain item "Claude Code-credentials", read only after you allow it. Both are created by Claude Code. | Asking Anthropic for your usage | Sent only to Anthropic (`api.anthropic.com`) over HTTPS. Never stored, copied, logged or displayed by the app. |
| Codex login token and account ID (only when showing ChatGPT) | `~/.codex/auth.json` (or `$CODEX_HOME/auth.json`), created by Codex CLI | Asking OpenAI for your usage | Sent only to OpenAI (`chatgpt.com`) over HTTPS. Never stored, copied, logged or displayed by the app. |
| Plan type (e.g. "Pro") | Same place as the token, or OpenAI's usage response | Showing your plan in the popup | Stays on your device |
| Usage percentages and reset times | Anthropic's or OpenAI's API response | Showing your usage | Cached on your device. Windows: `%LOCALAPPDATA%\ClaudeUsageBar\last.json`. macOS: `~/Library/Application Support/ClaudeUsageBar/last.json`. ChatGPT's go in `last-chatgpt.json` in the same folder. |
| App settings (size, start at login, Claude or ChatGPT) | Your choices | Remembering your preferences | Stored on your device. Windows: the registry (`HKCU\Software\ClaudeUsageBar`). macOS: the app's preferences. |

The app never writes to your Claude Code credentials file or Keychain item, or to your Codex login file.

## Third parties

- **Anthropic.** To fetch your usage, the app sends your Claude Code login token directly from your computer to Anthropic, just as Claude Code does. Anthropic handles that request under its own [Privacy Policy](https://www.anthropic.com/legal/privacy).
- **OpenAI.** Only if you switch the app to ChatGPT: it sends your Codex login token directly from your computer to OpenAI, just as Codex does. OpenAI handles that request under its own [Privacy Policy](https://openai.com/policies/privacy-policy).
- **GitHub.** The app's source code and downloads are hosted on GitHub, which may collect data about visitors under the [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). The developer only sees GitHub's aggregate stats, such as download counts.

The app shares no data with anyone else.

## Removing your data

**Windows.** To remove everything the app stores:

1. Right-click the widget and choose **Quit**.
2. Delete the folder `%LOCALAPPDATA%\ClaudeUsageBar`.
3. Delete the registry key `HKCU\Software\ClaudeUsageBar`, and the `ClaudeUsageBar` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Alternatively, untick **Start with Windows** before quitting.
4. Delete `ClaudeUsageBar.exe`.

**macOS.** Right-click the menu bar item and choose **Quit**. Then delete `ClaudeUsageBar.app` from Applications and the folder `~/Library/Application Support/ClaudeUsageBar`. Optionally, also run `defaults delete io.github.anglt12345.claudeusagebar`. Deleting the app also removes its Open at Login entry.

## Children

The app is not directed at children and collects no data from anyone.

## Changes

If this statement changes, the updated version will be published at this address, with a new "Last updated" date.

## Contact

Questions? Open an issue at [github.com/ANGLT12345/claudestatus/issues](https://github.com/ANGLT12345/claudestatus/issues).
