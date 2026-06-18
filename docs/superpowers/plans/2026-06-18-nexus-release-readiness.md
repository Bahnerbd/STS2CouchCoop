# Nexus Release Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make LocalCoop ready for a first manual-install Nexus Mods alpha release with public-facing repo docs, manifest metadata, package checks, and release verification.

**Architecture:** Create a curated `nexus-release` branch from `local-transport-broker`. Keep source and tests visible, but make the branch's root documentation player-facing and keep generated artifacts out of git. Extend the existing PowerShell release tests so the package fails fast if public docs or manifest metadata regress.

**Tech Stack:** PowerShell release scripts/tests, Markdown documentation, STS2 mod manifest JSON, .NET 9 solution tests.

---

## File Structure

- Modify: `README.md`
  - Public/player-facing overview, manual install, launch, limitations, troubleshooting, support, credits.
- Create: `LICENSE`
  - MIT license for LocalCoop source code, with an additional notice that STS2 assets/binaries are not included or licensed.
- Create: `CHANGELOG.md`
  - First public alpha release notes for `0.1.0`.
- Create: `CONTRIBUTING.md`
  - Pull request guidance and LocalCoop transport-bridge stance.
- Create: `SUPPORT.md`
  - GitHub-first support policy and bug report checklist.
- Create: `docs/release/nexus-page.md`
  - Nexus page text ready to paste into Nexus Mods.
- Modify: `release/LocalCoop.json`
  - Public author and alpha description.
- Modify: `Build-LocalCoopRelease.ps1`
  - Add public readiness checks after staging the package.
- Modify: `tests/LocalCoop.Script.Tests/Build-LocalCoopRelease.Tests.ps1`
  - Cover new public readiness checks.

## Task 1: Create And Enter Nexus Branch

**Files:**
- No file edits.

- [ ] **Step 1: Confirm the worktree is clean on the development branch**

Run:

```powershell
git status --short --branch
```

Expected:

```text
## local-transport-broker...origin/local-transport-broker
```

- [ ] **Step 2: Create or switch to `nexus-release`**

Run:

```powershell
git switch -c nexus-release
```

If the branch already exists locally, run:

```powershell
git switch nexus-release
```

Expected:

```text
Switched to a new branch 'nexus-release'
```

or:

```text
Switched to branch 'nexus-release'
```

- [ ] **Step 3: Confirm branch**

Run:

```powershell
git branch --show-current
```

Expected:

```text
nexus-release
```

## Task 2: Replace Developer README With Public README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Rewrite `README.md` as public alpha docs**

Replace the developer-oriented branch/prototype README with these sections:

```markdown
# LocalCoop for Slay the Spire 2

LocalCoop is an experimental same-machine local co-op launcher and transport bridge for Slay the Spire 2.

It starts multiple local STS2 clients, assigns input slots, runs a loopback broker, and bridges the game's native multiplayer traffic between those clients.

## Status

LocalCoop is an Experimental Alpha tested against Slay the Spire 2 v0.103.3 on Windows x64.

Expect rough edges. This release does not promise polished split-screen presentation, full gameplay stability, compatibility with every future STS2 update, or compatibility with arbitrary mod combinations.

## Requirements

- Slay the Spire 2 installed on Windows.
- A copy of the LocalCoop release zip.
- One controller per local client, or one mouse/keyboard controlling all clients.

## Install

1. Download the LocalCoop release zip.
2. Extract it into your Slay the Spire 2 install folder so the mod lands here:

```text
Slay the Spire 2\mods\LocalCoop
```

3. Confirm this file exists:

```text
Slay the Spire 2\mods\LocalCoop\LocalCoop.json
```

## Launch

Open PowerShell in `Slay the Spire 2\mods\LocalCoop` and run:

```powershell
.\Start-LocalCoopClients.ps1 -ClientCount 4 -ControllerDevices '0,1,2,3'
```

For two or three clients, use:

```powershell
.\Start-LocalCoopClients.ps1 -ClientCount 2 -ControllerDevices '0,1'
.\Start-LocalCoopClients.ps1 -ClientCount 3 -ControllerDevices '0,1,2'
```

Convenience launchers are also included:

```text
Start-LocalCoop2Players.bat
Start-LocalCoop3Players.bat
Start-LocalCoop4Players.bat
```

## Input Modes

Controller/mouse cross-play is not supported in this alpha.

Supported:

- one controller per client;
- one mouse/keyboard controlling all clients.

Untested and unsupported:

- mixing controller and mouse control across different clients;
- multiple simultaneous mice through MouseMux or similar tools.

MouseMux or similar tools may work in theory, but they are not tested or supported by this project.

## Logs And Troubleshooting

Packaged launches write generated configs and broker logs under:

```text
%APPDATA%\SlayTheSpire2\LocalCoop
```

If PowerShell blocks the launcher, open PowerShell from the LocalCoop folder and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Start-LocalCoopClients.ps1 -ClientCount 2 -ControllerDevices '0,1'
```

