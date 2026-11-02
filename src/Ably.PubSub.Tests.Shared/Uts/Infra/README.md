# UTS infra (ably-pubsub-dotnet)

The native test library that tests translated from the Universal Test Specs (UTS, `ably/specification` `uts/`) run on. It implements the helper specs (`uts/rest/unit/helpers/mock_http.md`, `uts/realtime/unit/helpers/mock_websocket.md`, `uts/realtime/unit/helpers/mock_vcdiff.md`), the corpus' `MockNetworkListener`, the pseudocode's waits and assertions, sandbox provisioning and the uts-proxy client. This file says what exists. How to author tests lives in `.claude/skills/uts-to-csharp/SKILL.md`.

- **Infra root:** `src/Ably.PubSub.Tests.Shared/Uts/Infra/`, namespace `Ably.PubSub.Tests.Uts.Infra[.Unit|.Integration|.Integration.Proxy]`.
- **Infra tests:** `src/Ably.PubSub.Tests.Shared/Uts/InfraTests/`, namespace `Ably.PubSub.Tests.Uts.InfraTests[.Unit|.Integration|.Integration.Proxy]`.
- **Generated tests** go in the mapping's target directories under `src/Ably.PubSub.Tests.Shared/Uts/`, **beside** `Infra/` and `InfraTests/` and never inside either. Don't name a folder `Encoding`, `Http`, `Realtime` or `Rest`: those names shadow `System.Text.Encoding`, `Ably.PubSub.Realtime` and `Ably.PubSub.Http`.
- Every `.cs` file under `Uts/` needs a `Compile Include="$(MSBuildThisFileDirectory)Uts\..."` line in `src/Ably.PubSub.Tests.Shared/Ably.PubSub.Tests.Shared.projitems`. Keep the lines alphabetical, with backslash paths.
- **Scope:** REST and Realtime only. LiveObjects is out of scope because the SDK has no LiveObjects API (D-28). No module fixture helpers exist, so there is nothing to index.

## Layout

Each file sits in the lowest directory that every tier using it can see. Dependencies point up the tree only: `Integration/Proxy/` may use `Integration/` and the root, and `Unit/` and `Integration/` never reference each other.

```
Uts/Infra/                      shared by every tier
  Unit/                         mocks, fake clock, templates, unit base
  Integration/                  SandboxApp, collection fixture, integration base
    Proxy/                      ProxyManager, ProxySession, rules, typed log, proxy base
Uts/InfraTests/                 common self-tests
  Unit/  Integration/  Integration/Proxy/
```

