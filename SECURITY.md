# Security policy

Only the latest published PrivacyAudit version is supported with security fixes.

Report vulnerabilities through the repository's private security advisory channel. If unavailable, open a minimal issue requesting a private contact method. Never publish sensitive paths, secrets, personal files, exploit details or database contents. Include the app version, Windows version and least-sensitive reproduction information. Maintainers should acknowledge a report within 7 days and coordinate disclosure after a fix.

PrivacyAudit is a local read-only auditor. It has no required internet access for audits, telemetry, background service or automatic startup. Network is used only for the explicit optional YuNet download or user-selected browser links. Elevated mode expands read access only.

The cleanup feature is limited to the exact app-owned `%LOCALAPPDATA%\NotBadPrivacyDetectorAgent` root, rejects any other root, and does not follow reparse points. It never deletes audited user files or the portable executable.
