# UTS — Universal Test Suite tests for the .NET SDK

These tests are **derived**, not written. Every one of them is a translation of a portable test
spec in [`ably/specification`](https://github.com/ably/specification) under `uts/`, and the spec is
the source of truth for *what* is tested. When a spec changes, the test is re-derived from it
rather than edited to taste.

If you are about to add or change a test here, read
[`.claude/skills/uts-to-csharp/SKILL.md`](../../../.claude/skills/uts-to-csharp/SKILL.md) first.
It is the translation guide for this SDK — layout, naming, the pseudocode mapping, the harness API,
and the traps that cost the most time. `uts/docs/writing-derived-tests.md` upstream governs it.

## Layout

```
Uts/
  Helpers/              the harness — mocks, clock, client factories, sandbox and proxy plumbing
    Http/               MockHttpClient, per uts/rest/unit/helpers/mock_http.md
    WebSockets/         MockWebSocket, per uts/realtime/unit/helpers/mock_websocket.md
    Sandbox/            the integration tier's app_config and wall-clock polling
    Proxy/              the proxy tier's session lifecycle and event-log readers
  Rest/Unit/            derived from uts/rest/unit/**
  Rest/Integration/     derived from uts/rest/integration/**
  Realtime/Unit/        derived from uts/realtime/unit/**
  Realtime/Integration/ derived from uts/realtime/integration/**
  coverage.md           what is not covered, and why
  deviations.md         where the SDK does the wrong thing
```

A spec at `uts/<path>/<name>.md` becomes `Uts/<Path>/<Name>Tests.cs`, each segment PascalCased.
Every test carries a `// UTS: <test id>` comment naming the spec point it came from.

## The three tiers

**Unit** (`Rest/Unit`, `Realtime/Unit`) reaches no network. Every request is served by
`MockHttpClient` and every frame by `MockWebSocket`, both installed through seams the SDK already
exposes publicly — `ClientOptions.HttpClient` and `ClientOptions.TransportFactory`. No SDK code was
changed to make these tests possible.

The realtime half of the unit tier runs in a single xUnit collection, declared on
`UtsTestBase.RealtimeUnitCollection`. These tests share one process with real timers and there is no
timer seam, so running them in parallel is not free: serialising them took the failure count from
eleven to three. A new realtime unit class needs that `[Collection]` attribute — xUnit does not
inherit it from a base class.

**Integration** (`*/Integration`) runs against the real Ably sandbox. It needs no secrets: the app
is self-provisioned from the vendored `common/test-resources/test-app-setup.json`. There is no mock
and nothing in front of the client, so a spec point that can only be shown through a stubbed
response belongs in the unit tier instead.

**Proxy** (`*/Integration/Proxy`) runs against the sandbox *through*
[`ably/uts-proxy`](https://github.com/ably/uts-proxy), a programmable HTTP/WebSocket proxy, so that
faults can be injected against the real backend. The proxy is the second witness: the SDK's own
result answers half of each question and the proxy's event log the other half.

## Running them

Through Cake, not bare `dotnet` — StyleCop runs as a **build error** in Release, and a clean
`dotnet build` in Debug proves nothing.

```bash
./build.sh --target=Build.NetStandard                                # lint gate
./build.sh --target=Test.NetStandard.Unit --framework=net6.0         # unit tier
./build.sh --target=Test.NetStandard.Integration --framework=net6.0  # integration tier
./build.sh --target=Test.NetStandard.Proxy --framework=net6.0        # proxy tier
```

On Windows run `./build.cmd` with the same arguments. `build.sh` needs a POSIX shell: from
PowerShell or `cmd` it is not an executable, so Windows offers to open it with a program and then
closes the window it picked.

Use the **non-retry** targets. `Test.NetStandard.Unit.WithRetry` and its siblings wrap the run in
warn-only `catch` blocks with no rethrow and **exit 0 even when tests fail** — and CI uses the
retry variants. A green CI run is not evidence that these tests pass; a green non-retry run is.

For a fast inner loop on one file:

```bash
dotnet test src/Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj \
  -f net6.0 -c Release --filter "FullyQualifiedName~Uts.Rest.Unit.Time"
```

### The integration target runs three passes

`Test.NetStandard.Integration` starts the test runner three times, split on the `tier` trait: the
repo's own sandbox specs, then `tier=uts-rest`, then `tier=realtime`. That is not tidiness. All
three groups reach the same sandbox app, and any two of them in one process produce "timed out
waiting for Connected" in numbers — measured at 52 failures when the first two were combined, against
6 when they were not. A new integration test must inherit one of the two UTS bases so that it lands
in a pass; one carrying only `type=integration` falls into the repo's pass and takes the contention
with it. N4 in `deviations.md` has the measurements.

### The proxy tier needs a proxy

Set one of `UTS_PROXY_CONTROL_URL` (a control API already running) or `UTS_PROXY_PATH` (the
binary), or put `uts-proxy` on `PATH`. Without one, the proxy tests **skip** rather than fail, so
that a local integration run is not blocked by a missing binary.

`ably/uts-proxy` publishes linux and darwin binaries only. On Windows, build it:

```bash
git clone https://github.com/ably/uts-proxy.git && cd uts-proxy
go build -o uts-proxy.exe .
export UTS_PROXY_PATH=$PWD/uts-proxy.exe
```

### Deviation tests

A test that carries the spec-correct assertion against behaviour the SDK gets wrong is marked
`[DeviationFact]` and skipped by default. Each is individually reproducible:

```bash
RUN_DEVIATIONS=1 dotnet test ... --filter "FullyQualifiedName~RSA7b"
```

`deviations.md` is the catalogue, grouped by root cause. Running the suite with
`RUN_DEVIATIONS=1` is also how you check the record is still true: every gated test must still
fail, and nothing may pass under both behaviours.

## Which target frameworks these run on

`net6.0` and `net7.0` — the frameworks `Ably.PubSub.Tests.DotNET` targets. They do **not** run on
`net462`: the .NET Framework test head is a separate project that does not compile this tree.

Two pre-existing CI defects are worth knowing before you read a result here. The `net8.0` and
`net9.0` legs discover **zero** tests, because the test project does not target them. And the
`*.WithRetry` targets cannot fail. Neither is caused by the UTS work and neither is fixed by it.

## What is not covered

`coverage.md`, grouped by tier, with the reason for each omission. The short version: the
mutable-messages area — message mutation, annotations, publish results, batch publish, batch
presence and token revocation — needs client API this SDK does not have, and in C# a test calling a
method that does not exist is a compile error rather than a failing assertion, so those specs are
skipped rather than written. Separately, msgpack is compiled out of this build, so every
protocol-variant spec has exactly one runnable variant.
