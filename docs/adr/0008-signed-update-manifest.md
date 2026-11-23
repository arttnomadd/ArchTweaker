# ADR-0008: Signed update manifest (RSA-3072, key built into the exe)

**Date**: 2026-11-16
**Status**: accepted (completes the "next step" of ADR-0005 without a paid certificate)
**Deciders**: Archie

## Context

ADR-0005 made the self-update fail closed on https, an allowlisted host and a SHA-256. That hash comes from the same
unsigned manifest as the download link, so it proves only that the download arrived intact. Whoever can change
latest.json (a compromised GitHub account, a squatted repository name, the default manifest URL pointing at an
account the maintainer does not own) can also pick the exe, and ArchTweaker runs it as administrator. The tweak
overlay has the same trust root.

## Decision

- The manifest must carry `sig`: an RSA-3072, PKCS#1 v1.5, SHA-256 signature by the maintainer's private key.
- The exe holds only the public key (`Updater.PublicKeyXml`). The private key never goes into the repository,
  a release, the zip or CI.
- The signed text is fixed: `archtweaker-manifest-v1`, then `version, serial, url, sha256, notes, overlay,
  overlay_sha256`, one per line, a missing field as an empty line (`Updater.SignedText`).
- The parser is strict: keys are case-sensitive, must start an object member, a key that appears twice reads as
  missing, and a value with `\`, a quote or a control character does not match. What is signed is exactly what is used.
- `serial` (signed) must not be lower than the highest serial this PC has seen (`HKLM\Software\ArchTweaker\Update\LastSerial`).
  An old signed manifest can't be replayed to roll an update offer or an overlay back.
- `Check` sets `UpdateInfo.Verified` only after the signature and serial pass; `Download` refuses anything else.
  `OverlayInfo` refuses an unsigned or older manifest.
- `tools/sign_manifest.py` (no extra packages) writes and verifies a signed manifest. It checks the key size, that the
  key matches the exe's public key (`--pub-xml`), the host allowlist, and verifies its own signature before writing.

## Alternatives Considered

### Authenticode signing of the exe
- **Pros**: Windows shows the publisher; SmartScreen reputation.
- **Cons**: needs a paid certificate; the CI step exists but has no certificate.
- **Why not now**: still a good addition later. It does not replace this: the manifest and overlay need their own trust.

### Pin the GitHub account only
- **Why not**: an account takeover or a typo in the URL still gives code execution as administrator.

## Consequences

### Positive
- A changed manifest (link, hash, version, notes, overlay) is refused, whoever hosts it.

### Negative
- Every release needs `sign_manifest.py` run with the private key. An unsigned latest.json makes every install refuse it.
- Losing the private key means a new key and a manual download of a new exe (old exes trust only the old key).
- This happened once: the 3.38.2 key was lost, and 3.42.1 ships a new key. Users of 3.38.2–3.42.0 install 3.42.1 by hand. A unit test (PublishedManifest_VerifiesWithTheBuiltInKey) now fails when latest.json does not verify with the key in the exe or does not offer the current version.

### Risks
- A leaked private key is a full compromise of the update channel. Keep it offline, with a backup.
