# NotBad Privacy Detector Agent

[Русская версия](README.ru.md)

> **Download the ready-to-run Windows build:** [GitHub Releases](https://github.com/Teutonick/NotBad-Privacy-Detector-Agent/releases)

> **Find what you forgot — before someone else does.**

## Five true-to-life reasons to run NotBad Privacy Detector

1. **“What are you handing over with the laptop?”** — before a sale, expose the documents, photos, metadata and secrets that can outlive a normal cleanup.
2. **“The pre-exit audit.”** — before returning a work computer, see in one run which personal files, account traces and activity footprints are still there.
3. **“A stalker in your AppData.”** — turn hidden folders, caches and application history into a clear map of what Windows and your apps still remember about you.
4. **“Find the NSFW before it finds you.”** — surface forgotten intimate and sensitive images in old folders before the computer changes hands.
5. **“Red-team yourself.”** — look at your PC through an attacker’s eyes and discover which files reveal your identity, habits and access first.

NotBad Privacy Detector Agent is a portable, local-first privacy scanner for Windows. It turns forgotten files, personal data, secrets, application traces and revealing metadata into a clear map of what deserves your attention — without sending your digital life to the cloud.

![NotBad Privacy Detector Agent demo](docs/assets/notbad-privacy-demo.gif)

## Your PC remembers more than you do

Old documents. API keys in abandoned configs. Photos with GPS. Copies buried in archives. Application histories that still point to files you thought were gone.

NotBad Privacy Detector Agent brings those traces together, explains the risk and helps you decide what matters. It is an auditor and detector — you stay in control of every action.

## Highlights

- **Privacy Radar that gets sharper over time.** Objective scanner risk and personal attention stay independent, combine transparently for ranking, and every local deep check refreshes both without hiding confirmed threats.
- **A guided priority deep check.** Triage Router cheaply selects a diverse, budgeted scope of up to 10% and sends each object only to applicable local deep scanners, then opens a focused priority report.
- **Digital archaeology in one place.** Discover personal data, credentials, forgotten application traces, sensitive archives, media metadata and duplicates across the PC.
- **Risk you can understand.** Findings come with evidence and context, so a scary score is never the whole explanation.
- **Discover where it came from.** Investigate a selected file's likely origin and connections to local software without turning the audit into guesswork.
- **See what applications remember.** Explore Windows application history as meaningful apps and remembered objects instead of a wall of cryptic system containers.
- **Report misses without exposing yourself.** Preview a privacy-safe, anonymized incorrect-detection report before choosing whether to open GitHub.
- **Truly local and portable.** No account, telemetry, cloud inference, installer, background service or autostart entry.

### See it in action

<p align="center">
  <img src="docs/assets/readme-priority-wizard.png" alt="NotBad Privacy Detector Agent AI priority wizard highlighting the riskiest objects" width="960">
</p>
<p align="center"><em>✨ AI priority wizard: highlight the riskiest objects in two clicks, then investigate the most valuable part of the audit first.</em></p>

<p align="center">
  <img src="docs/assets/readme-audit-overview.png" alt="NotBad Privacy Detector Agent audit overview" width="960">
</p>
<p align="center"><em>Audit overview: choose the scan scope and see the results grouped by privacy-relevant category.</em></p>

<p align="center">
  <img src="docs/assets/readme-findings-pii-secrets.png" alt="NotBad Privacy Detector Agent findings for PII and secrets" width="960">
</p>
<p align="center"><em>Findings: review personal data and potential secrets with risk, evidence, filters and local AI priority.</em></p>

<p align="center">
  <img src="docs/assets/readme-object-details.png" alt="NotBad Privacy Detector Agent object details, provenance and similar copies" width="960">
</p>
<p align="center"><em>Object details: inspect the audit evidence, investigate a file's provenance and find similar copies without opening its contents.</em></p>

<p align="center">
  <img src="docs/assets/readme-media-people-search.png" alt="NotBad Privacy Detector Agent people search" width="960">
</p>
<p align="center"><em>People search: detect faces in found images using a model that runs locally on your computer.</em></p>

<p align="center">
  <img src="docs/assets/readme-media-nsfw-search.png" alt="NotBad Privacy Detector Agent local NSFW search" width="960">
</p>
<p align="center"><em>Local NSFW search: classify found images on your computer with pause, continue and cancel controls—nothing is uploaded.</em></p>

<p align="center">
  <img src="docs/assets/readme-media-document-scan.png" alt="NotBad Privacy Detector Agent document scan" width="960">
</p>
<p align="center"><em>Document scan: surface document photos and identity-document traces for deliberate review.</em></p>

<p align="center">
  <img src="docs/assets/readme-application-history.png" alt="NotBad Privacy Detector Agent application history" width="960">
</p>
<p align="center"><em>Application history: inspect file paths remembered by Windows applications, including missing and sensitive matches.</em></p>

## ✨ AI Priority — tuned by you

Your privacy priorities are personal. Rate findings with 👍 or 👎, and an optional model trained locally on your feedback learns what is more likely to deserve **your** attention.

**✨ AI Priority** brings those recommendations to the top across findings, media and remembered application objects. Your ratings and model stay on your PC, and the recommendation remains a separate personal signal — it never replaces the product's evidence-based risk scores.

[See how the personal recommendation model works](docs/PERSONAL_MODEL_USER_DESCRIPTION.md).

## What it can uncover

- personal identifiers, contacts and sensitive document traces;
- API tokens, private keys, connection strings and forgotten credentials;
- application profiles, sessions, caches, histories and orphaned data;
- GPS/EXIF metadata, camera details, document photos and people indicators;
- sensitive files hidden inside archives and similar copies spread across folders;
- Windows Recent and application-history traces that can resurface during everyday work or screen sharing.

## From broad map to confirmed findings

1. Run a quick initial assessment to build the local privacy map.
2. Let Triage Router select a diverse, budgeted priority scope and run only applicable deep checks.
3. Review confirmed evidence in the priority report or launch additional focused checks manually.
4. Return later to the automatically restored radar, where completed work remains available and ranks meaningful findings higher.

The app does not decide what you should delete. File actions are explicit and separately confirmed.

> [!WARNING]
> **AS IS — USE AT YOUR OWN RISK.** The software is provided without warranties. The developer and contributors are not liable for data loss, data corruption, system damage, financial loss or any other direct or indirect consequences arising from use or inability to use the software. You are responsible for your decisions, permissions and backups. Read the full [Disclaimer](DISCLAIMER.md) and [MIT License](LICENSE) before use.

## Private by design

- normal audits and personal recommendations run locally;
- file paths, findings, ratings and model data are not uploaded;
- SQLite stores audit metadata, not arbitrary file contents;
- junctions and reparse points are not followed by default;
- the app does not change Registry, Defender, Windows Search or system services;
- optional network actions, such as downloading the people-detection model or opening a browser link, require an explicit user action.

See the full [Privacy Policy](PRIVACY.md), [Security Policy](SECURITY.md) and [Disclaimer](DISCLAIMER.md).

## Download and run

Download the latest portable `NotBadPrivacyDetectorAgent.exe` from Releases, place it in a folder you control and run it. No installer is required. Standard mode is enough for user-accessible locations; restart with administrator rights only when you explicitly want to inspect protected system areas.

## Documentation

README is the product overview. Implementation details, component contracts and development notes live in the linked engineering documents:

[Project map](docs/PROJECT_MAP.md) · [Architecture](docs/ARCHITECTURE.md) · [Testing](docs/TESTING.md) · [Building from source](docs/BUILDING.md) · [Personal recommendation model](docs/PERSONAL_MODEL_USER_DESCRIPTION.md)

NotBad Privacy Detector Agent is distributed under the [MIT License](LICENSE). Third-party components and their licenses are listed in [Third-Party Notices](THIRD_PARTY_NOTICES.md).
