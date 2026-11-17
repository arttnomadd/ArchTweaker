# ADR-0003: Deep clean — allowlisted cache roots, file-rules only, traversal rejected

**Date**: 2026-11-03 (hardened 2026-11-04)
**Status**: accepted
**Deciders**: Archie

## Context

The deep-clean feature uses the community Winapp2 ruleset (~3,700 apps) downloaded at runtime. Running an
unauthenticated, mutable ruleset as administrator is dangerous: a malicious or compromised rule could delete
arbitrary files, and a `..` segment or a broad root path could escape the intended cache folder.

## Decision

Only **FileKey (file)** rules are honored — never registry keys. Each resolved path is canonicalized and must
sit **under an allowlist** of cache roots (LocalAppData, AppData, LocalLow, Temp, ProgramData, Public, Program
Files); never the Windows tree, user document folders, or a drive root. Any rule containing `..` is rejected,
junctions/symlinks are not followed, masks match exactly, and `ExcludeKey` rules are honored.

## Alternatives Considered

### Alternative 1: Honor the full Winapp2 ruleset (files + registry) with a simple root blocklist
- **Pros**: cleans more; matches CCleaner behavior.
- **Cons**: exact-match blocklist is bypassable (`..`, subfolders of user data); registry deletion is riskier.
- **Why not**: not safe to run elevated against an untrusted ruleset.

### Alternative 2: Bundle a fixed, vetted ruleset
- **Pros**: no untrusted download.
- **Cons**: goes stale; large; licensing/maintenance burden.
- **Why not**: runtime download + strict guards gives freshness without trusting the content.

## Consequences

### Positive
- A malicious rule can delete nothing outside the cache allowlist, even as SYSTEM.
- No Recycle Bin dependency; the user reviews the measured list first.

### Negative
- Misses leftovers outside the allowlisted roots (deliberate).
- `master`-branch ruleset is still trust-on-first-use (size-capped + content-checked; pinning a commit is a future improvement).

### Risks
- A legitimate app storing cache in an unusual root is skipped — acceptable trade for safety.
