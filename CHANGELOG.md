# Changelog

## 0.1.3 - STS2 v0.113.1

- Update the broker join flow for the native lobby responses and separate Standard and Blitz ascension limits.
- Bridge the new Blitz and Dread lobbies and the Daily and Custom character-selection screens. Use the game's native run-start path.
- Check Harmony targets against the installed game during packaging. The ZIP includes the compatibility report.

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
- Controller/mouse cross-play is not supported because STS2 switches into controller mode when any active player uses a controller, which deactivates or resets mouse control.
- One controller per client is supported.
- One mouse/keyboard controlling all clients is supported.
- Multiple simultaneous mice through MouseMux or similar tools are untested and unsupported.
- Future STS2 updates may break compatibility.
