# CLAUDE.md

Guidance for Claude Code when working in ably-pubsub-dotnet.

## What this is

The Ably Pub/Sub SDK for .NET. It is structured as an internal `Ably.PubSub.Core` engine, consumed transitively, plus the `Ably.PubSub.Device` and `Ably.PubSub.Server` door packages. The public code namespace is `Ably.PubSub`.

## Build and test — use the Cake build, not bare `dotnet`

CI runs everything through the Cake wrapper (`./build.sh` on Unix, `./build.cmd` on Windows). The build enforces StyleCop analyzers **as build errors** via the rulesets `src/IO.Ably.ruleset` (product code) and `src/IO.Ably.Tests.ruleset` (tests), which set specific rules — including `SA1025` — to `Action="Error"`; the product projects additionally set `TreatWarningsAsErrors`. A plain `dotnet build`, an IDE build, or `dotnet test` does **not** reliably surface these — in particular the Cake build compiles in the **Release** configuration, while the test project relaxes analysis under `Debug` (a Debug-only `NoWarn` override), so a Debug build or `dotnet test` can pass while CI fails. Passing bare `dotnet` is NOT sufficient.

- Build + lint (all NetStandard projects, **including the test projects**): `./build.sh --target=Build.NetStandard`
- Unit tests for a target framework: `./build.sh --target=Test.NetStandard.Unit.WithRetry --framework=net6.0`
- .NET Framework legs (Windows or Mono only): `./build.sh --target=Test.NetFramework.Unit.WithRetry`

`Build.NetStandard` compiles `Ably.PubSub.Core`, both door packages **and** `Ably.PubSub.Tests.DotNET` in Release with the rulesets active, so it catches analyzer errors in test files without running the test suite. It is the minimal pre-push lint gate.

## Before you push — reproduce the CI build

The `check (net6.0..net9.0)` CI legs run `./build.sh --target=Test.NetStandard.Unit.WithRetry --framework=<tfm>`, which compiles the test projects with StyleCop-as-error **before** running any tests. Before pushing, run the Cake target CI runs for the area you touched (at minimum `./build.sh --target=Build.NetStandard`) — not just a bare build or `dotnet test`. If a change is clean under `dotnet build` but you have not run the Cake build, you have not validated what CI validates.

## Common lint gotchas (StyleCop, enforced as errors)

- **SA1025** — no multiple whitespace characters in a row. Do NOT use column-alignment spaces (e.g. lining up `[InlineData(...)]` values or trailing `//` comments). Use single spaces.
- EditorConfig: UTF-8, LF line endings, trim trailing whitespace, final newline.
