---
name: uts-to-csharp
description: Translate Universal Test Specifications from ably/specification into ably-pubsub-dotnet tests. Use when deriving, updating or evaluating tests under src/Ably.PubSub.Tests.DotNET/Uts.
allowed-tools: Bash, Read, Edit, Write, Glob, Grep, WebFetch
---

# Translating UTS specs into ably-pubsub-dotnet tests

## Sources

Fetch the governing docs and the spec fresh at the start of every run; do not work from memory.

```bash
gh api repos/ably/specification/contents/uts/docs/writing-derived-tests.md --jq '.content' | base64 -d
gh api repos/ably/specification/contents/uts/docs/integration-testing.md --jq '.content' | base64 -d
gh api repos/ably/specification/contents/uts/docs/proxy.md --jq '.content' | base64 -d
gh api 'repos/ably/specification/contents/uts/rest/unit/<spec>.md' --jq '.content' | base64 -d
gh api 'repos/ably/specification/contents/uts/realtime/unit/<spec>.md' --jq '.content' | base64 -d
gh api 'repos/ably/specification/contents/uts/rest/integration/<spec>.md' --jq '.content' | base64 -d
gh api 'repos/ably/specification/contents/uts/realtime/integration/<spec>.md' --jq '.content' | base64 -d
```

Both integration tiers nest, so list a directory before fetching from it:
`gh api repos/ably/specification/contents/uts/realtime/integration/channels --jq '.[].name'`.

`writing-derived-tests.md` **governs**. `integration-testing.md` governs both integration tiers and
`proxy.md` the proxy package inside each. This file covers only what is particular to
ably-pubsub-dotnet.

## Layout

A spec at `uts/<tier path>/<name>.md` becomes
`src/Ably.PubSub.Tests.DotNET/Uts/<TierPath>/<Name>Tests.cs`, with each path segment PascalCased:

| Spec | Derived test |
|---|---|
| `uts/rest/unit/time.md` | `Uts/Rest/Unit/TimeTests.cs` |
| `uts/rest/unit/auth/token_renewal.md` | `Uts/Rest/Unit/Auth/TokenRenewalTests.cs` |
| `uts/realtime/unit/connection/auto_connect_test.md` | `Uts/Realtime/Unit/Connection/AutoConnectTests.cs` |
| `uts/rest/integration/history.md` | `Uts/Rest/Integration/HistoryTests.cs` |
| `uts/realtime/integration/proxy/heartbeat.md` | `Uts/Realtime/Integration/Proxy/HeartbeatTests.cs` |

Most of `uts/realtime/integration` already ends in `_test` upstream; that suffix is dropped rather
than doubled, so `channels/channel_publish_test.md` becomes
`Realtime/Integration/Channels/ChannelPublishTests.cs`.

The namespace mirrors the directory: `Ably.PubSub.Tests.Uts.Rest.Unit.Auth`. No project-file edits
are needed — `Ably.PubSub.Tests.DotNET.csproj` is SDK-style and globs `**/*.cs`. (This is why the UTS
tree lives in that head rather than in `Ably.PubSub.Tests.Shared`, whose `.projitems` enumerates
every file by hand.)

The consequence worth knowing: **UTS tests run on `net6.0` and `net7.0` only, never on `net462`.**
The .NET Framework head is a separate, non-globbing project that does not compile this tree.

## Anatomy of a derived test

```csharp
namespace Ably.PubSub.Tests.Uts.Rest.Unit
{
    /// <summary>
    /// Derived from uts/rest/unit/time.md in ably/specification.
    ///
    /// Spec points: RSC16
    /// </summary>
    public class TimeTests : UtsTestBase
    {
        public TimeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSC16/returns-server-time-0
        [Fact]
        public async Task RSC16_TimeReturnsServerTime()
        {
            var captured = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    captured.Add(req);
                    req.RespondWith(200, new object[] { ServerTimeMs });
                });

            var client = RestClient(mockHttp);

            var result = await client.TimeAsync();

            result.ToUnixTimeInMilliseconds().Should().Be(ServerTimeMs);
            captured.Should().HaveCount(1);
            captured[0].Path.Should().Be("/time");
        }
    }
}
```

- The `// UTS:` comment carries the spec's Test ID verbatim, immediately above the `[Fact]`.
- The method name is the spec point plus the Test ID slug, PascalCased, with the trailing index
  dropped: `rest/unit/RSC16/returns-server-time-0` becomes `RSC16_TimeReturnsServerTime`. The spec
  point stays upper-case so `--filter "FullyQualifiedName~RSC16"` finds it.
