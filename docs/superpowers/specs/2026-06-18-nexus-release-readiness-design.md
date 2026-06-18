# Nexus Release Readiness Design

## Purpose

Prepare LocalCoop for a first Nexus Mods release without overclaiming its maturity.
The release should look polished and trustworthy to public viewers while staying
honest that LocalCoop is an experimental same-machine launcher and transport
bridge for Slay the Spire 2.

CurseForge release preparation is out of scope for this pass.

## Current Evidence

- Current development branch: `local-transport-broker`.
- Existing release artifact pipeline: `Build-LocalCoopRelease.ps1`.
- Existing release artifact shape: a zip with one top-level `LocalCoop/` folder.
- Current STS2 compatibility target from `release_info.json`: `v0.103.3`.
- Current public docs issue: root `README.md` is developer-oriented and contains
  branch/prototype/harness language that is not appropriate as the first public
  impression for Nexus users.
- Current live install folder `mods/LocalCoop` is not uploadable because it may
  contain local toggles, logs, probes, symbols, and runtime files.

## Positioning

The first Nexus release is an **Experimental Alpha**.

The public promise is:

> LocalCoop is an experimental same-machine local co-op launcher and transport
> bridge for Slay the Spire 2. It launches multiple local STS2 clients, assigns
> input slots, starts a loopback broker, and bridges native multiplayer traffic.

The public release must not promise:

- polished split-screen presentation;
- full gameplay stability;
- compatibility with every future STS2 update;
- compatibility with arbitrary mod combinations;
- production-quality controller and mouse cross-play.

## Branch Strategy

Create a dedicated `nexus-release` branch from `local-transport-broker`.

The `nexus-release` branch remains source-visible and buildable. It should not
be a binary-only branch. Public viewers should be able to inspect the source,
build the package, file issues, and send pull requests.

The branch should be curated for public presentation:

- root `README.md` is public/player-facing;
- generated release zips are not committed;
- generated runtime folders, logs, probes, symbols, and local config toggles are
  not committed;
- internal branch-history or prototype notes are removed from the public first
  impression;
- development-only README material remains on the development branch, not the
  Nexus branch.

## Public Documentation

The public branch should include these top-level docs:

- `README.md`
- `LICENSE`
- `CHANGELOG.md`
- `CONTRIBUTING.md`
- `SUPPORT.md`

The public README should cover:

- what LocalCoop is;
- alpha status and tested STS2 version;
- manual install path;
- launch instructions;
- update and uninstall instructions;
- input-mode limitations;
- troubleshooting;
- support/reporting path;
- credits.

The README should be written for players first. It may link to deeper technical
docs, but it should not require users to understand the broker architecture
before installing and trying the mod.

## Nexus Page Draft

Add a release-page draft, for example:

```text
docs/release/nexus-page.md
```

The draft should be ready to paste into Nexus and should include:

- title;
- short summary;
- longer description;
- compatibility;
- requirements;
- manual install instructions;
- launch instructions;
- known limitations;
- troubleshooting;
- support/reporting instructions;
- permissions and credits;
- changelog for the uploaded file.

## Input Limitations

The public docs must explicitly say that controller/mouse cross-play is not
supported.

Supported and expected input modes for the first Nexus release:

- one controller per client;
- one mouse/keyboard controlling all clients.

Untested and unsupported:

- mixing controller and mouse control across different clients;
- multiple simultaneous mice through MouseMux or similar tools.

The docs may mention that MouseMux might work in theory, but it must be framed
as untested and unsupported.

## Package Shape

The uploadable Nexus artifact should continue to be built by
`Build-LocalCoopRelease.ps1`.

The zip should contain one top-level folder:

```text
LocalCoop/
```

Expected package contents:

- `LocalCoop.dll`
- `LocalCoop.Protocol.dll`
- `LocalCoop.json`
- player-facing `README.md`
- launcher scripts
- packaged loopback broker executable under `broker/`

The package must not contain:

- STS2 game binaries;
- STS2 PCK files;
- game-provided Harmony or Godot assemblies;
- generated logs;
- probe reports;
- local toggle files such as `enable-local*.txt`;
- generated client folders;
- debug symbols by default;
- `.deps.json` or `.runtimeconfig.json` files.

## Manifest

The public manifest should use Bahne as the primary public author.

Codex should be credited in README or credits documentation as development
assistance, not as a co-author in the manifest author field.

The description should match alpha positioning and should not overpromise
playability.

## License And Permissions

Use the MIT license for LocalCoop source code.

The license/docs should make clear that:

- LocalCoop does not include or license Slay the Spire 2 assets or binaries;
- users must own/install Slay the Spire 2 separately;
- redistribution of STS2-owned content is not permitted by this project.

## Support Policy

Support posture:

- Pull requests and bug reports are welcome.
- GitHub Issues and PRs are strongly preferred.
- Nexus comments may be read, but they are not the support source of truth.
- There is no guarantee of continued feature work.
- Best-effort compatibility fixes may happen while the maintainer is still
  actively playing STS2.

Bug reports should request:

- STS2 version;
- LocalCoop version;
- Windows version;
- player count;
- input setup;
- reproduction steps;
- relevant logs from `%APPDATA%\SlayTheSpire2\LocalCoop`.

## Contribution Guidance

`CONTRIBUTING.md` should invite PRs while preserving the project stance:

- LocalCoop bridges native STS2 behavior.
- STS2 owns lobby, player identity, character choice, ready state, run start,
  gameplay state, and message semantics wherever possible.
- Contributions should avoid synthetic lobby truth, manual state replay, UI
  reconstruction, timing heuristics, or gameplay reconciliation unless evidence
  proves a transport-level bridge cannot solve the issue.
- Protocol, broker, launcher, and packaging changes should include focused tests
  when practical.
- Contributions must not bundle STS2 binaries or assets.

## Release Verification

Before a Nexus upload, run:

```powershell
dotnet test LocalCoopTransport.sln --no-restore -p:OutputPath=bin\Debug\net9.0-test\
.\Build-LocalCoopRelease.ps1 -GameRoot '..' -Version <version> -OutputRoot artifacts\release
```

Also perform one fresh manual install smoke from the generated zip.

The release pipeline should be updated or tested so public-readiness regressions
are caught, especially:

- archive root layout;
- forbidden file policy;
- public manifest author/description;
- public README copied into the release package.

## Screenshots

Prepare screenshots only for the first Nexus release.

Minimum useful screenshots:

- the extracted `LocalCoop/` folder or launch script entry point;
- multiple STS2 clients running locally after launch;
- optional controller/player setup evidence if it is readable.

Do not spend this pass producing video or polished marketing assets.

## Out Of Scope

- CurseForge release polish.
- Vortex-first install support.
- New gameplay synchronization features.
- New split-screen UI.
- Rewriting the broker or launcher architecture.
- Committing generated release zips.