| Directory | File | Holds |
|---|---|---|
| `Infra/` | `UtsWait.cs` | `AwaitConnectionState`, `AwaitChannelState`, `PollUntil` (4 overloads), `PollUntilSuccess` (2), `WithTimeout` (2), `Drain`, `DrainOrParked` (internal, the fake-clock advance's drain), `DefaultTimeout`, `DefaultPollInterval` |
| | `UtsRecord.cs` | Record-and-verify: `ConnectionStates`, `ConnectionStateChanges`, `ChannelStates`, `ChannelStateChanges`, each returning a `CaptureList<T>` |
| | `CaptureList.cs` | `CaptureList<T>`: locked, thread-safe capture; `Add`, `Clear`, `Snapshot()`, indexer, `Count` |
| | `UtsAssert.cs` | `ContainsInOrder`, `Fail`, the internal `Failure` factory (caller-attributed `XunitException`) and `UtsHelperException` (keeps `Cause`) |
| | `CapturingLogSink.cs` | `CapturingLogSink : ILoggerSink` (`Logs`, `Contains(level, text)`) and `CapturedLog` (`Level`, `Message`) |
| | `DeviationFact.cs` | `[DeviationFact("id")]`, `[DeviationTheory("id")]` and `Deviations.Enabled` (`RUN_DEVIATIONS`) |
| | `UtsFact.cs` | `[UtsFact]`, `[UtsTheory]`: `[Fact]`/`[Theory]` with the per-test hang guard `UtsTestTimeouts.DefaultTestTimeoutMs` (30 s) |
| | `UtsTestBase.cs` | `UtsTestBase : IAsyncLifetime`: `Track`, `OnTeardown`, `OnVerify`, `CloseClient`, `CloseClientsOnTeardown`, `CloseTimeout`; `UtsTeardownException` |
| `Infra/Unit/` | `UtsUnitTestBase.cs` | Unit base: `InstallMock` (×4), `InstallDeltaDecoder`, `UninstallMocks`, `EnableFakeTimers`, `AdvanceTime`, `ProcessPendingEvents`, `Rest(options)`, `Realtime(options)`, `Clock`, `DefaultHttp`, `DefaultWebSocket`, `UnexpectedRequests`, `ValidKey`, `ValidOptions()` |
| | `MockHttpClient.cs` | `MockHttpClient : HttpMessageHandler` (mock_http.md), the legacy `Queue*` API and `Strict()` |
| | `PendingHttpConnection.cs`, `PendingHttpRequest.cs`, `MockHttpOutcome.cs` (internal) | HTTP connection and request phases, and their outcomes |
| | `RecordedUrl.cs` | Parsed URL: `Uri`, `Scheme`, `Host`, `Port`, `Tls`, `Path`, `QueryParams`, `AllQueryParams` |
| | `MockWebSocket.cs` | `MockWebSocket : ITransportFactory` (mock_websocket.md), including the alternative API |
| | `MockTransport.cs` | `MockTransport : ITransport`, one per attempt, with an ordered async delivery chain; tests never touch it |
| | `MockConnection.cs` | The server side of one connection: `SendToClient`, `SendToClientAndClose`, `SendToClientRaw`, `SimulateDisconnect`, `CloseFromServer`, `SendPingFrame` |
| | `PendingWebSocketConnection.cs` | One WS attempt and its `RespondWith*` outcomes |
| | `MockEvent.cs` | `MockEventType` (8 values), `MockEvent`, `ClientCloseEvent` |
| | `ProtocolMessages.cs` | Templates and builders (fresh `JObject`s of wire names) and action constants 0–23 |
| | `FakeClock.cs` | `FakeClock : ITimerScheduler` (hook H1): `Now`, `NowMillis`, `NowFunc`, `AdvanceAsync`, `ScheduleAfter`, `PendingCount` |
| | `MockVCDiff.cs` | `MockVCDiffEncoder`, `MockVCDiffDecoder`, `FailingMockVCDiffDecoder`, `IMockDeltaDecoder` (hook H2) |
| | `MockNetworkListener.cs` | `SimulateNetworkLost`, `SimulateNetworkAvailable`, `Clients` |
| `Infra/Integration/` | `SandboxApp.cs` | `SandboxApp.CreateAsync`/`DeleteAsync`, `SandboxKey`, `DefaultEndpoint`, `AppSetupBody()` |
| | `SandboxAppFixture.cs` | `SandboxAppFixture` (one app per collection) and `[CollectionDefinition("UTS Sandbox")] UtsSandboxCollection` |
| | `UtsIntegrationTestBase.cs` | Integration base: sandbox `Rest`/`Realtime`, `AwaitConnectionState`/`AwaitChannelState` (default 15 s), `RandomId`, `UniqueChannelName`, `ExtractKeyName`, `ExtractKeySecret`, `GenerateJwt`, `RequestTokenFromSandbox`, `SandboxTokenCallback`, `AssertSucceeded`, `App`, `ApiKey`, `SandboxEndpoint` |
| `Infra/Integration/Proxy/` | `ProxyManager.cs` | Pinned download, verification, shared cache, lock, start, health check and reaping (`ProxyBinarySource` enum) |
| | `ProxySession.cs` | `CreateAsync`, `AddRulesAsync`, `TriggerActionAsync`, `GetLogAsync`, `ExistsAsync`, `CloseAsync`, `ProxyHost`, `ProxyPort` |
| | `ProxyRules.cs` | Rule, match and action builders; `match.action` is always sent as a string |
| | `ProxyEvent.cs` | Typed `ProxyEvent` and the `ProxyLog` filter extensions |
| | `UtsProxyTestBase.cs` | Proxy base: `CreateProxySession`, `ProxyRealtime`, `ProxyRest`; `[CollectionDefinition("UTS Proxy")] UtsProxyCollection` |

**Shared and port-only code.** No new code is shared with the native tests. The infra reuses the native `ResourceHelper` (for the embedded `test-app-setup.json`) and `Ably.PubSub.Tests.Push.FakeMobileDevice`, and changes neither. Everything under `Uts/` is port-only.

## Infra tests

These are the smoke tests (end-to-end wiring of one tier) and the helper self-tests (contract tests). They carry no `// UTS:` tag and are named after the capability they prove. They stay permanently and run ungated in CI.

| Folder | File | Kind | Proves |
|---|---|---|---|
| `InfraTests/` (common) | `UtsAssertTests.cs` | self | `ContainsInOrder` subsequence semantics; caller-line attribution |
| | `UtsWaitTests.cs` | self | `PollUntil` returns the value and aborts on an error; `PollUntilSuccess` rethrows the last error; `WithTimeout` holds under a fake clock; the parked drain: idle, parked on a fresh delay, not parked on a delay pending while idle (F1), fails when busy with no fresh delay |
| | `CaptureListTests.cs` | self | Concurrent appends; snapshot is a copy |
| | `StateWaitTests.cs` | self | State waits latch a transient state, fail fast on FAILED and report the caller's line. Also holds one `[DeviationFact]` |
| | `TeardownTests.cs` | self | Teardown disposes clients when the body throws, stops timers, doesn't wait for CLOSED from INITIALIZED or FAILED, runs cleanups LIFO, and fails on mock errors |
| `InfraTests/Unit/` | `RealtimeUnitSmokeTests.cs` | smoke | CONNECTED via `RespondWithSuccess`; attempt host, port, TLS and query; non-ASCII ATTACH captured; ATTACHED; `SimulateDisconnect`; a retry only after `AdvanceTime`; the await style through to SUSPENDED; factory clients are off the static OS network hook (native ones stay on) |
| | `RestUnitSmokeTests.cs` | smoke | Request captured; `RespondWith` status, body and headers decoded; realtime-driven HTTP (`AuthUrl`, `AuthCallback`) |
| | `OptionalMocksSmokeTests.cs` | smoke | `MockNetworkListener` (RTN20a/b); `MockVCDiffDecoder` and `FailingMockVCDiffDecoder` through H2; the msgpack tripwire |
| | `MockWebSocketTests.cs` | self | Async close; CONNECTED delivered after the connection completes; raw frame; timeout and DNS outcomes; the mixing rules, including skipping parked attempts answered meanwhile or abandoned by the client; every `MockEventType` emitted in order; `ProcessPendingEvents` |
| | `MockHttpClientTests.cs` | self | Connection-level vs request-level failures; the captured `Host` header; handler and await styles; the legacy queue; HTTP timeout and delay on fake time; a request the test just answered reads busy until the SDK has the response; the strict default |
| | `FakeClockTests.cs` | self | One advance runs cascades to quiescence; nothing waits on the real clock; a slow queue with only an old delay pending is waited for, not treated as parked; an authCallback that never returns is parked (`ProcessPendingEvents` settles, `AdvanceTime` fires its timeout); an infinite `Schedule` never fires; a throwing callback doesn't stop the advance; a poll deadline still expires; the hook is per client |
| `InfraTests/Integration/` | `SandboxSmokeTests.cs` | smoke | One app per collection, with sane keys; app deleted on teardown; client CONNECTED; attach, subscribe and acked publish in order; history via `PollUntil` |
| | `SandboxAppSelfTests.cs` | self | `POST /apps` is never retried; status checked before parsing; setup body and Basic-auth DELETE; delete is best-effort (offline). Plus one real create and delete |
| `InfraTests/Integration/Proxy/` | `ProxySmokeTests.cs` | smoke | Pinned proxy installed, verified, started and healthy; pass-through token-auth session; `TriggerActionAsync(Disconnect())` followed by recovery; a frame rule with a string action fires once; teardown closes sessions and clients |
| | `ProxySelfTests.cs` | self | Builders stringify `match.action`; a `rules:`-only session defaults to the sandbox; the typed log parses; printed events redact the access token and `Authorization` header; pinned assets and cache layout (offline) |

## Traits, commands and CI

Each test class carries its own traits; xUnit doesn't reliably inherit them.

| Trait | Values | On |
|---|---|---|
| `uts` | `unit`, `integration`, `proxy` | Every UTS class, generated or infra. The common self-tests carry `uts=unit` |
| `uts-infra` | `common`, `unit`, `integration`, `proxy` | Infra tests only, by folder |
| `type` | `integration` | Every integration and proxy class, so native unit runs (`type!=integration`) never pick them up |

Collections: direct-sandbox classes join `[Collection(UtsSandboxCollection.Name)]` ("UTS Sandbox") and proxy classes join `[Collection(UtsProxyCollection.Name)]` ("UTS Proxy"). Offline self-test classes join neither.

The native unit and integration targets exclude every `uts` tier (`&uts!=unit&uts!=integration&uts!=proxy`, or `-notrait "uts=…"` for the xunit console), so each UTS suite runs in exactly one CI job.

The Cake targets are the gate, because they build in Release with StyleCop as errors. Each target cleans every `bin/` and `obj/` under `src/` first. `{tier}` is one of `unit`, `integration` or `proxy`. The targets have no retry, and they fail if they ran zero tests.

```sh
# Infra tests of one tier plus the common self-tests (filter: uts-infra=common|uts-infra={tier})
./build.sh --target=Test.NetStandard.UtsInfra --framework=net6.0 --uts-tier={tier}
./build.sh --target=Test.NetFramework.UtsInfra --uts-tier={tier}        # net462; Mono locally

# Every UTS test of one tier, generated and infra (filter: uts={tier})
./build.sh --target=Test.NetStandard.Uts --framework=net6.0 --uts-tier={tier}
./build.sh --target=Test.NetFramework.Uts --uts-tier={tier}
```

`Test.*.Uts` selects `uts={tier}`, so the common self-tests (which carry `uts=unit`) run only with `--uts-tier=unit`. `Test.*.UtsInfra` runs them for every tier. An invalid or missing `--uts-tier` fails in Setup, before the build starts.

Fast inner loop, after one `./build.sh --target=Build.NetStandard` (or `Build.NetFramework`). This loop is not the gate:

```sh
dotnet test src/Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj -c Release -f net6.0 --no-build --no-restore --filter "uts-infra=common|uts-infra=unit"
dotnet test src/Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj -c Release -f net6.0 --no-build --no-restore --filter "FullyQualifiedName~Ably.PubSub.Tests.Uts.InfraTests.Unit.FakeClockTests"
mono tools/xunit/xunit.console.exe src/Ably.PubSub.Tests.NETFramework/bin/Release/Ably.PubSub.Tests.NETFramework.dll -trait "uts-infra=common" -trait "uts-infra=unit"
mono tools/xunit/xunit.console.exe src/Ably.PubSub.Tests.NETFramework/bin/Release/Ably.PubSub.Tests.NETFramework.dll -class "Ably.PubSub.Tests.Uts.InfraTests.Unit.FakeClockTests"
```

A repeated `-trait` is an OR in xunit console 2.4.1. Run `dotnet test` with `-f`: two TFMs running in parallel would collide on the proxy control port.

CI runs these from `.github/workflows/run-tests-uts.yml`:

| Job | Runner | Command | Tier |
|---|---|---|---|
| `unit` | ubuntu-22.04, matrix net6.0 and net7.0 | `Test.NetStandard.Uts --uts-tier=unit` | unit, plus the common self-tests |
| `unit-netframework` | windows-2022 | `Test.NetFramework.Uts --uts-tier=unit` | unit on net462 |
| `integration` | ubuntu-22.04 | `Test.NetStandard.Uts --framework=net6.0 --uts-tier=integration` | direct sandbox |
| `proxy` | ubuntu-22.04 | `Test.NetStandard.Uts --framework=net6.0 --uts-tier=proxy` | proxy; downloads the pinned uts-proxy |

**Verified (locally, macOS arm64, 2026-10-16), with the Cake targets above:** `Build.NetStandard` and `Build.NetFramework` build with 0 warnings and 0 errors. `Test.NetStandard.UtsInfra` passes on **net6.0** for every tier (unit 84 passed / 1 skipped, integration 44 / 1, proxy 45 / 1) and on **net7.0** for unit (84 / 1). `Test.NetFramework.UtsInfra` passes on **net462** (Mono) for every tier (unit 84 / 1, integration 44 / 1, proxy 45 / 1); the proxy tier runs under Mono on macOS too. `Test.NetStandard.Uts --framework=net6.0 --uts-tier=unit` passes (84 / 1; today it selects the same set as the infra unit run). The one skip everywhere is the `[DeviationFact]`. The CI jobs in the table run the `Test.*.Uts` form of these targets; the Windows `unit-netframework` job (net462 on .NET Framework rather than Mono) and the proxy job's fresh-cache download are not covered by these local runs.

## Test heads and language constraints

All UTS code compiles into **both** test heads:

- `Ably.PubSub.Tests.DotNET` (net6.0 and net7.0)
- `Ably.PubSub.Tests.NETFramework` (net462; runs on Windows, or on Mono locally)

Both use **C# 8** (`LangVersion 8`). That rules out records, `init`, file-scoped namespaces and target-typed `new`. It also rules out APIs net462 lacks: `System.Text.Json`, `Random.Shared`, `IAsyncDisposable`, `System.Formats.Tar`, `Process.Kill(bool)` and `WaitForExitAsync`.

Clients are built with the internal core constructors `new PubSubHttpClient(options, mobileDevice)` and `new PubSubRealtimeClient(options, createRestFunc, mobileDevice)`, through `InternalsVisibleTo` on both heads, never through the Device or Server door types.

Assertions use FluentAssertions 6.2 and xunit 2.4.1. xunit 2.4.1 has no `Assert.Fail`, so use `UtsAssert.Fail`. StyleCop rules enforced as errors include SA1025 (no runs of multiple spaces), SA1028 (no trailing whitespace) and SX1101 (no `this.`). Files use LF line endings and end with a newline.

## SDK hooks and seams

| Seam | Kind | Used for |
|---|---|---|
| `ClientOptions.HttpClient` | public | `MockHttpClient`, as `new HttpClient(mock, disposeHandler: false)`. It sits below the SDK's host selection and retries, so each request is one attempt |
| `ClientOptions.TransportFactory` (`ITransportFactory`) | public | `MockWebSocket` |
| **H1** `ClientOptions.TimerScheduler` (`ITimerScheduler`: `Schedule(TimeSpan, Action)`, `Delay(TimeSpan, CancellationToken)`) + `ClientOptions.NowFunc` | internal, test-only | `FakeClock`. Covers connection-state `CountdownTimer`s, channel attach and detach timers, the heartbeat monitor delay, the ping timeout (`DelayCommandHandler`), the RTL13b reattach delay, and every `TimeoutAfter` (publish ack, annotations, authCallback, internet check, `ConnectionChangeAwaiter`). Default `SystemTimerScheduler.Instance` |
| **H2** `ClientOptions.DeltaDecoder` (`Func<byte[], byte[], byte[]>`, base then delta) → `DecodingContext.DeltaDecoder` | internal, test-only | `MockVCDiffDecoder` and `FailingMockVCDiffDecoder`. When null, the SDK uses the real `DeltaDecoder.ApplyDelta` |
| `ConnectionManager.HandleNetworkStateChange(NetworkState)` + `Connection.NetworkState` | internal | `MockNetworkListener` (per client) |
| `ClientOptions.Logger` | internal | Each client gets its own logger |
| `Workflow.QueueCommand(EmptyCommand.Instance)`, `Workflow.IsProcessingCommands()` | internal | `ProcessPendingEvents` |
| `AblyHttpRequester.Client` / `CreateInternalHttpClient` | internal | Sets the HTTP timeout to infinite under a fake clock |

H1 and H2 are the only production changes. Both are internal, cloned by `ClientOptions`, and leave default behaviour unchanged.

## Symbol map: `mock_http.md`

All of these live in `Ably.PubSub.Tests.Uts.Infra.Unit`.

| Pseudocode | C# |
|---|---|
| `MockHttpClient(onConnectionAttempt:, onRequest:)` | `new MockHttpClient(onConnectionAttempt: conn => …, onRequest: req => …)`. `OnConnectionAttempt` and `OnRequest` can be reassigned mid-test |
| `install_mock(mock_http)` / `uninstall_mock()` | `InstallMock(mockHttp)` / `UninstallMocks()` on the unit base. Applies to clients built afterwards |
| `await_connection_attempt(timeout?)`, `await_request(timeout?)` | `await mock.AwaitConnectionAttempt(timeout?)`, `await mock.AwaitRequest(timeout?)` |
| `reset()` | `mock.Reset()`. Clears the record, waiters, parked attempts and the queue; handlers stay |
| `PendingConnection.host/port/tls/timestamp` | `PendingHttpConnection.Host/Port/Tls/Timestamp` |
| `conn.respond_with_success/_refused/_timeout/_dns_error()` | `RespondWithSuccess()`, `RespondWithRefused()` (`HttpRequestException` wrapping a `WebException` ConnectFailure), `RespondWithTimeout()`, `RespondWithDnsError()` (NameResolutionFailure). A refused attempt is not captured as a request |
| `request.url`, `.path`, `.query_params` / `.queryParams`, `.method`, `.headers`, `.body`, `.timestamp` | `PendingHttpRequest.Url` (`RecordedUrl`), `.Path`, `.QueryParams`, `.Method`, `.Headers` (case-insensitive; request and content headers merged; `Host` is the URI authority HttpClient sends, unless the request set its own), `.Body` (`byte[]`), `.BodyText`, `.Timestamp`, `.Message` |
| `parse_json(request.body)` | `JObject.Parse(request.BodyText)` |
| `req.respond_with(status, body, headers?)` | `RespondWith(int status, object body = null, IDictionary<string, string> headers = null)`. `byte[]` is sent as is, a string as UTF-8, and anything else serialised as JSON |
| `respond_with_delay(ms, status, body)` | `RespondWithDelay(long ms or TimeSpan, status, body, headers)`. Runs on the fake clock when one is installed |
| `req.respond_with_timeout()` | `RespondWithTimeout()` (see Known gaps) |
| legacy `queue_response`, `queue_responses`, `queue_response_for_host`, `queue_response_for_url`, `queue_timeout`, `queue_delayed_response` | `QueueResponse`, `QueueResponses(count, …)`, `QueueResponseForHost`, `QueueResponseForUrl`, `QueueTimeout`, `QueueDelayedResponse(TimeSpan, …)` |
| legacy `mock_http.captured_requests` | `mock.CapturedRequests` (a snapshot). `ConnectionAttempts` and `HandlerErrors` are also available |

**Idle (`MockHttpClient.IsIdle`, drained by `process_pending_events()` and `ADVANCE_TIME`):** a request is idle only while it is parked on something only the test or the fake clock completes (an answer, a fake delay or timeout) and none of those has completed. The moment the test answers, the request reads busy (the answer completes asynchronously, so the mock's continuation runs later on the pool), and it stays busy until `SendAsync` has returned the response. What the SDK does with the response after that is visible to the drain only when it runs inside a workflow command.

**Answering order for each attempt:** a waiter that was already registered (the handler is then skipped), then a queued response (requests only), then the handler. With no handler, a connection attempt succeeds. A request that nothing answers is parked for the next `Await*` call. An attempt that is still unanswered after `UnansweredTimeout` (default 5 s, real time) fails loudly.

## Symbol map: `mock_websocket.md`

| Pseudocode | C# |
|---|---|
| `MockWebSocket(onConnectionAttempt:, onMessageFromClient:, onTextDataFrame:, onBinaryDataFrame:)`, `create_mock_websocket()` | `new MockWebSocket(onConnectionAttempt, onMessageFromClient, onTextDataFrame, onBinaryDataFrame)`. Each handler is a reassignable property (`mock_ws.onConnectionAttempt = …` maps to `mock.OnConnectionAttempt = …`) |
| `install_mock(mock_ws)` | `InstallMock(mockWs)`. One mock serves the client for its whole life; a reconnect is a new attempt on the same timeline |
| `conn.url`, `.protocol`, `.timestamp`, `conn.queryParams` | `PendingWebSocketConnection.Url` (`RecordedUrl`), `.Protocol` (always `application/json`), `.Timestamp`, `.QueryParams` |
| `conn.respond_with_success(CONNECTED_MESSAGE)` | `conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage())`. This completes the connection first, then delivers CONNECTED asynchronously. With no argument, the client stays CONNECTING |
| `respond_with_connected()` | `conn.RespondWithConnected()` |
| `respond_with_refused/_dns_error()` | `RespondWithRefused()` (`SocketException` 10061), `RespondWithDnsError()` (11001) |
| `respond_with_timeout()` | `RespondWithTimeout()` (see Known gaps) |
| `respond_with_error(msg, then_close = true)` | `RespondWithError(JObject errorMessage, bool thenClose = true)` |
| `mock_ws.active_connection` (undocumented) | `mock.ActiveConnection` (a `MockConnection`, or null). Also `mock.Connections` |
| `.send_to_client(msg)`, `.send_to_client_and_close(msg)` | `SendToClient(JObject)`, `SendToClientAndClose(JObject)`. Available on `MockWebSocket` (acting on the active connection), `MockConnection` and `PendingWebSocketConnection` |
| `.simulate_disconnect(error?)`, `close_from_server()` | `SimulateDisconnect(ErrorInfo error = null)` (unclean), `CloseFromServer()` (clean, no message) |
| `send_to_client_raw(json)` | `SendToClientRaw(string json)` or `SendToClientRaw(JObject)`. The text is delivered verbatim, so unknown actions and fields survive |
| `send_ping_frame()` | `SendPingFrame()` (see Known gaps) |
| `await_connection_attempt(timeout?)` | `await mock.AwaitConnectionAttempt(timeout?)`. Returns the oldest parked attempt that is still live first; one answered meanwhile or abandoned by the client is skipped (`PendingWebSocketConnection.Answered` / `.Abandoned`) |
| `await_next_message_from_client(timeout?)` | `await mock.AwaitNextMessageFromClient(timeout?)`. Only messages sent after the call |
| `await_client_close(timeout?)` | `await mock.AwaitClientClose(timeout?)`, returning a `ClientCloseEvent` |
| `events`, `events.filter(e => e.type == X)` | `mock.Events`, `mock.EventsOfType(MockEventType.X)`. Each `MockEvent` has `.Type`, `.Timestamp`, `.Data`, `.Message` and `.Json` |
| `CONNECTION_ATTEMPT` … `CLIENT_CLOSE` | `MockEventType.ConnectionAttempt`, `ConnectionSuccess`, `ConnectionFailure`, `MessageFromClient`, `MessageToClient`, `PingFrame`, `ServerDisconnect`, `ClientClose` |
| `mock_ws.connect_attempts` (undocumented) | `mock.ConnectionAttempts` |
| `mock_ws.last_connect_url` (undocumented) | `mock.LastConnectUrl` (`RecordedUrl`) |
| client messages captured | `mock.MessagesFromClient`, `mock.MessagesFromClientOfAction(ProtocolMessages.Attach)`, `await mock.AwaitProtocolMessages(action, count, timeout?)` |
| `reset()` | `mock.Reset()`. Clears the timeline, waiters, parked attempts and handlers added by `OnMessage`/`OnClientMessage`; handler properties stay |
| alternative API `on_connect(respond_with: msg)` / `on_connect(handler)` | `mock.OnConnect(JObject respondWith)` / `mock.OnConnect(Action<PendingWebSocketConnection>)`. Both set `OnConnectionAttempt` |
| `on_message(action:, respond_with: msg)` / `on_message(action:, handler)` | `mock.OnMessage(int action, JObject respondWith)` / `mock.OnMessage(int action, Action<ProtocolMessage>)`. Handlers are additive |
| `on_client_message(handler)` | `mock.OnClientMessage(Action<ProtocolMessage>)`. Additive |
| `await_client_message(action:)` | `await mock.AwaitClientMessage(int action, timeout?)`. Returns the oldest unclaimed message with that action, **including one sent before the call** (one cursor per action) |
| `ProtocolMessage(action: X, …)` inline | A `JObject` literal with `ProtocolMessages` constants (`["action"] = ProtocolMessages.Attached`) |

**Templates (`ProtocolMessages`).** Every call returns a fresh `JObject`.

| Pseudocode | C# |
|---|---|
| `CONNECTED_MESSAGE` | `ConnectedMessage()`. Id `test-connection-id`, key `test-connection-key`, `connectionStateTtl` 120000 and `maxIdleInterval` 15000 (wire ms). Also `ConnectedMessage(connectionId, connectionKey, clientId, connectionStateTtl, maxIdleInterval)` |
| `CLOSED_MESSAGE` | `ClosedMessage()` |
| `HEARTBEAT_MESSAGE` | `HeartbeatMessage()` |
| `DISCONNECTED_MESSAGE` | `DisconnectedMessage(code = 80003, message = "Connection disconnected", statusCode = null)` |
| `ERROR_MESSAGE(code, message)` | `ErrorMessage(code, message, statusCode?)`. `statusCode` defaults to exactly `code / 100`, so 80000 gives 800. `ChannelErrorMessage(channel, …)` builds a channel-scoped ERROR |
| `PING_MESSAGE(id)` | `PingMessage(id)` |
| ATTACHED, DETACHED, MESSAGE, PRESENCE, SYNC, ACK, NACK, AUTH | `AttachedMessage`, `AttachedWithPresenceMessage`, `DetachedMessage`, `ServerDetachedMessage`, `MessageProtocolMessage`, `PresenceProtocolMessage`, `SyncMessage`, `PresenceEntry`, `AckMessage(msgSerial, count)`, `NackMessage`, `AuthMessage`, `ErrorObject` |
| `ATTACH`, `MESSAGE_ACTION_INT`, `HAS_PRESENCE` | `ProtocolMessages.Attach` (… `Pong` = 23), `ProtocolMessages.Message`, `ProtocolMessages.HasPresenceFlag` |

## Symbol map: `mock_vcdiff.md` and `MockNetworkListener`

| Pseudocode | C# |
|---|---|
| `MockVCDiffEncoder().encode(base, value)` | `new MockVCDiffEncoder().Encode(string, string)` gives `encodeURIComponent(base) + "/" + encodeURIComponent(value)`. `Encode(byte[], byte[])` gives the UTF-8 bytes of the unpadded base64url form |
| `MockVCDiffDecoder(onDecode?)`, `decode(delta, base)`, `number_of_calls`, last base and delta | `new MockVCDiffDecoder(onDecode: (delta, @base) => …)`, `Decode(byte[] delta, byte[] @base)` / `Decode(string, string)`, `NumberOfCalls`, `LastBase`, `LastDelta`. Throws "Base mismatch" or "Invalid delta format" |
| `FailingMockVCDiffDecoder` | `new FailingMockVCDiffDecoder()` (`NumberOfCalls`) |
| `plugins: {vcdiff: decoder}` | `InstallMock(decoder)` before the client is built (hook H2). Any delegate can be installed with `InstallDeltaDecoder(Func<byte[], byte[], byte[]>)` |
| `MockNetworkListener()`, `install_mock(listener)` | `new MockNetworkListener()`, `InstallMock(listener)`. Binds to every realtime client the base builds afterwards |
| `simulate_network_lost()` / `simulate_network_available()` | `listener.SimulateNetworkLost()` / `SimulateNetworkAvailable()`. Sets `Connection.NetworkState` and calls `ConnectionManager.HandleNetworkStateChange` for the bound clients only |

## Waits, time and assertions

| Pseudocode | C# | Notes |
|---|---|---|
| `AWAIT_STATE client.connection.state == ConnectionState.connected` (also `AWAIT_STATE(connection, "connected")`, bare `CONNECTED`) | `await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected, timeout?)` | Subscribes, then checks, and completes once. Fails fast if FAILED is entered while waiting for another state (a client already FAILED when the wait starts is not a failure). Default 10 s (`UtsWait.DefaultTimeout`); the integration base's wrapper defaults to 15 s |
| `AWAIT_STATE channel.state == ChannelState.attached` | `await UtsWait.AwaitChannelState(channel, ChannelState.Attached, timeout?)` | The same guarantees |
| `… WITH timeout: N seconds` | the `timeout:` argument, or `await UtsWait.WithTimeout(task, TimeSpan, "what")` for a bare `AWAIT` | Wall-clock (`Stopwatch`) |
| record-and-verify (`state_changes = []` + `connection.on(...)`) | `var states = UtsRecord.ConnectionStates(client.Connection)` (or `ConnectionStateChanges`, `ChannelStates`, `ChannelStateChanges`) | Subscribe before the stimulus |
| `captured = []` / `.append` / `.push` in an SDK callback | `new CaptureList<T>()`, `.Add`, `.Snapshot()` | Safe under concurrent appends |
| `poll_until(cond, interval, timeout)`, `x = poll_until(...)` | `await UtsWait.PollUntil(() => bool, timeout?, interval?, description?)`, or `T x = await UtsWait.PollUntil<T>(() => valueOrNull, …)` (sync or async conditions) | Returns the settled value. A throwing condition aborts at once. Default interval 50 ms, default timeout 10 s; integration specs pass 10–30 s |
| `poll_until_success(...)` | `await UtsWait.PollUntilSuccess<T>(async () => …, timeout?, interval?)` or the `Func<Task<bool>>` form | An exception means "not yet". On timeout the last error becomes `UtsHelperException.Cause` |
| `process_pending_events()` | `await ProcessPendingEvents()` (unit base) | Queues `EmptyCommand` on each live realtime client, then yields with no real delay until every workflow and mock is idle on two consecutive checks. With fake timers enabled it uses the `ADVANCE_TIME` drain, so a workflow parked on a fresh fake delay (an authCallback's `TimeoutAfter`, an unanswered token request's timeout) settles after 250 ms instead of failing; it never moves fake time. Bounded at 10 s |
| `enable_fake_timers()` | `EnableFakeTimers(start?)` (unit base; idempotent; returns the `FakeClock`) | Applies to built and unbuilt clients and mocks. Default start 2026-01-01T00:00Z |
| `ADVANCE_TIME(ms)` | `await AdvanceTime(ms)` (or `TimeSpan`) | Fires due timers in time order, draining after each, until quiescent. A queue counts as parked (so the advance fires the next timer rather than waiting for idle) only if it has stayed busy for 250 ms **and** the clock holds a pending `ITimerScheduler.Delay` created since a drain last saw every queue idle: the SDK awaiting a fake-clock `TimeoutAfter` (the authCallback bound), a mock HTTP timeout or `respond_with_delay`. A queue busy while only `Schedule` timers or delays already pending at an idle moment (the heartbeat monitor's) are pending is just slow, so the drain keeps waiting and fails at 10 s. A callback that throws is recorded (`Clock.CallbackErrors`), the advance fires the rest, then fails at the test's line. A `Schedule` with `Timeout.InfiniteTimeSpan` never fires. Fails if fake timers are not enabled |
| `NOW()`, `now_millis`, `current_fake_time` | `Clock.Now`, `Clock.NowMillis` | |
| `SCHEDULE_AFTER(100ms):` in a handler | `Clock.ScheduleAfter(TimeSpan.FromMilliseconds(100), () => …)` | |
| `TestClock()`, `WITH_CLOCK`, `test_clock.advance/now` | `EnableFakeTimers()` before the client, then `await AdvanceTime(…)` and `Clock.Now` | A stand-in (see Known gaps) |
| `ASSERT x CONTAINS_IN_ORDER [a, b, c]` | `UtsAssert.ContainsInOrder(x, new[] { a, b, c })` | An in-order subsequence; interleaving and repeated items are allowed |
| `FAIL("msg")` | `UtsAssert.Fail("msg")` | Attributed to the caller |
| `logHandler: (level, message) => captured_logs.push(…)`, `captured_logs`, `captured_log_messages` | `var sink = new CapturingLogSink(); options.LogHandler = sink;` then `sink.Logs` and `sink.Contains(LogLevel.Warning, "text")` | Each client has its own logger, so the sink only sees that client |
| `CLOSE_CLIENT(client)` | `await CloseClient(client)` | Uses the same rules as teardown |
| `AFTER EACH TEST` | Automatic: `UtsTestBase.DisposeAsync`. Register extras with `OnTeardown(…)` | |
| SDK deviation (runtime skip) | `[DeviationFact("RTNxx")]` / `[DeviationTheory("RTNxx")]` | Skipped unless `RUN_DEVIATIONS` is set to any value. The skip text points to `deviations.md`. In the unit tier pass `Timeout = UtsTestTimeouts.DefaultTestTimeoutMs` on an async test |
| a unit test (hang guard) | `[UtsFact]` / `[UtsTheory]` on every async unit test; `[Fact]` only on a synchronous one | xUnit fails an async test that runs past 30 s (`UtsTestTimeouts.DefaultTestTimeoutMs`), so an SDK operation that never completes (its fallback timer sits on the fake clock) fails the test instead of stalling the run. xUnit 2.4 honours `Timeout` on async tests only |
| `random_id()`, `unique_channel_name()`, `extract_key_name`, `extract_key_secret`, `get_key_parts`, `generate_jwt`, `request_token_from_sandbox` | `RandomId(byteCount = 6)` (base64url), `UniqueChannelName(prefix)` (`test-<prefix>-<id>`), `ExtractKeyName`, `ExtractKeySecret` (use both for `get_key_parts`), `GenerateJwt(keyName, keySecret, ttl?, clientId?, capability?, expiresAt?)` (spec ttl is in ms: `TimeSpan.FromMilliseconds`; spec `expires_at` → `expiresAt`, which sets `exp` and puts `iat` `ttl` (default 1 h) before it, so `expiresAt: DateTimeOffset.UtcNow.AddSeconds(-5)` gives an expired token, 40142), `RequestTokenFromSandbox(apiKey?, tokenParams?)` | Integration base |
| `auth_callback_count++` around a sandbox token | `SandboxTokenCallback(apiKey?, onInvoked: p => count++)` | |

Every helper that can fail takes `[CallerFilePath]`/`[CallerLineNumber]` and puts `File.cs:line:` first in its message.

## Threading model

- **SDK callbacks** (`Connection.On`, `channel.On`, `Subscribe`, `authCallback`) run on the realtime workflow's thread, or on a timer or HTTP continuation, never on the test's thread. Capture into `CaptureList<T>`, and don't block inside callbacks.
- **Mock deliveries to the SDK:** every `MockTransport` posts each listener call to **one ordered async chain per transport** (thread pool, never inline). That covers the Connected and Closed transport events and every message to the client. The timeline (`Events`) is recorded synchronously when the test acts. `MockTransport.Close(false)` notifies Closed asynchronously, and `RespondWithSuccess` posts Connected before CONNECTED.
- **`MockWebSocket` handlers** run synchronously on the SDK's workflow thread, inside `ITransport.Connect`/`Send`, so they **must not block or await**. Anything they send is delivered later, in order. A handler's exception goes into `HandlerErrors`, is rethrown into the SDK, and fails the test at teardown.
- **`MockHttpClient` handlers** run synchronously on the SDK's calling thread, inside `HttpClient.SendAsync`. The same rules apply.
- **Mixing handler and await styles** (as implemented):
  - **Connection attempts (WS and HTTP):** an `AwaitConnectionAttempt` registered *before* the attempt takes it, and the handler is not called. Otherwise the handler runs. With no handler, or if the handler leaves the attempt unanswered, the attempt goes to an await registered meanwhile or is parked for the next `AwaitConnectionAttempt`. **WS only:** a parked attempt that was answered meanwhile (a handler's `SCHEDULE_AFTER(…) respond_with_success`) or abandoned by the client (its transport closed or disposed first, e.g. the connect timer fired) is skipped, so the await gets the next live attempt. HTTP behaves the same otherwise, except that a connection attempt with no handler succeeds.
  - **WS client messages:** handlers and awaits mix freely. Each message is recorded, then every handler runs (`OnTextDataFrame`, `OnMessageFromClient`, then the added `OnMessage`/`OnClientMessage` handlers), then waiters complete (in a `finally`, so even when a handler throws). **An await never consumes an event.**
  - **HTTP requests:** an await registered first wins, then the queue, then `OnRequest`. An unanswered request is parked for `AwaitRequest`.
- **Time:** `FakeClock` never waits on the real clock. `FakeClock.Delay` completes synchronously when it fires, so continuations after `await Delay` run inside the advance. Every wait and poll deadline in `UtsWait` is wall-clock (`Stopwatch`), so a fake clock can't freeze it.

## Unit-tier defaults (`UtsUnitTestBase.Rest` / `Realtime`)

The factories change the options **in place**. They apply these defaults:

- `HttpClient` is the installed `MockHttpClient`. With none installed (and no `HttpClient` set), the base uses a **strict default** (`MockHttpClient.Strict()`, exposed as `DefaultHttp`): `Defaults.InternetCheckUrl` gets 200 `yes`, and anything else gets a loud 500 and is recorded in `UnexpectedRequests`, which fails the test at teardown.
- For realtime, `TransportFactory` is the installed `MockWebSocket`. With none installed, a **default that refuses every attempt** (`DefaultWebSocket`) is used, so no unit test reaches the network.
- `NowFunc` and `TimerScheduler` (H1) follow the fake clock whenever one is enabled, through a switching scheduler. With a fake clock, the client's internal `HttpClient.Timeout` is infinite, and `MockHttpClient` enforces `HttpRequestTimeout` on fake time.
- `DeltaDecoder` (H2) is set from an installed mock decoder.
- `AutomaticNetworkStateMonitoring = false`.
- Each client gets its own logger: `InternalLogger.Create(options.LogLevel, options.LogHandler ?? new DefaultLoggerSink())`, unless the test set `Logger`.
- Each client gets its own `LocalDevice.Create(mobileDevice: new FakeMobileDevice())`. This keeps it off the process-wide `LocalDevice`/`IoC.MobileDevice` that native push tests change.
- Teardown disposes every realtime client without sending CLOSE (`CloseClientsOnTeardown => false`). It then fails the test on mock `HandlerErrors` or unexpected strict-default requests.

The factories deliberately **don't** default these: `AutoConnect`, `FallbackHosts` and `SkipInternetCheck` are used exactly as the spec's options give them, so translate the spec's `autoConnect: false`. `ValidKey` / `ValidOptions()` give the specs' `"appId.keyId:keySecret"`.

## Integration tier

- **`SandboxApp`:** the app is created from the embedded `test-app-setup.json`. The `mutable` namespace (`mutableMessages: true`) is added when it's missing.
  - The request is `POST https://<Defaults.GetPrimaryDomain(endpoint)>/apps`, sent **exactly once**: no retry, a 30 s bound (`RequestTimeout`), and the status checked before the body is parsed.
  - `DeleteAsync()` sends `DELETE /apps/{id}` with Basic auth on the first key. It is best-effort, retried once, and never throws.
  - Endpoint: `ABLY_ENDPOINT` (trimmed; the same variable as the native suite), otherwise `nonprod:sandbox` (host `sandbox.realtime.ably-nonprod.net`).
- **Lifecycle:** each collection gets one app (`SandboxAppFixture`, provisioned in `InitializeAsync`, deleted in `DisposeAsync`), and its classes run serially. "UTS Proxy" provisions its own app. A full infra run creates **4 apps** (one per collection plus one for each of the two lifecycle self-tests) and deletes all of them (`DELETE /apps/{id}` answered 204). Each delete is reported as an xUnit diagnostic message (`UTS sandbox: DELETE /apps/… -> 204 (deleted: True)`): shown by the xunit console with `-diagnostics`, but by `dotnet test` (VSTest adapter 2.4.1) only with `"diagnosticMessages": true` in `xunit.runner.json`, which the repo doesn't set. A failed provisioning fails the whole collection; nothing is skipped.
- **Factories** (`Rest(options = null)`, `Realtime(options = null)`):
  - `Endpoint` is set to the app's endpoint unless the test set one.
  - `Key = app.Keys[0]` unless the test set any auth (`Key`, `Token`, `TokenDetails`, `AuthCallback` or `AuthUrl`).
  - The per-client logger, `LocalDevice` and `AutomaticNetworkStateMonitoring = false` match the unit tier.
  - `AutoConnect`, `Tls` and fallback hosts are untouched.
- **Waits are wall-clock:** state waits default to `StateTimeout` (15 s), and `PollUntil` takes an explicit 10–30 s. Infra tests also bound each test with `[Fact(Timeout = …)]`. Teardown sends `Close()` and waits for CLOSED (up to `CloseTimeout`, 10 s) from any state that can reach it, then disposes.

## Proxy tier

- **Pinned binary:** uts-proxy **v0.3.0** (`ProxyManager.Version`). The release assets are `uts-proxy_0.3.0_{darwin,linux}_{amd64,arm64}.tar.gz`, each with a pinned SHA-256 checked against the downloaded archive before extraction. **There is no Windows release binary.**
- **Cache:** `~/.cache/uts-proxy/v0.3.0/uts-proxy`, with the lock file `uts-proxy.lock` beside it. The cache is **shared on purpose with ably-java and ably-cocoa**, so keep the layout identical. A binary already there is reused without re-hashing (only archives are pinned).
- **Install:** under the lock (`FileShare.None`, retried for up to 5 min), download into a staging folder (3 attempts), verify, extract with the system `tar`, `chmod`, and only then rename into place.
- **Overrides:**
  - `UTS_PROXY_LOCAL_PATH` points at a locally built binary, which is started where it is. On Windows, or on any platform without a pinned asset, the tier **fails** with a message naming this variable. It never skips.
  - `UTS_PROXY_CONTROL_PORT` changes the control port from the default 9100.
- **Process:** one proxy per test process on the control port, started with `-port <port>`. Startup waits up to 15 s for `GET /health`, and the proxy is killed on process exit. A foreign process already answering on the port is a failure. All proxy classes share "UTS Proxy" and run serially.
- **Session:** `CreateProxySession(rules?, endpoint?, port?, timeoutMs?)` defaults the endpoint to the sandbox when only `rules:` is given. The endpoint is resolved client-side into `target{realtimeHost, restHost}`. The session timeout is 120 s (`ProxySession.DefaultTimeoutMs`), a backstop only; teardown closes every session after the clients. Control-API errors (400, 409) fail at the caller's line.
- **Proxied clients:** `ProxyRealtime(session, options?)` / `ProxyRest(session, options?)` set `Endpoint = "localhost"`, `Port` and `TlsPort` to the session port, and `Tls = false`. With no options at all, `ProxyRealtime` uses `AutoConnect = false`. Every corpus proxy spec sets `autoConnect: false` explicitly, so keep it.
- **Proxy auth:** Basic auth can't cross the plain hop, because the SDK refuses Basic without TLS (RSC18, `AblyAuth.EnsureSecureConnection`). Unless the test set token auth itself, the client gets an `AuthCallback` returning a real `TokenDetails` from `RequestTokenFromSandbox`. That call goes directly to the sandbox over TLS with the test's `Key` or the app key, so it adds nothing to the session log.
- **Rules and log:**
  - Builders: `ProxyRules.Rule(match, action, times?, comment?)`; matches `WsConnect`, `WsFrameToClient(action, channel?)`, `WsFrameToServer`, `HttpRequest`, `DelayAfterWsConnect`; actions `Passthrough`, `RefuseConnection`, `AcceptAndClose`, `Disconnect`, `Close`, `Suppress`, `Delay`, `InjectToClient`, `InjectToClientAndClose`, `Replace`, `SuppressOnwards`, `HttpRespond`, `HttpReplaceResponse`, `HttpDelay`, `HttpDrop`; positions `ProxyRules.Append` / `Prepend`.
  - `add_rules` maps to `AddRulesAsync`, `trigger_action` to `TriggerActionAsync`, and `get_log()` to `GetLogAsync()`, which returns typed `ProxyEvent`s (`Type`, `Direction`, `Message`, `MessageAction`, `QueryParams`, `CloseCode`, `RuleMatched`, …).
  - Log filters: `WsConnects()`, `WsDisconnects()`, `FramesToClient(action?)`, `FramesToServer(action?)`, `HttpRequests(pathContains?)`, `HttpResponses()`, `MatchedBy(comment)`.
  - `"__PASSTHROUGH__"` is an ordinary string.
- **Credentials stay out of output:** `ProxyEvent.ToString()` and the kept proxy output (`ProxyManager.RecentOutput`) are redacted by `ProxyRedaction`: the `accessToken` and `key` query values (in a URL, a path or `queryParams`) and any `Authorization` header become `<redacted>`. The typed fields (`QueryParams`, `Url`, `Raw`) stay unredacted for assertions, so never print them directly; print the event or use `ProxyRedaction.RedactText`.
- **Control API, confirmed against the real uts-proxy v0.3.0** (infra test runs plus a direct probe):
  - CLI: `-port <int>` (control API port, default 9100). `GET /health` answers 200 `{"ok":true}`.
  - `POST /sessions {target:{realtimeHost,restHost},rules,timeoutMs}` answers 201 `{"sessionId":…,"proxy":{"host":"localhost:N","port":N}}`. A numeric `match.action` is refused with 400 (`cannot unmarshal number into … match.action of type string`), hence the stringifying builders.
  - `GET /sessions/{id}` answers 200 `{port,ruleCount,sessionId,target}`; `ruleCount` drops once a `times: 1` rule has fired. `POST /sessions/{id}/rules {rules,position}` answers 200 `{"ruleCount":N}`.
  - `POST /sessions/{id}/actions` with no live WebSocket answers 409 `no active WebSocket connection`, but still logs an `action` event.
  - `DELETE /sessions/{id}` answers 200 with the event log; after it, `GET` and `DELETE` answer 404.
  - `GET /sessions/{id}/log` answers `{"events":[…]}`. Every event has an RFC3339 `timestamp` and `ruleMatched` (the rule's comment, or JSON `null` for pass-through). `ws_connect`: `url`, and `queryParams` as a map of string to string holding only the **first** value of a repeated key. `ws_frame`: `direction` (`server_to_client` / `client_to_server`), `message` with a numeric `action`. `ws_disconnect`: `initiator` (`proxy`, `client`, `server`), no `closeCode` in the server- and proxy-initiated cases seen. `http_request`: `method`, `path`, `direction` and `headers` (including `Authorization: Bearer <token>`). `http_response`: `status`. `action`: `message:{type}`, `initiator: "proxy"`.

## Protocol: JSON only

msgpack is compiled out of this SDK build (decision log #6), and `ClientOptions.UseBinaryProtocol` is a no-op. The infra has no msgpack variant, so translate a protocol-parameterised test once, for JSON. `OptionalMocksSmokeTests.BinaryProtocolIsCompiledOutSoTheInfraHasNoMsgpackVariant` is a tripwire that fails if binary support ever compiles in. The proxy relays text frames only.

## SDK behaviour a translator meets (not infra gaps)

- **FAILED left without `connect()` (RTN27h, via RTN14g/RTN15j) — fixed:** after a fatal connection-level ERROR followed by the server closing (the `respond_with_error` default), the SDK could go FAILED and then back to CONNECTING. `RealtimeWorkflow` `HandleTransportEventCommand` now defers a transport close once, behind the commands the earlier messages queued, so the ERROR is handled first; `StateWaitTests.ConnectionStaysFailedWhenTheServerClosesAfterAFatalError` runs by default and guards it. The original finding follows. RTN27h: FAILED "will remain in this state indefinitely, unless and until an explicit call to `connect()` is made". This is a race in the SDK, not the mock (the mock delivers ERROR before the close on one ordered chain, as the real transport does): the ERROR's handling is queued (`ConnectionConnectingState.cs:66-71`, `ConnectionConnectedState.cs:63-69`) behind the close's retry, and the queued `SetConnectingStateCommand` (`RealtimeWorkflow.cs:1023`) runs after FAILED without a state check. It affected the RTN14g, RTN15j and RTN25 (`error-reason-protocol-error-5`) specs.
- **5xx ERROR while CONNECTING goes to DISCONNECTED, not FAILED (RTN14g; deterministic):** `HandleConnectingErrorCommand` sends any ERROR whose `statusCode` is 500..504 to DISCONNECTED and retries (`RealtimeWorkflow.cs:561-565`, `error.IsRetryableStatusCode()`). RTN14g says an ERROR with an empty `channel` "for any reason other than RTN14b" moves the connection to FAILED; RTN17f1 grants 5xx retry only to a DISCONNECTED response. So `realtime/unit/RTN14g/error-empty-channel-failed-0` and `realtime/unit/RTN25/error-reason-protocol-error-5` (both use `statusCode: 500`) fail in this SDK on every run. RTN15j (CONNECTED) is unaffected by this one. Evidence: `specifications/features.md` RTN14g and RTN17f1 ("a `DISCONNECTED` response with an `error.statusCode` in the range `500 <= code <= 504`"), and the code path above.
- **Instant first retry:** after a failed first attempt the SDK retries at once, even with `FallbackHosts` empty, and its jittered backoff can retry before 15 s. Read `ConnectionStateChange.RetryIn` rather than assuming `disconnectedRetryTimeout`.

## Known gaps

Each row is keyed by the exact pseudocode token, so a preflight grep over the selected specs' `pseudo` fences can match it.

| Pseudocode token | Tier | Status | Workaround |
|---|---|---|---|
| `onBinaryDataFrame` | unit | unsupported: JSON only (decision log #6) | `MockWebSocket.OnBinaryDataFrame` exists but never fires. Inspect frames with `OnTextDataFrame` (raw JSON); a msgpack-only test is language-inapplicable |
| `MSGPACK_DECODE` | unit | unsupported: JSON only | Decode the text frame with `JObject.Parse` in `OnTextDataFrame`; a msgpack-only test is n/a |
| `msgpack_decode` | unit | unsupported: JSON only | n/a; the JSON variant only |
| `msgpack_encode` | unit | unsupported: JSON only | n/a; the JSON variant only |
| `msgpack_deserialize` | unit | unsupported: JSON only | n/a |
| `msgpack_serialize` | unit | unsupported: JSON only | n/a |
| `x-msgpack` | unit | unsupported: JSON only | Assert `application/json` only; msgpack content-type cases are n/a |
| `useBinaryProtocol: true` | unit, integration | unsupported: `UseBinaryProtocol` is a no-op without MSGPACK | Mark the test language-inapplicable; don't translate it as JSON silently |
| `useBinaryProtocol: PROTOCOL` | integration, proxy | partial: JSON variant only | One run with `UseBinaryProtocol = false` (or unset); no theory over protocols |
| `send_ping_frame` | unit | stand-in: records `PING_FRAME` only | RTN23b is n/a: `ClientWebSocket` surfaces no pings and the SDK sends `heartbeats=true`. Use `HEARTBEAT_MESSAGE` (RTN23a) |
| `ClientCloseEvent` | unit | partial: `.code` is always 1000, `.reason` always null | `ITransport` carries no close code. Assert `ClientCloseEvent.ViaDispose` / `SuppressClosedEvent`, or count `MockEventType.ClientClose` events. The SDK closes through `Dispose()`, so async close is observable only via `MockTransport.Close(false)` |
| `respond_with_timeout` | unit | partial | **WS:** records `CONNECTION_FAILURE`, but the SDK hears nothing and its own connect timer decides, so `EnableFakeTimers()` and `AdvanceTime` past the realtime request timeout. **HTTP:** with a fake clock it fails with `TaskCanceledException` after `HttpRequestTimeout` of fake time; without one it fails at once (stand-in). Connection and request phases share that one timeout (the SDK has no separate open timeout) |
| `(level, message, context)` | unit | partial: no `context` argument | `ILoggerSink.LogEvent(level, message)`: capture with `CapturingLogSink`; context assertions are language-inapplicable |
| `logLevel: verbose` | unit | partial: SDK `LogLevel` is `Debug`, `Warning`, `Error`, `None` | Use `LogLevel.Debug` |
| `logLevel: info` | unit | partial: no info level | Nearest is `LogLevel.Debug`; record the mapping in the skill and treat an info-only assertion as a deviation candidate |
| `LOG_INFO` | unit | partial: no info level | As `logLevel: info` |
| `level == info` | unit | partial: no info level | As `logLevel: info` |
| `enable_fake_timers()` | unit | partial: a timer already running when fake timers are enabled stays on the real clock | Call `EnableFakeTimers()` before building the client wherever the spec allows. Each timer picks its clock when it is scheduled |
| `connection.ping()` | unit | partial: the ping-timeout continuation (`DelayCommandHandler`'s `ContinueWith`) runs asynchronously after `ADVANCE_TIME` | After the advance, `await` the ping task or `UtsWait.PollUntil` the outcome; don't assert synchronously |
| `TestClock()` | unit | stand-in | `EnableFakeTimers()` before building the client; then `test_clock.now()` maps to `Clock.Now` |
| `WITH_CLOCK` | unit | stand-in | The block scope is the test: `EnableFakeTimers()` first |
| `test_clock.advance` | unit | stand-in | `await AdvanceTime(ms)` (async, unlike the spec) |
| `MockVCDiffEncoder` | unit | partial: SDK trap | `DataHelpers.ConvertToByteArray` base64-decodes a string delta that happens to be valid base64. Send `base64(delta)` with encoding `…/vcdiff/base64`, as the spec's JSON note says. The mock decoder accepts both encoder forms |
| `CLEAR channel._lastPayload` | integration | not in infra (white-box) | RTL20 reads `RealtimeChannel.LastSuccessfulMessageIds.LastMessageId` (`ChannelMessageProcessor.cs:110,205`), not `MessageDecodingContext.PreviousPayload`. In the test: `((RealtimeChannel)channel).LastSuccessfulMessageIds.LastMessageId = null;` (internal setter, `LastMessageIds.cs:12`), only after a message has arrived: before that the property is the shared `LastMessageIds.Empty` instance (`LastMessageIds.cs:8`, `RealtimeChannel.cs:43`), so writing it would change every channel. Don't assign `LastMessageIds.Empty` (it drops the channel serial the recovery attach sends, `RealtimeChannel.cs:349-351`) |
| `load_fixtures` | unit | not an infra gap: `encoding.json` exists nowhere in ably-common (vendored 133665b or upstream main) | `messages-encoding.json` is embedded but has a different shape (`messages[]` with `data`, `encoding`, `expectedType`, `expectedValue`; the spec reads `input_data`, `expected_wire_data`, `expected_encoding`, `use_binary_protocol`). `rest/unit/RSL4/encoding-fixtures-ably-common-0` is a UTS spec error (a skipped placeholder plus a UTS Spec Errors entry); don't substitute the other fixture |
| `http_replace_response` | proxy | partial: not in `proxy.md` | `ProxyRules.HttpReplaceResponse(status, body?, headers?)` (same shape as `HttpRespond`). The action type is in the uts-proxy v0.3.0 binary; the end-to-end behaviour (request forwarded upstream, response replaced) is exercised by `rest/proxy/RSL1k4/idempotent-retry-dedup-0`. Never substitute `HttpRespond` (it doesn't forward the request upstream) |
| `mutable:` | integration, proxy | stand-in | `SandboxApp` adds the `mutable` namespace that the vendored `test-app-setup.json` lacks. Nothing to do in the test |
| `create_proxy_session` | proxy | partial: no Windows release binary | Set `UTS_PROXY_LOCAL_PATH` to a local `go build`; CI runs the tier on Linux only |
| `STANDARD_POOL_OBJECTS` | objects | unsupported: no LiveObjects API (D-28) | Don't translate `uts/objects` |
| `setup_synced_channel` | objects | unsupported (D-28) | as above |
| `setup_synced_channel_no_ack` | objects | unsupported (D-28) | as above |
| `provision_objects_via_rest` | objects | unsupported (D-28) | as above |
| `channel.object` | objects | unsupported (D-28) | as above |
| `create_proxy_session` | proxy | partial: the verified-download path has run only on a fresh cache | Locally the pinned binary was a cache hit (trusted, not re-hashed). The download, SHA-256 check and extraction run on a fresh CI runner (the `proxy` job); nothing to do in the test |
| `get_log` | proxy | partial: the log has an event type `proxy.md` doesn't document | uts-proxy v0.3.0 also logs an `action` event (`message:{type}`, `initiator: "proxy"`) for every `trigger_action`, even one answered 409. `ProxyEvent.ActionType` / `log.Actions()` declare it; filter by type rather than asserting the log's exact length |
| `trigger_action` | proxy | partial: logged even when refused | With no live WebSocket the control API answers 409 (the call fails at the caller's line) but the log still gains an `action` event. Trigger only after the client is CONNECTED |
| `http_request` | proxy | partial: the logged event carries `headers`, which `proxy.md` doesn't document | `headers` includes `Authorization: Bearer <token>`, so never print `Raw`; `ProxyEvent.ToString()` is redacted. Assert `Method` / `Path` |
| `queryParams` | proxy | partial: first value only | The proxy logs `ws_connect.queryParams` as a map holding only the first value of a repeated key (`foo=1&foo=2` gives `"1"`). Parse `ProxyEvent.Url` when a repeated parameter matters |
| `POST https://sandbox.realtime.ably-nonprod.net/apps` | integration, proxy | stand-in: one app per collection, not per spec file | `SandboxAppFixture` provisions it (never retried) and deletes it in teardown. A full infra run creates 4 apps, all deleted (204) |

**Other limits (no single pseudocode token):**

- **Clock reads outside `NowFunc`:** the synthesised RTP19 LEAVE timestamp (`Presence.cs`, `DateTimeOffset.UtcNow`), `RealtimeCommand.Created`, and `GenerateJwt`'s `iat`/`exp` use the real clock.
- **Parked WS attempts:** a WS attempt with no handler is parked rather than answered. Until the test answers it, the client stays CONNECTING and its connect timer runs. `AwaitConnectionAttempt` skips parked attempts already answered or abandoned (self-tests `MockWebSocketTests.AParkedAttemptAnsweredMeanwhileIsSkippedByTheNextAwait` / `…TheClientAbandonedIsSkippedByTheNextAwait`).
- **Teardown waits for dispose:** `client.Dispose()` only queues a DisposeCommand, and the SDK still runs commands queued before it completes the workflow (e.g. a retry timer that fired meanwhile starts a new attempt). Teardown therefore waits, up to `DisposeTimeout` (5 s), until the disposed client's command channel is completed and drained (`UtsTestBase.IsWorkflowStopped`, read by reflection because the net462 head doesn't reference System.Threading.Channels), and the unit drain (`ProcessPendingEvents`, and the fake clock's drain before and between timers) treats a disposed client as idle only then, so `AdvanceTime` after a mid-test dispose is safe too.
- **Teardown checks:** mock handler errors and unexpected strict-default requests fail the test at teardown. A test that wants a handler to throw calls `mock.Reset()` before the end.
- **Shared HTTP mock:** when one `MockHttpClient` serves several clients, the last client built sets `RequestTimeout`.
- **Network notifications (isolated):** every SDK `Connection` adds itself, in its constructor and regardless of `AutomaticNetworkStateMonitoring`, to the private static `Connection.OsEventSubscribers` that the public static `Connection.NotifyOperatingSystemNetworkState` broadcasts to. The unit and integration factories take each UTS client off that list right after construction (`UtsTestBase.DetachFromOsNetworkEvents`, which invokes the connection's private `CleanUpNetworkStateEvents` by reflection, the same clean-up `Dispose()` runs), so a native test calling the static in parallel can't disturb a UTS client. Native clients keep the SDK behaviour. A UTS test simulates a network change with `MockNetworkListener` (its own clients only). Self-test: `RealtimeUnitSmokeTests.FactoryBuiltClientIsOffTheProcessWideOsNetworkHookButANativeClientStaysOnIt`.
- **Work the idle checks can't see (F2):** the drains see the workflow's command queue and the command being processed, each mock transport's delivery chain, and in-flight mock HTTP. SDK work started with `Task.Run` or `ContinueWith` outside a command is invisible to them: `RealtimeChannel.ReattachAfterTimeout` registers its RTL13b delay inside `Task.Run`, and the heartbeat monitor loop (`RealtimeWorkflow.Start`) registers its first `Delay` on a pool thread. A drain (and so `ADVANCE_TIME` or `process_pending_events()`) can return before such a delay exists, and an advance made then puts it at now + delay instead of firing it. Continuations the SDK posts to a captured `SynchronizationContext` are invisible too. Where a spec depends on one of these, `UtsWait.PollUntil` the effect (e.g. `Clock.PendingCount` growing, a state change) before the next `ADVANCE_TIME`.
- **Teardown with a parked command (F6):** unit teardown disposes each client and waits up to 5 s (`DisposeTimeout`) for its workflow to stop, but `Dispose()` only queues a DisposeCommand behind the command being processed. If the test ends while a command is parked on a fake timer or an unanswered mock (an authCallback that never returns, a pending `authorize()`), nothing advances the clock during teardown, so the wait times out and the test fails with `UtsTeardownException`. Release what the command awaits before the test ends (complete or cancel the callback's task, answer the request), as `FakeClockTests.AdvanceTimeUnblocksAnAuthCallbackThatNeverReturns` does.
- **Dependence on SDK internals via reflection:** the infra reads `RealtimeWorkflow.CommandChannel.Reader.Completion` (teardown), relies on `RealtimeWorkflow.IsProcessingCommands()` (which itself reads the channel's private `ItemsCountForDebugger` and degrades silently to the processing flag if that property is missing), and invokes `Connection.CleanUpNetworkStateEvents` (OS-event isolation). The first and last throw a clear `InvalidOperationException` if the SDK changes shape; the `ItemsCountForDebugger` fallback doesn't.
- **Parked detection is a heuristic:** "parked" needs a fresh fake delay plus a 250 ms stall. A command that starts a delay it doesn't await and then stalls for 250 ms (JIT, GC) still reads as parked, and before the first idle drain any pending delay counts as fresh. Each timer an advance fires while the SDK really is parked costs 250 ms of real time.
- **Proxy lock:** interop with the other SDKs' locks on the shared cache is not guaranteed: the lock doesn't exclude Java's `fcntl` lock on Linux, and Mono may not lock across processes. Concurrent installs stay correct anyway, because each install verifies and then renames atomically.
- **Fixed control port per process:** one proxy per test process, on 9100 or `UTS_PROXY_CONTROL_PORT`. Two test processes at once (for example two TFMs under one `dotnet test`) collide on it; run one framework at a time, or give each its own `UTS_PROXY_CONTROL_PORT`.
- **Vendored ably-common:** its `test-app-setup.json` has no `mutable` namespace; `SandboxApp` adds it (`mutableMessages: true`). See the `mutable:` row.
- **Proxy port:** a SIGKILLed run can orphan the proxy, which then blocks port 9100 until it is killed. Use `UTS_PROXY_CONTROL_PORT` to work around it.
- **Test timeouts:** xUnit 2.4.1 has no class-level timeout, and `[Fact(Timeout)]` applies only to async tests and is ignored when parallelisation is off.