- A class-level `<summary>` names the spec file it was derived from and lists its spec points.
- Keep `captured` a local list appended from the handler, as the specs do, rather than reading
  `mockHttp.CapturedRequests`. Specs that genuinely want the whole timeline use the property.
- Derive from `UtsTestBase` and build clients through its `RestClient` / `RealtimeClient` helpers:
  realtime clients are then disposed for you after every test, including when the test throws.
- Assertions use FluentAssertions (`.Should()`), which is what the rest of the repo uses.

## Mapping pseudocode to C#

| Pseudocode | ably-pubsub-dotnet |
|---|---|
| `install_mock(m)` + `Rest(options: ...)` | `RestClient(m)` from `UtsTestBase` |
| `install_mock(m)` + `Realtime(options: ...)` | `RealtimeClient(m)` from `UtsTestBase` |
| `uninstall_mock()` | nothing; `UtsTestBase` disposes tracked clients |
| `ClientOptions(key: "appId.keyId:keySecret")` | the default; pass nothing |
| any other client option | `RestClient(m, configure: o => o.Xyz = ...)` |
| `AWAIT client.time()` | `await client.TimeAsync()` |
| `... FAILS WITH error` | `await act.Should().ThrowAsync<AblyException>()` |
| `error.code` / `error.statusCode` / `error.message` | `ex.ErrorInfo.Code` / `.StatusCode` / `.Message` |
| `request.url.queryParams` / `queryParameters` | `request.Url.QueryParams` (spec drift; one concept) |
| `parse_json(request.body)` | `JObject.Parse(request.BodyText)` |
| `msgpack_decode(x)` / `useBinaryProtocol: true` | **not available** — see *msgpack is compiled out* |
| `process_pending_events()` | `await Task.Yield()`, or `UtsClients.PollUntil(...)` for a premise |
| `enable_fake_timers()` / `ADVANCE_TIME(ms)` | `TestClock` for measured time, a short client option for scheduled time — see **Timers** |
| `AWAIT_STATE client.connection.state == X` | `await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.X)` |
| `AWAIT_STATE channel.state == X` | `await UtsClients.AwaitChannelState(channel, ChannelState.X)` |
| `AWAIT UNTIL <premise>` / `poll_until` (unit tier) | `await UtsClients.PollUntil(() => ..., "description")` |
| `poll_until(interval:, timeout:)` (integration tier) | `await UtsSandbox.WallClockPollUntil(...)` |
| `CONTAINS_IN_ORDER` | `UtsClients.ContainsInOrder(observed, a, b, c)` |
| `mock_ws.active_connection` | `mockWs.ActiveConnection` |
| `mock_ws.active_connection.close()` | `mockWs.SimulateDisconnect()` — the spec means the *server* closing |
| `random_id()` | `UtsSandbox.RandomId()` |

Client options are PascalCase. Check the real property on `ClientOptions`
(`src/Ably.PubSub.Shared/ClientOptions.cs`) before assuming one exists — several spec options have
no .NET equivalent, and setting a non-existent property is a compile error rather than a silent
no-op, which is the thing that forces a test to be skipped rather than translated.

**`LangVersion` is pinned to `8`.** No records, no file-scoped namespaces, no target-typed `new`,
no `init`, no top-level statements, no `using` declarations without braces in older positions.

## The HTTP mock

`Uts/Helpers/Http/MockHttpClient.cs`, matching `uts/rest/unit/helpers/mock_http.md`. Read it.

It is an `HttpMessageHandler` installed through the **public** `ClientOptions.HttpClient` seam, which
matters: it sits *below* the SDK's host selection, so by the time a request reaches the mock
`AblyHttpRequester` has already chosen the host for that attempt. That is what makes
`rest/unit/fallback.md` translatable at all.

Every call raises a `PendingHttpConnection`, then a `PendingHttpRequest` only if the connection
succeeded. A refused connection records nothing in `CapturedRequests`.

`PendingHttpConnection`: `Host`, `Port`, `Tls`, `Timestamp`; `RespondWithSuccess()`,
`RespondWithRefused()`, `RespondWithTimeout()`, `RespondWithDnsError()`.

`PendingHttpRequest`: `Method`, `Url` (`Scheme`, `Host`, `Port`, `Path`, `QueryParams`,
`AllQueryParams`), `Path`, `Headers` (case-insensitive, request **and** content headers merged),
`Body`, `BodyText`, `Timestamp`, `Message`; `RespondWith(status, body, headers)`,
`RespondWithDelay(delay, status, body, headers)`, `RespondWithTimeout()`.

