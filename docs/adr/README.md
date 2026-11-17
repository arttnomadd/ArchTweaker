# Architecture Decision Records

Key decisions behind ArchTweaker, in Michael Nygard's lightweight format. Backfilled from the project's
history (original dates noted); recorded Nov 2026.

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [0001](0001-mono-build-against-dotnet45-refs.md) | Build with Mono `mcs` against .NET Framework 4.5 reference assemblies + call audit | accepted | 2026-10-31 |
| [0002](0002-journal-based-exact-undo.md) | Journal-based exact-undo for every change | accepted | 2026-10-31 |
| [0003](0003-winapp2-allowlist-file-only-cleaner.md) | Deep clean: allowlisted cache roots, file-rules only, traversal rejected | accepted | 2026-11-03 |
| [0004](0004-conservative-registry-scanner.md) | Registry scanner limited to dead uninstall/App Paths refs (no CLSID, no defrag) | deprecated | 2026-11-03 |
| [0005](0005-self-update-trust-model.md) | Self-update trust: https + allowlist + fail-closed SHA-256; Authenticode deferred | accepted | 2026-11-04 |
| [0006](0006-offline-image-bake-registry-only.md) | Bake into ISO: registry values and service start types only, written to offline hives outside the journal | accepted | 2026-11-13 |
| [0007](0007-registry-snapshot-values-only.md) | Registry snapshots hold only the values an apply touches | accepted | 2026-11-16 |
| [0008](0008-signed-update-manifest.md) | Signed update manifest: RSA-3072 key in the exe, strict parser, replay serial | accepted | 2026-11-16 |
| [0009](0009-owner-drawn-depth.md) | Depth from derived tokens and one surface painter; buttons drawn by the app | accepted | 2026-11-16 |
