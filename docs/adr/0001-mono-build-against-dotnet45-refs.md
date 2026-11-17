# ADR-0001: Build with Mono `mcs` against .NET Framework 4.5 reference assemblies + call audit

**Date**: 2026-10-31
**Status**: accepted
**Deciders**: Archie

## Context

ArchTweaker ships as a single Windows `.exe` but is built on Linux. Compiling with Mono's own class
libraries produced an exe that crashed on real Windows with `Method not found` (e.g. `String.TrimStart()`),
because Mono exposes API overloads that .NET Framework 4.5 — the runtime built into Windows 10/11 — does not.

## Decision

Compile with Mono's `mcs` at `-langversion:5`, referencing the **.NET Framework 4.5 reference assemblies**
(`/usr/lib/mono/4.5-api`) with `-nostdlib -noconfig`, and run an `ikdasm` audit that fails the build if any
call resolves to a Mono/.NET-Core-only overload.

## Alternatives Considered

### Alternative 1: Build against Mono's own libraries
- **Pros**: simplest invocation.
- **Cons**: produces calls that don't exist on Windows .NET Framework.
- **Why not**: caused runtime `Method not found` crashes for users.

### Alternative 2: Build on Windows with MSBuild / csc
- **Pros**: native, no reference-assembly juggling.
- **Cons**: requires a Windows build host; not available here.
- **Why not**: the project is developed and built on Linux.

## Consequences

### Positive
- The exe uses only APIs present on stock Windows 10/11; no runtime install needed.
- The audit catches regressions at build time (it has caught `Split(char,…)`, `Stack<T>`, etc.).

### Negative
- Restricted to C# 5 language features (no interpolation, `?.`, pattern variables, etc.).
- Contributors must know the forbidden-overload list.

### Risks
- A new Mono-only overload could slip in — mitigated by the `ikdasm` grep audit in `build-win.sh`.