`MockHttpClient` also carries `CapturedRequests`, `ConnectionAttempts`, `HandlerErrors`,
`AwaitRequest(timeout)`, `AwaitConnectionAttempt(timeout)`, `Reset()`, and the queue family
(`QueueResponse`, `QueueResponses`, `QueueTimeout`, `QueueDelayedResponse`, `QueueResponseForHost`,
`QueueResponseForUrl`). Handlers are reassignable.

A response body given as a native object or array is serialised to JSON with
`Content-Type: application/json`. Pass `byte[]` to control the encoding yourself, with an explicit
`Content-Type` — anything the SDK must decode needs one.

**Answering precedence** per attempt: a registered waiter, then a queued response, then the handler.
An attempt that *nothing* answers throws a descriptive `InvalidOperationException` after 5 seconds
rather than hanging — the message names the attempt and lists the ways to answer it.

## The WebSocket mock

`Uts/Helpers/WebSockets/MockWebSocket.cs`, matching `uts/realtime/unit/helpers/mock_websocket.md`.
Read it. Installed through the public `ClientOptions.TransportFactory` seam.

One mock serves a whole client. Each connection attempt produces a fresh `MockTransport`, so a
reconnection is observable as a second attempt on the same mock rather than needing a new one.

| Member | Is |
|---|---|
| `Events` | the unified timeline, a list of `MockEvent(Type, Timestamp, Data)` |
| `EventsOfType(MockEventType.X)` | the timeline filtered by type |
| `ConnectionAttempts` / `MessagesFromClient` | the timeline filtered to attempts and decoded client messages |
| `Connections` / `ActiveConnection` | the connections established, and the live one. `ActiveConnection` is null before the first connection and after it closes |
| `HandlerErrors` | whatever a test's handler threw, in order |
| `SendToClient(JObject)`, `SendToClientAndClose(JObject)`, `SimulateDisconnect(error)`, `SendPingFrame()` | against `ActiveConnection`; they throw if there is none |
| `AwaitConnectionAttempt`, `AwaitNextMessageFromClient`, `AwaitClientClose` | each registers its waiter **when called** and returns a task, so the next event can be awaited before the current one is answered |
| `AwaitProtocolMessages(action, count)` | the primitive: waits until `count` messages of that action have left the client, and returns them |
| `AwaitPublished(count)` / `AwaitPresenceSent(count)` | the MESSAGE and PRESENCE wrappers |
| `Reset()` | clears the timeline, waiters and transports; leaves handlers alone |

`MockEventType`: `ConnectionAttempt`, `ConnectionSuccess`, `ConnectionFailure`, `MessageFromClient`,
`MessageToClient`, `PingFrame`, `ServerDisconnect`, `ClientClose`.

`PendingWebSocketConnection`: `Url` (a `RecordedUrl`), `QueryParams`, `Protocol`, `BinaryProtocol`,
`Timestamp`, `Connection`; `RespondWithSuccess(connectedMessage)`, `RespondWithRefused()`,
`RespondWithTimeout()`, `RespondWithDnsError()`, `RespondWithError(errorMessage, thenClose)`, plus
the server-side `SendToClient` / `SendToClientAndClose` / `SimulateDisconnect` so a handler can
respond and then immediately inject.

### Protocol message templates

All in `ProtocolMessages`. Everything is a **`JObject` of wire names** rather than a
`ProtocolMessage`, for two reasons: unknown fields and unknown action numbers survive to the wire
untouched — so `SendToClient` with a literal already *is* the specs' `send_to_client_raw` and the
forwards-compatibility specs (RTF1/RSF1) need no extra mock method — and a template cannot be
mutated by one test into something the next inherits, because the builders return fresh objects.

| Name | Is |
|---|---|
| `ConnectedMessage()` | action 4, `connectionId "test-connection-id"`, `connectionDetails{connectionKey "test-connection-key", connectionStateTtl 120000, maxIdleInterval 15000}` |
| `ConnectedMessage(connectionId:, connectionKey:, clientId:, connectionStateTtl:, maxIdleInterval:)` | a CONNECTED variant |
| `ConnectedMessageNoIdle()` | the same with `maxIdleInterval: 0`, so the transport never arms its idle timer |
| `ClosedMessage()`, `HeartbeatMessage()`, `DisconnectedMessage(...)` | actions 8, 0 and 6 |
| `ErrorMessage(code, message, statusCode?)` | action 9; `statusCode` defaults per the spec formula (40142 → 401), 8xxxx → 500 |
| `ChannelErrorMessage(channel, ...)` | a channel-scoped ERROR: an attach failure that leaves the connection up |
| `AttachedMessage(channel, fields?)`, `DetachedMessage(channel, fields?)` | actions 11 and 13 |
| `ServerDetachedMessage(channel, code, message)` | a DETACHED carrying an error, for RTL13 |
| `MessageProtocolMessage(channel, messages, fields?)`, `PresenceProtocolMessage(...)`, `SyncMessage(...)` | actions 15, 14 and 16 |
| `AckMessage(msgSerial, count)`, `NackMessage(msgSerial, code, description, ...)` | actions 1 and 2, built against a captured outgoing message |
| `AuthMessage()` | action 17 |
| the action constants | `ProtocolMessages.Connected` etc., as ints, for injecting unknown actions |