If controller assignment looks wrong, start with two clients and two controllers:

```powershell
.\Start-LocalCoopClients.ps1 -ClientCount 2 -ControllerDevices '0,1'
```

To uninstall, delete:

```text
Slay the Spire 2\mods\LocalCoop
%APPDATA%\SlayTheSpire2\LocalCoop
```

## Support

Pull requests and bug reports are welcome. GitHub Issues and Pull Requests are strongly preferred.

Nexus comments may be read, but GitHub is the support source of truth. This is an alpha project with no guarantee of continued feature work. Best-effort compatibility fixes may happen while the maintainer is still actively playing Slay the Spire 2.

Useful bug reports include:

- Slay the Spire 2 version;
- LocalCoop version;
- Windows version;
- player count;
- controller and mouse/keyboard setup;
- reproduction steps;
- logs from `%APPDATA%\SlayTheSpire2\LocalCoop`.

## Credits

LocalCoop is maintained by Bahne. Codex assisted with development.

LocalCoop does not include or license Slay the Spire 2 assets or binaries. You must own and install Slay the Spire 2 separately.
```

- [ ] **Step 2: Check README for removed developer-only phrases**

Run:

```powershell
rg -n "orphan branch|Current Slice|hold/paneling|Manual Four-Client Smoke|transport seam probe|synthetic-player" README.md
```

Expected: no matches and exit code `1`.

## Task 3: Add Public Project Docs

**Files:**
- Create: `LICENSE`
- Create: `CHANGELOG.md`
- Create: `CONTRIBUTING.md`
- Create: `SUPPORT.md`
- Create: `docs/release/nexus-page.md`

- [ ] **Step 1: Add `LICENSE`**

Create `LICENSE` with the MIT license text and this project notice:

```text
MIT License

Copyright (c) 2026 Bahne

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

This license applies only to LocalCoop source code and documentation. LocalCoop
does not include or license Slay the Spire 2 assets, binaries, trademarks, or
other content owned by Mega Crit or its partners. Users must own and install
Slay the Spire 2 separately.
```

- [ ] **Step 2: Add `CHANGELOG.md`**

Create `CHANGELOG.md`:

```markdown
# Changelog

## 0.1.0 - Experimental Alpha

Initial Nexus-ready alpha release for Slay the Spire 2 v0.103.3 on Windows x64.

### Added

- Manual-install LocalCoop package for `Slay the Spire 2\mods\LocalCoop`.
- Multi-client launcher for same-machine local sessions.
- Loopback broker executable packaged with the mod.
- Controller slot assignment through launcher options.
- Public install, troubleshooting, support, and contribution docs.

### Known Limitations

- Experimental alpha; full gameplay stability is not guaranteed.
- Controller/mouse cross-play is not supported.
- One controller per client is supported.
- One mouse/keyboard controlling all clients is supported.
- Multiple simultaneous mice through MouseMux or similar tools are untested and unsupported.
- Future STS2 updates may break compatibility.
```

- [ ] **Step 3: Add `CONTRIBUTING.md`**

Create `CONTRIBUTING.md`:

```markdown
# Contributing

Pull requests are welcome.

LocalCoop is a transport bridge around Slay the Spire 2. STS2 should own lobby logic, player identity, character choice, ready state, run start, gameplay state, and message semantics wherever possible.

Prefer narrow transport, lifecycle, factory, launcher, or adapter changes that preserve native STS2 behavior.

Avoid synthetic lobby truth, manual state replay, UI reconstruction, timing heuristics, or gameplay reconciliation unless game-file analysis, runtime logs, and focused tests prove that a transport-level bridge cannot solve the issue.

## Before Opening A Pull Request

- Run `dotnet test LocalCoopTransport.sln --no-restore -p:OutputPath=bin\Debug\net9.0-test\` when dependencies are already restored.
- Run `.\Build-LocalCoopRelease.ps1 -GameRoot '..' -Version 0.1.0 -OutputRoot artifacts\release` when changing packaging or public docs.
- Do not include Slay the Spire 2 binaries, PCK files, logs, generated client folders, local config toggles, or generated release zips.
- Include focused tests for protocol, broker, launcher, release-script, and packaging changes when practical.
```

- [ ] **Step 4: Add `SUPPORT.md`**

Create `SUPPORT.md`:

