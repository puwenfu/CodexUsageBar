# Privacy

CodexUsageBar reads quota information from the local Codex app-server protocol
so it can display remaining five-hour and weekly allowance and reset times. It
does not copy or persist authentication credentials, account identifiers, raw
Codex responses, or task content.

Diagnostic output can include timestamps, event codes, sanitized status
categories, retry timing, the detected Codex version, and taskbar placement
coordinates. It does not include credentials, account identifiers, raw Codex
responses, or task content. Do not include logs or unredacted screenshots in
public issues or pull requests.

The local preferences file stores only whether the five-hour meter is hidden.
The optional startup setting is a separate Windows Run registration. Neither
location stores Codex authentication information.

For a security-sensitive concern, follow the private reporting route in
[SECURITY.md](../SECURITY.md) rather than opening a public issue.