## Client and state helpers

All in `UtsClients`, and surfaced on `UtsTestBase` where a test needs them most.

| Helper | Is |
|---|---|
| `RestClient(mockHttp, clock?, configure?)` | a REST client on the HTTP seam |
| `RealtimeClient(mockWs, mockHttp?, clock?, configure?)` | a realtime client on up to three seams, registered for teardown |
| `Options(...)` | the options alone, for a spec that must construct the client itself |
| `AwaitConnectionState(connection, state, timeout?)` | `AWAIT_STATE`. Registers synchronously; **returns at once if the state is already current** — see trap 1 |
| `NextConnectionState(connection, state, timeout?)` | waits for the next *fresh* entry into a state. The antidote to trap 1 |
| `AwaitChannelState` / `NextChannelState` | the channel equivalents, same hazard |
| `RecordConnectionStates(connection)` / `RecordChannelStates(channel)` | a list that accumulates every state entered. The prescribed shape for transient states |
| `Snapshot(recorded)` | a thread-safe copy of a recorder list, to assert on |
| `PollUntil(condition, description, timeout?)` | `AWAIT UNTIL`, for a premise no state captures |
| `ContainsInOrder(observed, params expected)` | `CONTAINS_IN_ORDER` |

`UtsClients.Options` sets four unit-tier defaults, each of which a spec can override through
`configure:`:

- `Key` — the specs' `"appId.keyId:keySecret"`.
- `AutoConnect = false` — realtime specs drive the connection explicitly; only the specs *about*
  auto-connect pass `AutoConnect = true`.
- `FallbackHosts = []` — otherwise one injected 5xx fans out into a request per host and every
  request count in the file is wrong.
- `SkipInternetCheck = true` — `PubSubHttpClient.CanConnectToAbly()` is reached from the realtime
  fallback path (`Realtime/AttemptsHelpers.cs:12`). Left on, it either puts a surprise request into
  `CapturedRequests` or, with no mock HTTP installed, **reaches the real internet from a unit test**.

## Timers

Two regimes. Pick by what the SDK does with the interval.

**Time the SDK *measures* → `TestClock`.** Installed through `ClientOptions.NowFunc`, which is the
single clock the SDK reads. Token expiry, the fallback-host cache in `AblyHttpRequester`
(`FallbackHostUsedFrom`) and the cumulative `HttpMaxRetryDuration` budget all go through it, so
`clock.Advance(ms)` makes those elapse instantly. `TestClockTests` proves this end to end rather
than taking it on trust.

**Time the SDK *schedules* → a short real interval through a client option.** There is no timer
seam: every connection state constructs its own `CountdownTimer` inline
(`Transport/States/Connection/*.cs`) with no injection point reachable from a client. So a spec's
`ADVANCE_TIME` over a scheduled wait becomes a shortened option, and the test says so at the site:

| Spec interval | Option |
|---|---|
| connect / attach / detach / ping deadline (TO3l11) | `RealtimeRequestTimeout` |
| the wait in DISCONNECTED before retrying | `DisconnectedRetryTimeout` |
| the wait in SUSPENDED before retrying | `SuspendedRetryTimeout` |
| the wait before re-attaching a channel | `ChannelRetryTimeout` |
| REST request deadlines | `HttpRequestTimeout`, `HttpOpenTimeout` |
| how long a fallback host stays preferred | `FallbackRetryTimeout` |
| the transport's idle detection | `ConnectedMessage(maxIdleInterval: ...)` |

All of these are `TimeSpan` and milliseconds on both sides.

Where neither reaches the elapsing — `ConnectionStateTtl`, whose default costs 120 real seconds — the
spec is recorded as a **Mock Infrastructure Limitation** rather than driven with a real wait. Do not
add a timer seam to the SDK to get around this; that is a product-code refactor of the connection
state machine, and it is explicitly out of scope for the UTS work.

**Never use a real delay to wait for an event.** `Task.Delay` as a settling mechanism is flaky under
load and slow at scale. Use `PollUntil`, an `Await*` helper, or a state recorder. The one acceptable
real timer is the safety deadline the helpers already apply.

## msgpack is compiled out

