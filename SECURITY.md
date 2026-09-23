# Security policy

Claude Usage Bar reads your Claude Code login token to fetch your plan usage. Security reports are taken seriously.

## Reporting a vulnerability

Please **don't open a public issue** for security problems. Instead, use GitHub's
[private vulnerability reporting](../../security/advisories/new) for this repository.

Please include:
- what you found and its impact,
- steps to reproduce,
- the app version (right-click the exe → Properties → Details).

You should get a reply within a week.

## Scope

In scope: anything that could expose the Claude Code token, send data anywhere other than `api.anthropic.com`,
run unexpected programs, or let another user or process tamper with the app.

Out of scope: problems in Claude Code, Windows or .NET themselves, and attacks that need someone who already
controls your Windows account (they can read the credentials file directly).