```markdown
# Support

LocalCoop is an experimental alpha.

Pull requests and bug reports are welcome. GitHub Issues and Pull Requests are strongly preferred. Nexus comments may be read, but GitHub is the support source of truth.

There is no guarantee of continued feature work. Best-effort compatibility fixes may happen while the maintainer is still actively playing Slay the Spire 2.

## Useful Bug Reports

Please include:

- Slay the Spire 2 version;
- LocalCoop version;
- Windows version;
- player count;
- controller and mouse/keyboard setup;
- reproduction steps;
- logs from `%APPDATA%\SlayTheSpire2\LocalCoop`.

## Known Unsupported Areas

- Controller/mouse cross-play.
- Multiple simultaneous mice through MouseMux or similar tools.
- Arbitrary mod combinations.
- Future STS2 versions that have not been tested with LocalCoop.
```

- [ ] **Step 5: Add `docs/release/nexus-page.md`**

Create `docs/release/nexus-page.md` with paste-ready Nexus text:

```markdown
# LocalCoop - Experimental Local Co-op Alpha

## Summary

Experimental same-machine local co-op launcher and transport bridge for Slay the Spire 2.

## Description

LocalCoop starts multiple local Slay the Spire 2 clients, assigns input slots, runs a loopback broker, and bridges the game's native multiplayer traffic between those clients.

This is an Experimental Alpha tested against Slay the Spire 2 v0.103.3 on Windows x64. Expect rough edges. This release does not promise polished split-screen presentation, full gameplay stability, compatibility with every future STS2 update, or compatibility with arbitrary mod combinations.

## Requirements

- Slay the Spire 2 for Windows.
- Manual mod installation.
- One controller per client, or one mouse/keyboard controlling all clients.

## Installation

Extract the zip into your Slay the Spire 2 install folder so it lands here:

```text
Slay the Spire 2\mods\LocalCoop
```

Confirm this file exists:

```text
Slay the Spire 2\mods\LocalCoop\LocalCoop.json
```

## Launch

Open PowerShell in `Slay the Spire 2\mods\LocalCoop` and run:

```powershell
.\Start-LocalCoopClients.ps1 -ClientCount 4 -ControllerDevices '0,1,2,3'
```

For fewer clients:

```powershell
.\Start-LocalCoopClients.ps1 -ClientCount 2 -ControllerDevices '0,1'
.\Start-LocalCoopClients.ps1 -ClientCount 3 -ControllerDevices '0,1,2'
```

Convenience batch files are included for 2, 3, and 4 players.

## Known Limitations

- Experimental alpha; full gameplay stability is not guaranteed.
- Controller/mouse cross-play is not supported.
- One controller per client is supported.
- One mouse/keyboard controlling all clients is supported.
- Multiple simultaneous mice through MouseMux or similar tools are untested and unsupported.
- Future STS2 updates may break compatibility.

## Troubleshooting

Packaged launches write generated configs and broker logs under:

```text
%APPDATA%\SlayTheSpire2\LocalCoop
```

If PowerShell blocks the launcher, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\Start-LocalCoopClients.ps1 -ClientCount 2 -ControllerDevices '0,1'
```

## Support

GitHub Issues and Pull Requests are strongly preferred. Nexus comments may be read, but GitHub is the support source of truth.

Useful bug reports include STS2 version, LocalCoop version, Windows version, player count, input setup, reproduction steps, and logs from `%APPDATA%\SlayTheSpire2\LocalCoop`.

## Credits And Permissions

LocalCoop is maintained by Bahne. Codex assisted with development.

LocalCoop source code is MIT licensed. LocalCoop does not include or license Slay the Spire 2 assets or binaries. Users must own and install Slay the Spire 2 separately.

## Changelog