`ClientOptions.UseBinaryProtocol`'s getter returns a hard-coded `false` unless the `MSGPACK` define
is set, and the main build does not set it — `Ably.PubSub.Core.csproj` imports the MsgPack shared
project only `Condition="$(DefineConstants.Contains(MSGPACK))"`, and `Defaults.Protocol` is
`Protocol.Json`. So:

- Every request and frame is JSON, whatever a spec asks for.
- Setting `UseBinaryProtocol = true` is a silent no-op, not an error.
- A spec's `## Protocol Variants` section has only one runnable variant here.
- `uts/rest/unit/encoding/msgpack_interop.md` is not applicable to this build.

Record these as **Mock Infrastructure Limitations** (a build configuration, not an SDK defect), not
as deviations.

## The integration tier

Both integration tiers run against the real Ably sandbox. There is no mock and no seam: the client
reaches the network. See `Uts/README.md` for the reader's view; this is what a writer needs.

- **Nothing sits in front of the client.** No `install_mock`, no captured request, no way to make the
  server answer a chosen way. A spec point that can only be shown through a stubbed response belongs
  in the unit tier.
- **Every test carries `[Trait("type", "integration")]`.** The CI filters are
  `type!=integration` for the unit legs and `type=integration` for the integration legs
  (`cake-build/helpers/test-execution.cake:240,255`), so a missing trait silently puts an
  integration test into the unit run, where it will hit the network.
- **One sandbox app per run**, provisioned from the vendored `test-app-setup.json`. It needs no
  secrets. The app is shared, so every channel name, client id and device id takes a `RandomId()`
  suffix, and anything a test registers it removes in a `finally`.
- **Waits are wall-clock**, the inverse of the unit tier's rule, because the test is waiting on a
  real server over a real network. Use `UtsSandbox.WallClockPollUntil` and pass `timeout: 10s` or
  more to the state helpers — five seconds is the budget a mock-backed test needs.
- **Nothing is consistent immediately after a write.** History and presence lag a publish or an
  enter. Any count that follows a write goes through `WallClockPollUntil`; a fixed delay either
  flakes or spends the budget.
- **The tier runs as three passes, in three processes, and must keep doing so.** The repo's own
  sandbox specs, the UTS REST integration tier and the UTS realtime integration tier each pass
  alone and fail in numbers whenever two of them share a process - always with "timed out waiting
  for Connected", and measured at 52 failures when the first two were combined. The split is on the
  `tier` trait (`tier!=realtime&tier!=uts-rest`, `tier=uts-rest`, `tier=realtime`) in
  `cake-build/tasks/test.cake`. A new integration test must inherit one of the two UTS bases so it
  lands in a pass; one that carries only `type=integration` falls into the repo's pass and takes
  the contention with it. N3 in `deviations.md` has the measurements.

## The proxy tier

