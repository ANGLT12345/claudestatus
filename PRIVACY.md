# Privacy statement

_Last updated: 23 September 2026_

Claude Usage Bar is a free, open-source Windows app that shows your Claude plan usage in the taskbar. This statement explains what data it handles. In short: **the developer collects nothing.**

## What the developer collects

**Nothing.** Claude Usage Bar has no website, server, accounts, analytics, telemetry or tracking. No information about you or your use of the app is ever sent to the developer.

## What the app uses on your device

The app runs entirely on your computer and uses the following only to show you your own usage:

| Data | Where it comes from | What it's used for | Where it goes |
| --- | --- | --- | --- |
| Claude Code login token | `~/.claude/.credentials.json`, created by Claude Code | Asking Anthropic for your usage | Sent only to Anthropic (`api.anthropic.com`) over HTTPS. Never stored, copied, logged or displayed by the app. |
| Plan type (e.g. "Pro") | Same file | Showing your plan in the popup | Stays on your device |
| Usage percentages and reset times | Anthropic's API response | Showing your usage | Cached on your device in `%LOCALAPPDATA%\ClaudeUsageBar\last.json` |
| App settings (widget size, start with Windows) | Your choices | Remembering your preferences | Stored on your device in the Windows registry (`HKCU\Software\ClaudeUsageBar`) |

The app never writes to your Claude Code credentials file.

## Third parties

- **Anthropic.** To fetch your usage, the app sends your Claude Code login token directly from your computer to Anthropic, just as Claude Code does. Anthropic handles that request under its own [Privacy Policy](https://www.anthropic.com/legal/privacy).
- **GitHub.** The app's source code and downloads are hosted on GitHub, which may collect data about visitors under the [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). The developer only sees GitHub's aggregate stats, such as download counts.

The app shares no data with anyone else.

## Removing your data

To remove everything the app stores:

1. Right-click the widget and choose **Quit**.
2. Delete the folder `%LOCALAPPDATA%\ClaudeUsageBar`.
3. Delete the registry key `HKCU\Software\ClaudeUsageBar`, and the `ClaudeUsageBar` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Alternatively, untick **Start with Windows** before quitting.
4. Delete `ClaudeUsageBar.exe`.

## Children

The app is not directed at children and collects no data from anyone.

## Changes

If this statement changes, the updated version will be published at this address, with a new "Last updated" date.

## Contact

Questions? Open an issue at [github.com/ANGLT12345/claudestatus/issues](https://github.com/ANGLT12345/claudestatus/issues).