Initial Nexus-ready alpha release for Slay the Spire 2 v0.103.3 on Windows x64.
```

## Task 4: Update Manifest And Release Public-Readiness Checks

**Files:**
- Modify: `release/LocalCoop.json`
- Modify: `Build-LocalCoopRelease.ps1`
- Modify: `tests/LocalCoop.Script.Tests/Build-LocalCoopRelease.Tests.ps1`

- [ ] **Step 1: Update manifest metadata**

Change `release/LocalCoop.json` to:

```json
{
  "id": "LocalCoop",
  "name": "LocalCoop",
  "author": "Bahne",
  "description": "Experimental alpha same-machine local co-op launcher and transport bridge for Slay the Spire 2.",
  "version": "0.1.0",
  "has_pck": false,
  "has_dll": true,
  "dependencies": [],
  "affects_gameplay": true
}
```

- [ ] **Step 2: Add public readiness assertion function**

In `Build-LocalCoopRelease.ps1`, add `Assert-LocalCoopReleasePublicReadiness` after `Assert-LocalCoopReleaseFilePolicy`:

```powershell
function Assert-LocalCoopReleasePublicReadiness {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageRoot
    )

    $manifestPath = Join-Path $PackageRoot 'LocalCoop.json'
    $readmePath = Join-Path $PackageRoot 'README.md'

    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Public release manifest not found: $manifestPath"
    }

    if (-not (Test-Path -LiteralPath $readmePath -PathType Leaf)) {
        throw "Public release README not found: $readmePath"
    }

    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.author -ne 'Bahne') {
        throw "Public release manifest author must be 'Bahne'."
    }

    $description = [string]$manifest.description
    if (-not $description.Contains('Experimental alpha', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Public release manifest description must include 'Experimental alpha'."
    }

    $readme = Get-Content -Raw -LiteralPath $readmePath
    $requiredReadmeText = @(
        'Experimental Alpha',
        'Slay the Spire 2 v0.103.3',
        'Controller/mouse cross-play is not supported',
        'GitHub Issues and Pull Requests are strongly preferred',
        'LocalCoop does not include or license Slay the Spire 2 assets or binaries'
    )

    foreach ($requiredText in $requiredReadmeText) {
        if (-not $readme.Contains($requiredText, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Public release README must include: $requiredText"
        }
    }

    $forbiddenReadmeText = @(
        'orphan branch',
        'Current Slice',
        'hold/paneling-ui-wip',
        'Manual Four-Client Smoke',
        'transport seam probe'
    )

    foreach ($forbiddenText in $forbiddenReadmeText) {
        if ($readme.Contains($forbiddenText, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Public release README must not include developer-only text: $forbiddenText"
        }
    }
}
```

- [ ] **Step 3: Call public readiness assertion from release build**

After the existing `Assert-LocalCoopReleaseFilePolicy` call in `Invoke-LocalCoopReleaseBuild`, add:

```powershell
Assert-LocalCoopReleasePublicReadiness -PackageRoot $packageRoot
```

- [ ] **Step 4: Update release script tests**

In `tests/LocalCoop.Script.Tests/Build-LocalCoopRelease.Tests.ps1`, update the sample manifest author to `Bahne`, add `README.md` to the staged package, call `Assert-LocalCoopReleasePublicReadiness`, and add negative checks for:

```powershell
Assert-Throws {
    $badManifest = Get-Content -Raw -LiteralPath (Join-Path $packageRoot 'LocalCoop.json') | ConvertFrom-Json
    $badManifest.author = 'bahne + Codex'
    $badManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $packageRoot 'LocalCoop.json')
    Assert-LocalCoopReleasePublicReadiness -PackageRoot $packageRoot
} "Public release manifest author must be 'Bahne'." 'Public readiness should reject non-public manifest author.'
```

and:

```powershell
Set-Content -LiteralPath (Join-Path $packageRoot 'README.md') -Value '# Current Slice'
Assert-Throws {
    Assert-LocalCoopReleasePublicReadiness -PackageRoot $packageRoot
} 'Public release README must include: Experimental Alpha' 'Public readiness should reject developer README content.'
```

Restore a valid manifest and README before later policy assertions.

## Task 5: Verify And Commit

**Files:**
- Verify all modified files.

- [ ] **Step 1: Run script release tests**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tests\LocalCoop.Script.Tests\Build-LocalCoopRelease.Tests.ps1
```

Expected:

```text
Build-LocalCoopRelease.Tests.ps1 passed.
```

- [ ] **Step 2: Run full test suite**

Run:

```powershell
dotnet test LocalCoopTransport.sln --no-restore -p:OutputPath=bin\Debug\net9.0-test\
```

Expected: all tests pass.

- [ ] **Step 3: Build release package**

Run:

```powershell
.\Build-LocalCoopRelease.ps1 -GameRoot '..' -Version 0.1.0 -OutputRoot artifacts\release
```

Expected: produces `artifacts\release\LocalCoop-v0.1.0-sts2-v0.103.3-win-x64.zip`.

- [ ] **Step 4: Inspect git diff**

Run:

```powershell
git diff --stat
git status --short --branch
```

Expected: only planned docs, manifest, release script, and release test files changed.

- [ ] **Step 5: Commit implementation**

Run:

```powershell
git add README.md LICENSE CHANGELOG.md CONTRIBUTING.md SUPPORT.md docs/release/nexus-page.md release/LocalCoop.json Build-LocalCoopRelease.ps1 tests/LocalCoop.Script.Tests/Build-LocalCoopRelease.Tests.ps1 docs/superpowers/plans/2026-06-18-nexus-release-readiness.md
git commit -m "Prepare Nexus release branch"
```

Expected: one commit on `nexus-release`.