`uts/<tier>/integration/proxy/<name>.md` routes its traffic through
[ably/uts-proxy](https://github.com/ably/uts-proxy) on the way to the sandbox. `uts/docs/proxy.md`
governs the tier; read it in full before deriving one of these.

- **The client points at the session, not the sandbox:** `localhost`, `session.ProxyPort`,
  `Tls = false`.
- **Basic auth cannot be used through the proxy at all.** The session speaks plain HTTP and the SDK
  refuses basic auth over non-TLS (RSC18), so every client authenticates with an `AuthCallback` —
  including the `/time` ones, where it looks unnecessary.
- **The token callback's own client must go straight to the sandbox**, or it puts an extra request in
  front of the waiting rule and breaks every assertion that counts requests exactly.
- **The proxy is the second witness.** The SDK's own result answers half the question and
  `session.GetLog()` the other half: how many requests were made, in what order, and what the proxy
  answered them with.
- **The event log's field names are the proxy's, not the spec's.** Derive against the real names
  through a filter defined once at the top of the file, and record the drift in `deviations.md`.
- **Own trait**, so the tier stays out of the default legs — the proxy has to be running.
- **On Windows there is no prebuilt binary.** `ably/uts-proxy` ships linux/darwin release assets
  only; build from source with `go build -o uts-proxy.exe .`. CI runners are Linux/macOS and use the
  release binary. The harness handles both.
- **`JwtAuthCallback` returns a `TokenDetails`, not the bare JWT string RSA8d allows.** D13 means a
  string from an `authCallback` is only ever read as a `TokenRequest` here, so a bare JWT fails to
  parse. It happens to work for `/time`, which needs no token, and fails for anything that does —
  an easy way to spend an hour on the wrong thing.
- **The event log lags the wire.** Counting frames immediately after an operation can see zero and
  then mistake the operation's own frame for the one you were waiting for. Wait for the baseline to
  appear before taking the "before" count. The same applies at the end of a test: an SDK callback
  returning is not the frame it produces reaching the socket, so poll `GetLog()` for the frame you
  expect rather than reading it once. Both of this tier's intermittent failures were this.
- **A state an injected fault produces can be too brief to await.** A killed transport earns an
  immediate reconnect, so the client can be back in CONNECTING - or CONNECTED - before an awaiter
  registers, and `AwaitConnectionState(..., Disconnected)` then times out on a drop that did
  happen. Record the transitions before triggering the fault and assert on the record.
- **A `[ProxyFact]` that is also deviation-gated is `[ProxyDeviationFact]`** — both gates, so no
  proxy skips it and so does a run without `RUN_DEVIATIONS=1`.
- **Two rule shapes do not work at `uts-proxy` v0.3.0**, which is what CI pins: `"__PASSTHROUGH__"`
  in a replaced message is not substituted (the client stores the literal string), and
  `refuse_connection` matched on a `ws_connect` `count` above 1 never fires — `ruleMatched` comes
  back empty. `count: 1` does work. M3 records what that blocks.

## Traps

Ordered by how much they cost. The first two account for most of the lost time.

**1. `AwaitConnectionState` returns immediately when the state is already held**, so it silently
no-ops and the assertions after it run against stale state. It looks like it passed. Two shapes:

- *"Drop the connection and reconnect"* — the target is CONNECTED and the client is **still**
  CONNECTED when you call it. Use `NextConnectionState(connection, Connected)`.
- *"An attempt is in flight"* — `client.Connect()` moves the connection to CONNECTING synchronously,
  so waiting for CONNECTING returns before anything has happened. Poll for the **attempt**:
  `PollUntil(() => mockWs.ConnectionAttempts.Count == 1, "...")`.

The same applies to `AwaitChannelState`: inject a DETACHED and then wait for ATTACHED and you have
asserted nothing, because the channel still *is* attached at that instant. Poll for the second
ATTACH message leaving the client first.

**2. An SDK call starts its request eagerly, so a waiter must be registered first.** This is the
main ordering difference from the Python harness, where a coroutine does nothing until awaited. In
C#, `client.TimeAsync()` has already reached the mock by the time the next statement runs, so

```csharp
var pending = mockHttp.AwaitRequest();   // register FIRST
var call = client.TimeAsync();           // then provoke
var request = await pending;
```

is the correct order. Registering afterwards races the handler and the handler usually wins. An
attempt that *nothing* answered is parked for the next `AwaitRequest()`, so the await pattern still
works when there is no handler at all — but with a handler installed, the order above is the only
reliable one.

**3. The SDK closes a socket through `ITransport.Dispose()`, not `Close()`.**
`ConnectionManager.DestroyTransport()` nulls the listener and calls `Dispose()`. The mock records
`ClientClose` from both, so `AwaitClientClose()` works — but when reading SDK code, do not expect
`Close()` to be the client-initiated close path.

**4. `client.Close()` does not close the socket by itself.** It sends a CLOSE protocol message and
waits for the server's CLOSED. A test that wants the socket torn down has to answer:

```csharp
client.Close();
await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Close);
mockWs.SendToClient(ProtocolMessages.ClosedMessage());
```

**5. A realtime publish awaits its ACK (RTL6b).** `channel.PublishAsync(...)` does not complete
until the ACK arrives, and PRESENCE is the same. Most specs write the publish un-awaited and never
ACK it: read that as a promise the spec is eliding and **add the ACK**, built against the captured
`msgSerial`, unless the spec says in as many words not to — then drive the publish as a task. State
which you did in the class summary.

**6. A transient state cannot be waited for.** The retry after a drop from CONNECTED is immediate, so
DISCONNECTED is gone before the next `await` returns. Register a recorder with
`RecordConnectionStates` **before connecting** and assert on the list, as `mock_websocket.md` says.

**7. `MessageAction` tops out at `Auth = 17`.** There is no PING/PONG (22/23), no ANNOTATION (21),
no OBJECT. Injecting an unknown action number through a `JObject` is safe — Newtonsoft widens it
into the enum and the connection survives, which is exactly what the forwards-compatibility specs
assert — but there is no SDK behaviour behind it to test.

**8. Keep the fallback hosts empty unless the spec is about them.** A 5xx or a connection failure is
retried once per host, so a handler that always answers 500 is called once per host, not once.

**9. Anything the SDK must decode needs a `Content-Type`.** A native object or array body gets
`application/json` automatically; a `byte[]` body does not, and the response parsing will fail in a
way that does not name the cause.

**10. `/time` returns an array.** Several specs stub it as `{"time": N}`; the endpoint and `time.md`
both use `[N]`, and `TimeAsync()` indexes it. Stub `new object[] { N }`.

**11. StyleCop runs as a build error in Release.** `SA1025` (no runs of whitespace — so no
column-aligned `[InlineData]` values and no aligned trailing comments), `SA1028` (no trailing
whitespace) and `SX1101` (no `this.` prefix) are errors. A plain `dotnet build` in Debug will not
show them; the Cake build in Release will.

**12. A state is reached before the frame that caused it leaves.** `channel.State` is ATTACHING
before the ATTACH is handed to the transport, so a test that waits for the state and then reads a
message count reads zero. Any assertion about *what went on the wire* has to wait for the wire:
`await UtsClients.PollUntil(() => attachCount == 1, "the ATTACH")`. This bit twice in one sitting,
in two unrelated files.

**13. `SetChannelState` emits after running its handler, and the handler can recurse.**
`RealtimeChannel.SetChannelState` builds the `ChannelStateChange`, calls `HandleStateChange` — which
assigns `State` and may call `SetChannelState` again — and only then emits. So the *emitted* order
is not the state machine's order: a nested change is emitted before its parent. Do not assume
`changes[0]` is the first transition; find the change you mean. D43 is this, measured.

**14. Asserting that nothing happened needs a round trip, not a sleep.** For "no second ATTACH",
"no delivery", "no re-entry", drive one request through the workflow and then read the count:
`await channel.Presence.GetAsync(waitForSync: false)` is cheap and proves the frame in question has
been processed. A `Task.Delay` either flakes or wastes the budget.

**15. `UtsClients.PollUntil` takes a `Func<bool>`.** An `async () => ...` condition is a CS4010, not
an overload resolution failure that suggests the fix. For an async condition use
`UtsSandbox.WallClockPollUntil`, which takes `Func<Task<bool>>`.

**16. FluentAssertions' `OnlyContain` fails on an empty collection.** For "every recorded state was
CONNECTED" where the usual answer is *no states at all*, write
`.Should().NotContain(s => s != Connected)`.

**17. `LocalDevice.Instance` is static and leaks between tests.** Any client that reads `.Device`
sets it for the whole process, and `PubSubHttpClient.OnAuthClientIdChanged` then dereferences a
null instance property on an unrelated client — a `NullReferenceException` out of `AuthorizeAsync`
that only appears when a push test ran first. `UtsTestBase.Dispose` clears it; keep that. D48.

**18. `ProtocolMessage.OnSerializing` deletes "empty" messages, and mutates.** A `Message` equal to
a default-constructed one is filtered out at serialisation time, and if that leaves none the whole
`messages` array is dropped — on the live object, not a copy. Publishing `(null, null)` therefore
sends a MESSAGE with no messages and still completes successfully. D46.

**19. Failure arrives two ways from the same method.** `PublishAsync` returns a failed `Result` for
a NACK and *throws* an `AblyException` for a state refusal (`RealtimeChannel.PublishImpl`), the
latter re-wrapped by `TaskWrapper` as a 50000 "Unexpected error" with the real code inside. Check
which one the spec's `FAILS WITH error` means before writing the assertion. N4.

**20. `AttachAsync` and `DetachAsync` return `Task<Result>` and do not throw** — and a pending one
is resolved as *failed* the moment the connection goes DISCONNECTED, even though RTN19b then
resends the ATTACH and the channel attaches. Do not read the returned task as the outcome of the
operation across a reconnect. D47.

## API shapes that mislead

Separate from the timing traps above: these are places where reading the SDK gives the wrong
answer about whether something can be translated.

- **`MessageAction` resolves, but to the wrong thing.** `ProtocolMessage.MessageAction` is the
  protocol-action enum (`Heartbeat = 0 … Auth = 17`), not the spec's per-message
  `MessageAction.MESSAGE_CREATE`. They are numerically incompatible — `MESSAGE_UPDATE` = 1 collides
  with `Ack` = 1 — so a translator who sees the name resolve may wrongly conclude the API exists.
- **An unknown key in a *mock* body is harmless.** Several specs put `res: [PublishResult(...)]` in
  the ACK they inject. The templates are raw `JObject`s and the SDK ignores what it does not know,
  so that alone blocks nothing; only an assertion that *reads* `PublishResult` does. In
  `channel_publish.md` that distinction is the difference between three blocked tests and six.
- **`RealtimeChannelOptions(attachOnSubscribe: false)` has no counterpart and usually does not
  matter.** It appears throughout `channel_publish.md` and `channel_subscribe.md` purely to stop
  `subscribe()` attaching — and every one of those tests attaches explicitly first. Drop the option
  and say so; do not skip the test.
- **`MessageExtras` keeps its `Data` JToken private**, exposing only `Delta` and `ToJson()`. An
  `extras["custom"]`-style assertion has to go through `ToJson()`.
- **`Encoding.UTF8` does not resolve inside `Uts.Rest.Unit`.** The sibling `Uts/Rest/Unit/Encoding/`
  directory puts an `…Uts.Rest.Unit.Encoding` namespace in scope, which shadows `System.Text`, so
  this is a CS0234 rather than the obvious thing. Write `System.Text.Encoding.UTF8`. The same
  hazard applies to any helper named after a directory in the UTS tree.
- **Counting tests: `**Test ID**` undercounts.** Some spec files contain fully specified tests with
  no marker, and at least one marker covers two scenarios needing two `[Fact]`s. Count headings
  that have Setup/Steps/Assertions, and say which you counted.

## Deviations

Diagnose per the decision tree in `writing-derived-tests.md`, then apply one of:

- **Env-gated skip**, for non-compliance expected to be fixed. xUnit cannot skip conditionally
  through `[Fact(Skip=...)]` at runtime, so use the `[DeviationFact]` attribute in
  `Uts/Helpers/DeviationFact.cs`, which is a `[Fact]` with a dynamic skip reason unless
  `RUN_DEVIATIONS` is set. Reproduce with:
  `RUN_DEVIATIONS=1 dotnet test --filter "FullyQualifiedName~RSA7b"`.
- **Adapted assertion**, preferred where the behaviour is stable: assert what the SDK does, with the
  spec's expectation in a comment above.
- **Spec-error fail-fast**, only where the spec contradicts the features spec:
  `Assert.Fail("UTS spec error <point> — fix the spec first; see deviations.md")`.

Never write a test that passes under either behaviour.

Record every one in `Uts/deviations.md` under its heading, keeping all four headings present and in
order: **UTS Spec Errors**, **Failing Tests**, **Adapted Tests**, **Mock Infrastructure
Limitations**. Mark an empty category `*(none)*` rather than deleting its heading. Each entry needs
the spec point, what the spec says, what the SDK does, root cause where known, which tests are
affected, and status.

**A differently spelled API is not a deviation.** `TimeAsync()` for `time()`, `Connection.Id` for
`connection.id`, `PaginatedRequestParams` for a params object — that is ordinary translation and
there is nothing to record.

**A missing public accessor is adapted, never gated.** Where the behaviour is right and only the
public member is absent, assert the equivalent observable however internal it is (the test assembly
has `InternalsVisibleTo`), define a reader at the top of the file so the adaptation is in one place,
and record the missing API in its own entry. Gating would take real behavioural coverage out of the
run indefinitely over a question of spelling.

**Group by root cause, not by test or spec file.** Five tests failing for one reason are one entry
naming all five.

**Record a refuted claim too.** If something looks like a defect and turns out not to be, put it
under *Investigated and not defects* with the reasoning, so the next reader does not reach the same
first conclusion.

## Checks

Always through Cake, never bare `dotnet`. StyleCop runs as **build errors** in Release and a clean
`dotnet build` in Debug proves nothing.

```bash
./build.sh --target=Build.NetStandard                                      # lint gate
./build.sh --target=Test.NetStandard.Unit --framework=net6.0               # unit tier
./build.sh --target=Test.NetStandard.Unit --framework=net7.0
./build.sh --target=Test.NetStandard.Integration --framework=net6.0        # integration tier
./build.sh --target=Test.NetStandard.Proxy --framework=net6.0              # proxy tier
```

On Windows run `./build.cmd` with the same arguments. `build.sh` needs a POSIX shell: from
PowerShell or `cmd` it is not an executable, so Windows offers to open it with a program and then
closes the window it picked.

**Use the non-retry targets.** `Test.NetStandard.Unit.WithRetry` and friends wrap the run in
warn-only `catch` blocks with no rethrow (`cake-build/tasks/test.cake`) and **exit 0 even when tests
fail**. Every CI workflow but the proxy one uses a retry variant, so a green CI run is not evidence
that tests pass. Verify locally with the non-retry targets.

For a fast inner loop while writing a single file, `dotnet test --filter` is fine — but run the Cake
targets before claiming anything is green.

```bash
dotnet test src/Ably.PubSub.Tests.DotNET/Ably.PubSub.Tests.DotNET.csproj \
  -f net6.0 -c Release --filter "FullyQualifiedName~Uts.Rest.Unit.Time"
```

One more defect worth knowing: **the `net8.0` and `net9.0` CI legs discover zero tests**, because
`Ably.PubSub.Tests.DotNET` targets `net6.0;net7.0` only. New tests will not run there. Out of scope
to fix, but do not read those legs as coverage.
