# ADR-0005: Self-update trust — https + allowlist + fail-closed SHA-256; Authenticode deferred

**Date**: 2026-11-04
**Status**: accepted, extended by [ADR-0008](0008-signed-update-manifest.md) (signed manifest)
**Deciders**: Archie

## Context

Self-update downloads an executable and runs it elevated. Done naively, this is the highest-risk feature in
the app: a tampered manifest, a non-admin registry override, or a predictable temp path could all lead to
arbitrary code execution as administrator.

## Decision

The manifest and download must be **https** to an **allowlisted host**; the manifest URL override lives in
**HKLM** (admin-only), not HKCU; a **missing SHA-256 fails closed**; the version string is validated against
`^\d+(\.\d+){0,3}$` before use in any path; staging uses random names under ProgramData and keeps a `.bak`
that is restored if the swap fails. Full Authenticode signing of the exe is **deferred** (needs a code-signing
certificate the maintainer must provide).

## Alternatives Considered

### Alternative 1: No self-update (manual download only)
- **Pros**: zero update-attack surface.
- **Cons**: fixes never reach users who don't re-download.
- **Why not**: the project ships frequent releases; stale installs are the bigger practical risk.

### Alternative 2: Trust the manifest + hash as shipped (no host/scheme/version checks)
- **Pros**: simpler.
- **Cons**: hash from the same unsigned source proves nothing; version/URL become injection vectors.
- **Why not**: that's the exact class of bug the security review flagged.

## Consequences

### Positive
- The common attack paths (HKCU redirect, http downgrade, hash-less install, path-traversal version, temp TOCTOU) are closed.

### Negative
- Still trust-on-first-use on the manifest source until the exe is Authenticode-signed.

### Risks
- A compromise of the allowlisted host/repo would still serve a malicious (but hash-matching) exe — mitigation is Authenticode signing, tracked as the next step.
