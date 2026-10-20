# Realtime module: spec → ably-pubsub-dotnet translation notes

Read this **before translating any spec from `uts/realtime`**. The realtime specs are written in a language-agnostic pseudocode that mirrors the **ably-js** API; this SDK (ably-pubsub-dotnet) differs in naming (PascalCase, `…Async`), failure model (`Result`, exceptions) and options (`Endpoint`), so almost every spec line needs a mechanical rewrite, not a literal transcription. This doc is that rewrite table. **Everything REST-shaped comes from `rest-mapping.md`: read both.** That covers client options shared with REST, `ErrorInfo`/`AblyException`, message and token types, `PaginatedResult`, and the REST-backed realtime methods (`history`, `getMessage`, `getMessageVersions`, `annotations.get`, `stats`, `time`, `request`), which run through the realtime client's REST client and throw like REST. The spec's own Interface Definition (the IDL section of `specifications/features.md` in the spec clone: `class RealtimeClient`, `class Connection`, `class RealtimeChannel`, `class RealtimePresence`, …) is the canonical bridge; no typed-SDK variant applies. The SDK source (`src/Ably.PubSub.Shared/`) is the ground truth: where this doc, the IDL and the source disagree about the SDK's API, the source wins, so fix this doc.

Symbols are cited by file and member, never by line. "Unit base" is `UtsUnitTestBase`, "integration base" `UtsIntegrationTestBase`, "proxy base" `UtsProxyTestBase`; the infra README is `src/Ably.PubSub.Tests.Shared/Uts/Infra/README.md`.

SDK facts: the WebSocket URL carries `v=6`, `format=json` and `heartbeats=true` (always; `TransportParams`), plus the `transportParams` entries.

> ✅ **Runtime status.** The realtime SDK surface exists (connection, channels, presence, annotations, delta decoding) and the mapping entry `realtime` has no readiness markers, so `realtime` specs are translate **and** evaluate at the unit, integration and proxy tiers (SKILL.md Steps 5 and 6). Evaluate mode is the normal path.

**Consolidations (apply everywhere):**

1. **`Result` versus throw.** Attach, detach, `setOptions`, publish, update/delete/append, presence enter/update/leave (and the `…Client` variants), annotations publish/delete and connection `ping` return `Result` / `Result<T>` and normally don't throw: assert `IsSuccess` / `Error`. Exceptions: `PublishAsync` throws when its precondition check fails (`EnsureCanPublish`; wrapped as 50000 on the `TaskWrapper` path), and `channel.Presence.GetAsync`, presence `HistoryAsync`, `Auth.AuthorizeAsync` and every REST-backed method throw `AblyException` (§4).
2. **JSON only.** Msgpack inputs are not applicable (§12).
3. **LiveObjects is out of scope.** `channel.object` and the `objects` module are unready in the mapping; `OBJECT` / `OBJECT_SYNC` are wire constants only.

## Table of contents

1. [The layers](#1-the-layers)
2. [Entry point & client construction](#2-entry-point)
3. [Realtime & channel options](#3-options)
4. [Async: connect/close, state waits, Result ops, exceptions, listeners, fake time](#4-async)
5. [States, events & state-change objects](#5-states)
6. [Connection operations](#6-connection-operations)
7. [Channel operations & subscriptions](#7-channel-operations)
8. [Presence](#8-presence)
9. [Annotations](#9-annotations)
10. [Wire protocol in mocks](#10-wire-protocol)
11. [Errors](#11-errors)
12. [Not applicable, missing APIs, REST-only extras & delegating specs](#12-not-applicable)
13. [Known SDK behaviour differences](#13-known-differences)
14. [White-box specs & shape-deviation vocabulary S-1…S-4](#14-white-box)
15. [Harness & helper-spec coverage](#15-harness)
16. [Overrides of the generic flow](#16-overrides)
17. [Integration & proxy helpers](#17-integration)
18. [Worked example](#18-worked-example)
19. [Symbol index](#19-symbol-index)

---

## 1. The layers <a id="1-the-layers"></a>

| Spec name | This SDK | Note |
|---|---|---|
| `Realtime` / `RealtimeClient` | `PubSubRealtimeClient` (internal constructors; public type) | Built only by the base factories (§2) |
| `client.rest` (implicit in REST-backed calls) | `client.HttpClient` (public `PubSubHttpClient`) | The realtime client's own REST client; `rest-mapping.md` applies to it |
| `Connection` | `Ably.PubSub.Realtime.Connection` (public sealed; `EventEmitter<ConnectionEvent, ConnectionStateChange>`) | |
| `RealtimeChannel` | `IRealtimeChannel` (what `Channels.Get` returns; public). The class `RealtimeChannel` is **internal** | Public-API specs use `IRealtimeChannel` only; the internal class is reachable only for listed white-box specs |
| `RealtimePresence` | `Ably.PubSub.Realtime.Presence` (public sealed, `channel.Presence`) | |
| `PresenceMap` (RTP2) / `LocalPresenceMap` (RTP17) | internal `PresenceMap` / internal `InternalPresenceMap` (keyed by `clientId`) | White-box (§14) |
| `RealtimeAnnotations` | `RealtimeAnnotations` (`channel.Annotations`) | `get` delegates to REST |
| `ConnectionState` vs `ConnectionEvent` | two enums with the same values; `ConnectionEvent` adds `Update` | `On(event, …)` takes `ConnectionEvent` |
| `ChannelState` vs `ChannelEvent` | two enums with the same values; `ChannelEvent` adds `Update` | `On(event, …)` takes `ChannelEvent` |
| `ProtocolMessage` (wire) | `Ably.PubSub.Types.ProtocolMessage` with nested `ProtocolMessage.MessageAction` (0–23) and `ProtocolMessage.Flag` | The message-level `Ably.PubSub.MessageAction` (`MessageCreate` …) is a different enum |

## 2. Entry point & client construction <a id="2-entry-point"></a>

`Realtime(options: ClientOptions(...))` renders as the tier base's factory:

| Tier | Rendering | Notes |
|---|---|---|
| unit | `var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });` | `UtsUnitTestBase.Realtime`: the internal `new PubSubRealtimeClient(options, createRestFunc, mobileDevice)`, tracked for teardown |
| integration | `Realtime(options)` on the integration base | sandbox `Endpoint`, the app's first key unless the test set auth |
| proxy | `ProxyRealtime(session, options)` | localhost endpoint, session port, `Tls = false`, a sandbox token callback unless the test set token auth |

The factories change the options in place and wire the installed doubles. Install every mock **before** the factory call: `webSocketClient: mock_ws`, `httpClient: mock_http`, `plugins: { vcdiff: decoder }` and `install_mock(…)` all become `InstallMock(…)` first (a mock applies to clients built afterwards). **Translate `autoConnect: false` exactly**: the factories don't default it. With no `MockWebSocket` installed, the unit base refuses every attempt (`DefaultWebSocket`); with no `MockHttpClient`, a strict default fails the test on any unexpected request (README "Unit-tier defaults"). Realtime specs that build a `Rest` client use the base's `Rest(options)` (`rest-mapping.md`).

## 3. Realtime & channel options <a id="3-options"></a>

REST options and the log-level mapping are in `rest-mapping.md` §3; every duration is a `TimeSpan`.

| Spec option | C# | Notes |
|---|---|---|
| `autoConnect` | `AutoConnect` | `bool`, default true |
| `echoMessages`, `queueMessages` | `EchoMessages`, `QueueMessages` | `bool`, default true |
| `recover` | `Recover` | `string` recovery key |
| `transportParams: { … }` | `TransportParams = new Dictionary<string, object> { ["k"] = v }` | values stringified on the URL |
| `disconnectedRetryTimeout`, `suspendedRetryTimeout`, `channelRetryTimeout` | same names | `TimeSpan` (15 s, 30 s, 15 s) |
| `realtimeRequestTimeout` | `RealtimeRequestTimeout` | `TimeSpan` (10 s); the setter throws `ArgumentOutOfRangeException` below 1 ms or above `int.MaxValue` ms |
| `httpOpenTimeout`, `httpRequestTimeout` | same names | `TimeSpan` |
| `fallbackHosts`, `endpoint`, `port`, `tls` | as REST | `realtimeHost` is gone (§12) |
| `plugins: { vcdiff: decoder }` | `InstallMock(decoder)` (hook H2, `ClientOptions.DeltaDecoder`) | Sanctioned stand-in: the SDK has no plugin option and a built-in decoder |
| `clientId` | `ClientId` | |
| `connectionStateTtl`, `maxIdleInterval` | server-sent in `CONNECTED.connectionDetails` (wire ms) | read back as `client.Connection.ConnectionStateTtl` (`TimeSpan`) |

**Channel options.** `client.channels.get(name, ChannelOptions(params:, modes:, cipher:))` → `client.Channels.Get(name, new ChannelOptions(encrypted: …, @params: cipherParams, modes: new ChannelModes(ChannelMode.Subscribe), channelParams: new ChannelParams { ["delta"] = "vcdiff" }))` (`rest-mapping.md` §3 for the parameter names). `Get` with options on an existing channel throws `AblyException` (40000) when the options would force a reattach (RTS3c), else applies them. `RealtimeChannelOptions(attachOnSubscribe: …)` has no equivalent (§12).

## 4. Async: connect/close, state waits, Result ops, exceptions, listeners, fake time <a id="4-async"></a>

- **`client.connect()` / `client.close()` / `connection.connect()` / `connection.close()`** → `client.Connect()` / `client.Close()` / `client.Connection.Connect()` / `Close()`: `void`, they queue a workflow command. Follow with a state wait: `AWAIT_STATE client.connection.state == ConnectionState.connected` (both spellings, and bare `CONNECTED`) → `await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected)`; channels → `await UtsWait.AwaitChannelState(channel, ChannelState.Attached)`. Both subscribe first, then check, and fail fast on FAILED (README "Waits, time and assertions").
- **Channel and presence operations return `Task<Result>`** (or `Task<Result<T>>`) and normally **don't throw**: `AttachAsync`, `DetachAsync`, `SetOptionsAsync`, `PublishAsync` (`Result<PublishResult>`), `UpdateMessageAsync`/`DeleteMessageAsync`/`AppendMessageAsync` (`Result<UpdateDeleteResult>`), presence `EnterAsync`/`UpdateAsync`/`LeaveAsync` and the `…ClientAsync` forms, annotations `PublishAsync`/`DeleteAsync`, `Connection.PingAsync` (`Result<TimeSpan?>`).
  - `AWAIT channel.attach()` (expected to succeed) → `var attachResult = await channel.AttachAsync(); attachResult.IsSuccess.Should().BeTrue(…);` (unit) or `AssertSucceeded(attachResult, "attach")` (integration base): an `AWAIT` that fails must fail the test.
  - **Exceptions on the `Result` path:** `PublishAsync` throws when the precondition check fails (`RealtimeChannel.EnsureCanPublish`: channel SUSPENDED or FAILED, or the connection can't queue because `QueueMessages` is false and it isn't CONNECTED). The `TaskWrapper` path wraps that in `new AblyException(ex)`: the thrown `ErrorInfo` is code 50000, message `"Unexpected error :…"`, with the original `AblyException` as `InnerException`. Render those `FAILS WITH` cases with the throwing form (`(await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo`, see SKILL.md *Assertions (FluentAssertions)*) and assert the outer `ErrorInfo` as the spec states it; a mismatch is a deviation candidate (§13), not a reason to read the inner one. A disposed client throws `ObjectDisposedException` from `Connect`/`Close`/`PingAsync`. A publish that gets no ACK/NACK within `RealtimeRequestTimeout` resolves (on the fake clock) to a failed `Result` whose `ErrorInfo` has code 0 and the message `"PublishAsync timeout expired. Message was not confirmed by the server"`.
- **Throwing realtime APIs:** `client.Auth.AuthorizeAsync` (RTC8), `channel.Presence.GetAsync` (RTP11: FAILED/DETACHED channel → 90001-range error; SUSPENDED with `waitForSync` → 91005), presence `HistoryAsync`, and every REST-backed method.
- **Callbacks** (`Connection.On`, `channel.On`, `Subscribe`, presence/annotation subscribe, `authCallback`) run on the workflow thread or a timer/HTTP continuation, concurrently with the test body (README "Threading model"). Capture into `CaptureList<T>` or `UtsRecord.*`; never block inside a callback. A `MockWebSocket` handler runs synchronously inside the SDK's `Connect`/`Send` and must not block or await.
- **Listener overloads:** `IEventEmitter` has both `Action<T>` and `Func<T, Task>` overloads. A statement or `void` expression lambda binds the `Action`; `async change => …` binds the `Func`. Use non-async lambdas unless the spec's handler awaits.
- **Unit-tier time:** `EnableFakeTimers()` before building the client wherever the spec allows (a timer already running stays on the real clock), then `await AdvanceTime(ms)`; `process_pending_events()` → `await ProcessPendingEvents()`. Keep `LOOP … ADVANCE_TIME … BREAK` as a loop.

A failed `AWAIT … FAILS WITH` on a `Result` operation:

```text
AWAIT channel.attach() FAILS WITH error
ASSERT error.code == 90001
```

```csharp
var result = await channel.AttachAsync();
result.IsFailure.Should().BeTrue();
result.Error.Code.Should().Be(90001);
```

## 5. States, events & state-change objects <a id="5-states"></a>

| Spec | C# | Notes |
|---|---|---|
| `ConnectionState.connected` / `CONNECTED` | `ConnectionState.Connected` | `Initialized`, `Connecting`, `Connected`, `Disconnected`, `Suspended`, `Closing`, `Closed`, `Failed` |
| `ChannelState.attached` | `ChannelState.Attached` | `Initialized`, `Attaching`, `Attached`, `Detaching`, `Detached`, `Suspended`, `Failed` |
| listener filters | `connection.on(ConnectionEvent.update, …)` → `client.Connection.On(ConnectionEvent.Update, …)` | a spec that filters by a state passes the `ConnectionEvent` / `ChannelEvent` of the same name |
| `ConnectionStateChange` `current`/`previous`/`event`/`reason`/`retryIn` | `Current`/`Previous`/`Event`/`Reason`/`RetryIn` | `RetryIn` is `TimeSpan?`: spec ms → `RetryIn.Value.TotalMilliseconds` |
| `ChannelStateChange` `current`/`previous`/`event`/`resumed` | `Current`/`Previous`/`Event`/`Resumed` | **`reason` → `Error`** |
| connection attributes `state`, `id`, `key`, `errorReason` | `State`, `Id`, `Key`, `ErrorReason` | `connectionStateTtl` → `ConnectionStateTtl` (`TimeSpan`); `createRecoveryKey()` → `CreateRecoveryKey()` (`string`; §13 on null) |
| channel attributes `name`, `state`, `errorReason` | `Name`, `State`, `ErrorReason` | `properties.attachSerial`/`channelSerial` → `Properties.AttachSerial`/`ChannelSerial`; `params` → `Params` (`ReadOnlyChannelParams`), `modes` → `Modes` (`ReadOnlyChannelModes` of `ChannelMode`); `options.params[...]` → `Options.Params[...]`, `options.modes CONTAINS ChannelMode.subscribe` → `Options.Modes.Should().Contain(ChannelMode.Subscribe)`, `options.cipherParams` → `Options.CipherParams` |
| presence results | `channel.presence.get(…)` returns `IEnumerable<PresenceMessage>` (not a `PaginatedResult`) | `members.length` → `members.Count()`; `PresenceAction` (`Absent`, `Present`, `Enter`, `Leave`, `Update`); `presence.syncComplete` → `Presence.SyncComplete` |
| message data and extras, tokens, time | as `rest-mapping.md` §5 | |

## 6. Connection operations <a id="6-connection-operations"></a>

Every row of §6 to §9 was read in the SDK source.

| Pseudocode | C# | Result / failure |
|---|---|---|
| `client.connect()`, `client.close()` | `client.Connect()`, `client.Close()` | `void` |
| `CLOSE_CLIENT(client)` | `await CloseClient(client)` | infra; also runs at teardown |
| `client.connection.ping()` | `await client.Connection.PingAsync()` | `Result<TimeSpan?>`; failures (RTN13b/c) are `Result` errors; see README Known gaps `connection.ping()` for the timeout continuation |
| `client.connection.on(cb)` / `.on(event, cb)` / `.once(…)` / `.off(…)` | `client.Connection.On(change => …)` / `On(ConnectionEvent.X, …)` / `Once(…)` / `Off(…)` | |
| `client.connection.whenState(state, cb)` | none | §12 (RTN26) |
| `client.connection.createRecoveryKey()` | `client.Connection.CreateRecoveryKey()` | `string` |
| `client.auth.authorize(tokenParams?, authOptions?)` | `await client.Auth.AuthorizeAsync(tokenParams, authOptions)` | `TokenDetails`; throws |
| `client.auth.clientId`, `client.clientId` | `client.Auth.ClientId`, `client.ClientId` | |
| `client.time()`, `client.stats(…)` | `await client.TimeAsync()`, `await client.StatsAsync(…)` | REST semantics |
| `client.request(…)` | `await client.HttpClient.Request(…)` | §12 (RTC9) |
| `client.push`, `client.push.admin`, `client.device` | `client.Push` (`PushRealtime`), `client.Push.Admin`, `client.Device` | |
| record-and-verify `state_changes = []` + `on(...)` | `var states = UtsRecord.ConnectionStates(client.Connection)` (or `ConnectionStateChanges`, `ChannelStates`, `ChannelStateChanges`) | subscribe before the stimulus; assert with `UtsAssert.ContainsInOrder` |

## 7. Channel operations & subscriptions <a id="7-channel-operations"></a>

| Pseudocode | C# | Result / failure |
|---|---|---|
| `client.channels.get(name[, options])`, `.exists(name)`, `.release(name)` | `client.Channels.Get(name[, options])`, `Exists(name)`, `Release(name)` | `Release` returns `bool`; an attached channel is detached first, then removed |
| `client.channels.getDerived(…)` | none | §12 (RTS5) |
| `channel.attach()`, `channel.detach()` | `await channel.AttachAsync()`, `await channel.DetachAsync()` | `Result` |
| `channel.setOptions(options)` | `await channel.SetOptionsAsync(options)` | `Result` |
| `channel.on(cb)` / `.on(ChannelEvent.update, cb)` / `.once(…)` | `channel.On(change => …)` / `On(ChannelEvent.Update, …)` / `Once(…)` | |
| `channel.whenState(state, cb)` | none | §12 (RTL25) |
| `channel.subscribe(cb)` / `.subscribe(name, cb)` | `channel.Subscribe(msg => …)` / `Subscribe(name, msg => …)` | `void`; attaches implicitly unless ATTACHED or ATTACHING; the attach result isn't returned (attach failures show on the channel state) |
| `channel.subscribe(filter, cb)` (`MessageFilter`) | none | §12 (RTL22) |
| `channel.unsubscribe(listener)` / `(name, listener)` / `()` | `channel.Unsubscribe(listener)` / `Unsubscribe(name, listener)` / `Unsubscribe()` | keep the listener in a variable to unsubscribe it |
| `channel.publish(name:, data:)` / `(message:)` / `(messages:)` | `await channel.PublishAsync(name, data[, clientId])` / `PublishAsync(message)` / `PublishAsync(messages)` | `Result<PublishResult>` (`Value.Serials`); may throw (§4) |
| `channel.updateMessage(…)`, `deleteMessage`, `appendMessage` | `await channel.UpdateMessageAsync(message, operation, parameters)` etc. | `Result<UpdateDeleteResult>` |
| `channel.history(params)` | `await channel.HistoryAsync(PaginatedRequestParams)` | REST; `untilAttach` is not a public option (§12: internal `AddUntilAttachParameter`) |
| `channel.getMessage(…)`, `getMessageVersions(…)` | `GetMessageAsync`, `GetMessageVersionsAsync` | REST |

## 8. Presence <a id="8-presence"></a>

| Pseudocode | C# | Result / failure |
|---|---|---|
| `channel.presence.enter(data?)`, `update(data?)`, `leave(data?)` | `await channel.Presence.EnterAsync(data)`, `UpdateAsync`, `LeaveAsync` | `Result`; an invalid connection or channel state is a `Result` error with code 91001 |
| `presence.enterClient(clientId, data?)`, `updateClient`, `leaveClient` | `EnterClientAsync(clientId, data)`, `UpdateClientAsync`, `LeaveClientAsync` | `Result` |
| `channel.presence.get(waitForSync:, clientId:, connectionId:)` | `await channel.Presence.GetAsync(new Presence.GetParams { WaitForSync = false, ClientId = …, ConnectionId = … })` (or `GetAsync(clientId, connectionId, waitForSync)`) | `IEnumerable<PresenceMessage>`; throws; implicitly attaches an INITIALIZED channel |
| `channel.presence.history(params)` | `await channel.Presence.HistoryAsync(PaginatedRequestParams[, untilAttach])` | throws |
| `channel.presence.subscribe(cb)` / `(action: ENTER, cb)` | `channel.Presence.Subscribe(m => …)` / `Subscribe(PresenceAction.Enter, m => …)` | `void`; implicit attach as `channel.Subscribe` |
| `channel.presence.unsubscribe(…)` | `Unsubscribe(listener)` / `Unsubscribe(PresenceAction, listener)` / `Unsubscribe()` | |

## 9. Annotations <a id="9-annotations"></a>

| Pseudocode | C# | Result / failure |
|---|---|---|
| `channel.annotations.publish(serialOrMessage, annotation)`, `.delete(…)` | `await channel.Annotations.PublishAsync(serial, annotation)`, `DeleteAsync(…)` | `Result` |
| `channel.annotations.subscribe(cb)` / `(type:, cb)`, `.unsubscribe(…)` | `channel.Annotations.Subscribe(a => …)` / `Subscribe(type, a => …)`, `Unsubscribe(…)` (`bool`) | implicit attach; logs a warning (RTAN4e) when ANNOTATION_SUBSCRIBE wasn't granted |
| `channel.annotations.get(…)` | `GetAsync(serial, AnnotationsRequestParams)` | REST |

## 10. Wire protocol in mocks <a id="10-wire-protocol"></a>

| Spec | C# | Notes |
|---|---|---|
| Wire messages in mock handlers (`onMessageFromClient: (msg) => …`, `mock_ws.events[i].message`) | a decoded `ProtocolMessage` | `msg.action == ATTACH` → `msg.Action == ProtocolMessage.MessageAction.Attach` (or compare `(int)msg.Action` with `ProtocolMessages.Attach`) |
| `msg.channel`, `msg.msgSerial`, `msg.id`, `msg.messages`, `msg.presence`, `msg.params`, `msg.flags` | `Channel`, `MsgSerial` (`long`), `Id`, `Messages`, `Presence`, `Params`, `Flags` (`int?`) | flag tests → `msg.HasFlag(ProtocolMessage.Flag.AttachResume)` |
| Raw frames | `OnTextDataFrame` gives the JSON text | `JObject.Parse` |
| Server-to-client messages (`ProtocolMessage(action: X, …)` inline, `CONNECTED_MESSAGE`, `ATTACHED`, …) | `JObject` literals or the `ProtocolMessages` templates | wire names and wire units (ms) (README "Templates") |
| `ERROR_MESSAGE(code, message)` | `ProtocolMessages.ErrorMessage(code, message, statusCode?)` | **trap:** it defaults `statusCode` to `code / 100`; a spec that needs another status passes it explicitly |
| Error and detach templates | `ChannelErrorMessage(channel, …)`, `ServerDetachedMessage(channel, code, message)`, `NackMessage(msgSerial, code, message)`, `DisconnectedMessage(code, message)`, and `ErrorObject` for an `error` field | `SimulateDisconnect(ErrorInfo error = null)` takes the SDK `ErrorInfo` |

Decoded values are `DateTimeOffset` / `TimeSpan`; templates and frames use ms.

## 11. Errors <a id="11-errors"></a>

Fields and constructors: `rest-mapping.md` §7 (`Code`, `StatusCode` as `HttpStatusCode?`, `Message`, `Href`, `Cause`; never `AblyException.Message`).

| Spec failure | SDK |
|---|---|
| Where errors appear | `client.Connection.ErrorReason`, `ConnectionStateChange.Reason`, `channel.ErrorReason`, `ChannelStateChange.Error`, `Result.Error`, `AblyException.ErrorInfo` |
| Nested causes | `connection.errorReason.cause.code` → `client.Connection.ErrorReason.Cause.Code`. Auth failures while connecting carry RSA4c1's 80019 with the callback's `ErrorInfo` as `Cause` (`AblyAuth.RequestTokenAsync`) |
| Error codes | assert literals (`80002`, `91005`, `90001`), not the internal `ErrorCodes` constants |

## 12. Not applicable, missing APIs, REST-only extras & delegating specs <a id="12-not-applicable"></a>

How each row is rendered:

- **Not applicable** (the input cannot exist in this SDK): omit the whole test and leave one marker alone on its line, in the standard form `// UTS-NA: <full Test ID> — not applicable: <reason>`. These are never deviations or coverage gaps. For a row or part of a test, keep the spec line as a comment under a `// NOTE:` instead.
- **Missing API, whole test depends on it:** a missing-API stub as in SKILL.md *Test patterns for a diagnosed failure* (skip reason `Missing SDK public API: <Type#member or option> (<spec point>); see deviations.md (Missing SDK Public API / Features)`), recorded under *Missing SDK Public API / Features* in `deviations.md` in evaluate mode and listed in the run summary in translate-only mode. A missing *optional* feature is not applicable (`// UTS-NA:`) instead.
- **Missing API, only part of a test needs it:** comment out just those lines under the exact skip-reason text (`// Missing SDK public API: <Type#member or option> (<spec point>); see deviations.md (Missing SDK Public API / Features)`) and keep the rest running; stub the whole test only if nothing meaningful would remain.
- **Adapt first** (SKILL.md *First, try to adapt the test*): when another public mechanism, or at the unit tier an existing internal member, reaches the behaviour, the test runs through it, marked `// DEVIATION: <Type#member> absent; using <equivalent>` and recorded under *Adapted Tests* noting the absent public API. Adapted Tests always takes precedence; a missing-API stub or partial test is only for when adapting fails.

### Not applicable

```csharp
// realtime/unit/channels/channel_publish.md (translate realtime/unit/RTL6i3/null-fields-json-0)
// UTS-NA: realtime/unit/RTL6i3/null-fields-msgpack-1 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)

// realtime/unit/connection: `# RTN23b Tests (WebSocket Ping Frames)` applies "to platforms where the WebSocket client CAN surface ping frame events";
// ClientWebSocket can't, and the SDK always sends heartbeats=true (RTN23a path)
// UTS-NA: realtime/unit/RTN23b/heartbeats-false-query-param-0 — not applicable: the platform WebSocket can't surface ping frames
// UTS-NA: realtime/unit/RTN23b/idle-timeout-reconnect-1 — not applicable: the platform WebSocket can't surface ping frames
// UTS-NA: realtime/unit/RTN23b/ping-frame-resets-timer-2 — not applicable: the platform WebSocket can't surface ping frames
// UTS-NA: realtime/unit/RTN23b/any-message-resets-timer-3 — not applicable: the platform WebSocket can't surface ping frames
// UTS-NA: realtime/unit/RTN23b/timeout-triggers-reconnect-4 — not applicable: the platform WebSocket can't surface ping frames
// UTS-NA: realtime/unit/RTN23b/reconnect-uses-resume-5 — not applicable: the platform WebSocket can't surface ping frames
// UTS-NA: realtime/unit/RTN23b/multiple-pings-keep-alive-6 — not applicable: the platform WebSocket can't surface ping frames

// PC3 needs a client without a vcdiff decoder; the decoder is built in and cannot be configured away (default)
// UTS-NA: realtime/unit/PC3/no-plugin-fails-1 — not applicable: no plugin mechanism in this SDK
// UTS-NA: realtime/integration/PC3/no-plugin-causes-failed-2 — not applicable: no plugin mechanism in this SDK
```

Parts of tests (translate the rest):

| Spec | Test ID or section | What is omitted |
|---|---|---|
| `realtime/unit/client/realtime_client.md` | section `#### useBinaryProtocol in Realtime` (no Test ID) | the `use_binary = true` iteration (`format == "msgpack"`); keep the JSON iteration, with a `// NOTE:` |
| integration specs with `## Protocol Variants` (`delta_decoding_test`, `presence_lifecycle_test`, `channel_history_test`, `mutable_messages_test`, `channels/channel_publish_test`) | | the msgpack variant: one JSON test, tag and name singular, with SKILL.md's `// NOTE: msgpack variant not applicable: …` line |

### Deprecated options

`realtimeHost` (TO3k3, optional and deprecated; removed in favour of `Endpoint`). In `realtime/unit/RTN17g/empty-fallback-set-error-0` it is incidental (a custom host meaning "no fallbacks"): render `Endpoint = "custom.example.com"` (REC1b2: a hostname endpoint has an empty fallback set) with a `// NOTE:` naming the substitution. Tests about the deprecated options themselves are in `rest-mapping.md` §8.

### Missing APIs

| Missing | Spec point | Tests | Rendering |
|---|---|---|---|
| `Connection#whenState` | RTN26 | all of `realtime/unit/connection/when_state_test.md` | missing-API stub per test |
| `RealtimeChannel#whenState` | RTL25 | all of `realtime/unit/channels/channel_when_state_test.md` | missing-API stub per test |
| `Channels#getDerived` | RTS5 | `realtime/unit/RTS5a/creates-derived-channel-0`, `RTS5a1/filter-base64-encoded-0`, `RTS5a2/derived-with-params-0`, `RTS5/get-derived-with-options-0` | missing-API stub per test |
| `attachOnSubscribe` channel option (`RealtimeChannelOptions`) | TB4, RTL7h | `realtime/unit/TB4/attach-on-subscribe-default-0`, `realtime/unit/RTL7h/no-attach-on-subscribe-0`, and the `attachOnSubscribe` assertions in `channel_options.md` | missing-API stub for the two tests; the assertions commented out under the skip-reason comment; see the setup rule below |
| `subscribe(MessageFilter, …)` | RTL22 | `realtime/unit/RTL22a/filter-matching-ref-timeserial-1`, `…-clientid-2`, `RTL22b/filter-isref-false-0`, `RTL22c/filter-multiple-criteria-0` | missing-API stub per test. Not `RTL22a/filter-matching-name-0`: a name-only filter is reached by the public `channel.Subscribe(name, listener)`, so that test runs through it, marked `// DEVIATION: RealtimeChannel#subscribe(MessageFilter) absent; using channel.Subscribe("target-event", listener)`, and is an Adapted Test |
| `history(untilAttach:)` on a channel | RTL10b | `realtime/unit/RTL10b/adds-from-serial-0`, `realtime/unit/RTL10b/errors-when-not-attached-1` (presence history has it; the channel's `HistoryAsync` doesn't) | adapted at the unit tier: call the internal `((RealtimeChannel)channel).AddUntilAttachParameter(query)` (adds `fromSerial`, throws when not ATTACHED) then `HistoryAsync(query)`, marked `// DEVIATION: RealtimeChannel#history(untilAttach) absent; using internal RealtimeChannel.AddUntilAttachParameter + HistoryAsync(query)`; Adapted Test noting the public option is absent |
| `RealtimeClient#request` | RTC9 | not missing (default): idiomatic. The public `client.HttpClient` is the realtime client's REST client, and RTC9 asks for the wrapper "delivered in an idiomatic way" | translate through `client.HttpClient.Request(…)` |

**`attachOnSubscribe` as setup.** Where `RealtimeChannelOptions(attachOnSubscribe: false)` is only setup (55 uses: the channel is attached before any `subscribe`), drop it with a `// NOTE: attachOnSubscribe (TB4) is not in this SDK; Subscribe attaches only when the channel is not ATTACHED or ATTACHING, and this test attaches first, so the outcome is unchanged`. Where the test subscribes before attaching and relies on the option, the test is a missing-API stub. When a missing member is needed by only part of a broader test, comment out just those lines under the skip-reason comment and keep the rest of the test running.

### REST-only extras filed under `uts/realtime`

Translate these in this module's tree; the REST rows apply: `realtime/integration/proxy/rest_faults.md` (REST faults through the proxy: `ProxyRest`), the RSA4e test in `realtime/unit/auth/auth_callback_errors_test.md` (`realtime/unit/RSA4e/rest-callback-error-40170-0`, a `Rest` client), and the REST clients used for setup in `realtime/integration/auth/token_request_test.md` and `realtime/integration/proxy/connection_resume.md`.

### Delegating specs

Seven, at the pinned SHA; each says "the tests in `uts/rest/unit/…` should be used to verify … on a `RealtimeChannel`/`RealtimeClient`":

| Delegating Test ID (or section) | Delegated REST spec | Realtime operation | Inherited base (realtime class) |
|---|---|---|---|
| `realtime/unit/RTC6/time-proxies-rest-0` (`client/realtime_time.md`) | `uts/rest/unit/time.md` | `client.TimeAsync()` | `TimeTestsBase<PubSubRealtimeClient>` (`RealtimeTimeTests`) |
| `realtime/unit/RTC9/request-proxies-rest-0` (`client/realtime_request.md`) | `uts/rest/unit/request.md` | `client.HttpClient.Request(…)` (no `Request` on the realtime client; see missing APIs) | `RequestTestsBase` (`RealtimeRequestTests`) |
| `realtime/unit/RTC5/stats-proxies-rest-0` (`client/realtime_stats.md`) | `uts/rest/unit/stats.md` | `client.StatsAsync(…)` | `StatsTestsBase` (`RealtimeStatsTests`) |
| `realtime/unit/RTL28/identical-to-rest-0` (`channels/channel_get_message.md`) | `uts/rest/unit/channel/get_message.md` | `channel.GetMessageAsync(…)` | `GetMessageTestsBase<PubSubRealtimeClient, IRealtimeChannel>` (`ChannelGetMessageTests`) |
| `realtime/unit/RTL31/identical-to-rest-0` (`channels/channel_message_versions.md`) | `uts/rest/unit/channel/message_versions.md` | `channel.GetMessageVersionsAsync(…)` | `MessageVersionsTestsBase<PubSubRealtimeClient, IRealtimeChannel>` (`ChannelMessageVersionsTests`) |
| `realtime/unit/RTL10a/supports-rest-params-0` (`channels/channel_history.md`) | `uts/rest/unit/channel/history.md` | `channel.HistoryAsync(…)` | `HistoryTestsBase<PubSubRealtimeClient, IRealtimeChannel>` (`ChannelHistoryTests`) |
| section `## RTAN3a — get is identical to RestAnnotations#get` (`channels/channel_annotations.md`, no Test ID) | `uts/rest/unit/channel/annotations.md` | `channel.Annotations.GetAsync(…)` | none (no Test ID) |

Rule: the realtime class derives the REST suite's client-agnostic base (`rest-mapping.md` §12: `TimeTestsBase<PubSubRealtimeClient>`, `StatsTestsBase`, `RequestTestsBase`, `HistoryTestsBase<PubSubRealtimeClient, IRealtimeChannel>`, `GetMessageTestsBase<…>`, `MessageVersionsTestsBase<…>`), so every REST test runs again against the realtime client. It overrides only the hooks: `NewClient` sets `AutoConnect = false` (only the HTTP mock is exercised) and returns `Realtime(options)` (`Realtime(options).HttpClient` for RTC9), and the channel hooks use `client.Channels.Get(name)` and the `IRealtimeChannel` operation. The delegating spec's own `// UTS: realtime/unit/…` tag sits above those overrides, followed by a `// NOTE:` naming the base and the REST spec; the delegating Test IDs have no pseudo code, so the audit reports full coverage with no shortfall. Other tests of the same realtime spec (e.g. RTL10b in `channel_history.md`) stay in the class as usual. Inherited deviations are the REST suite's: their `deviations.md` entries name the realtime class as inheriting them. RTAN3a has no Test ID: no invented tag, a `// NOTE:` naming the heading.

## 13. Known SDK behaviour differences <a id="13-known-differences"></a>

SDK behaviour a translator meets, read from the source. These are non-binding: translate faithfully, and let evaluate mode decide; record a deviation only if the run shows it, with the features-spec quote and code path. The README section "SDK behaviour a translator meets" holds the verified ones.

> **RTN27h race, FAILED left without `connect()`.** After a fatal connection-level ERROR followed by the server closing (`respond_with_error`'s default), the connection can go FAILED and then CONNECTING again: the ERROR's handling is queued behind the close's retry (`ConnectionConnectingState`, `ConnectionConnectedState`), and the queued `SetConnectingStateCommand` runs after FAILED without a state check. Verified; kept as `[DeviationFact]` `StateWaitTests.ConnectionStaysFailedWhenTheServerClosesAfterAFatalError`. Affects RTN14g, RTN15j and `realtime/unit/RTN25/error-reason-protocol-error-5`.

> **5xx ERROR while CONNECTING goes to DISCONNECTED, not FAILED (RTN14g; deterministic).** `RealtimeWorkflow` `HandleConnectingErrorCommand` sends an ERROR with `statusCode` 500–504 to DISCONNECTED (`IsRetryableStatusCode`); RTN17f1 grants 5xx retry only to a DISCONNECTED response. `realtime/unit/RTN14g/error-empty-channel-failed-0` and `realtime/unit/RTN25/error-reason-protocol-error-5` (both `statusCode: 500`) fail on every run. Verified.

> **RTN14d retry timing.** A failed connection attempt that ends in an exception (or a retryable status) is retried at once, up to `1 + fallback hosts` times (`CheckInstantRetryFlag`, citing RTN17j), even with `FallbackHosts` empty; only then does the RTB1 backoff apply, and its jitter can retry before `disconnectedRetryTimeout`. Read `ConnectionStateChange.RetryIn` rather than assuming the timeout; a spec expecting the first retry after `disconnectedRetryTimeout` is a deviation candidate (suspected, unverified).

> **RTN16g3, recovery key in inactive states.** `CreateRecoveryKey()` returns `string.Empty`, not null, in CLOSING, CLOSED and FAILED and before the first connect; `ASSERT … IS null` fails as written. Evaluate mode decides; record only if the run shows it.

> **RTL7g, implicit attach states.** `Subscribe` (messages, presence, annotations) attaches whenever the channel is not ATTACHED or ATTACHING, including SUSPENDED and FAILED; RTL7g lists INITIALIZED, DETACHING and DETACHED.

> **Publish precondition errors.** Thrown and wrapped as 50000 (§4); a spec expecting the precondition's own code on the returned error is a deviation candidate.

> **RSA4e on a REST request inside a realtime spec.** 80019, not 40170 (`rest-mapping.md` §7).

> **Log levels.** An assertion on an info-level message (for example in `realtime/unit/auth/token_expiry_non_renewable_test.md`, `LOG_INFO`) is a deviation candidate; translate `LOG_INFO` as `LogLevel.Debug` with a `// NOTE:` (`rest-mapping.md` §3).

## 14. White-box specs & shape-deviation vocabulary S-1…S-4 <a id="14-white-box"></a>

Public-API specs use only the public API plus the infra. Only the specs below reach internals (SKILL.md *Accessing SDK internals*); integration and proxy specs never do, except where the spec itself prescribes the internal write (the delta-decoding row). This list's only home:

| Spec | Internal members | Rendering |
|---|---|---|
| `realtime/unit/presence/presence_map.md` | `PresenceMap` (namespace `Ably.PubSub.Realtime`) | `var map = new PresenceMap("test", InternalLogger.Create(LogLevel.Debug, new CapturingLogSink()));` then `Put`, `Remove`, `Values`, `Clear`, `StartSync`, `EndSync`, `SyncInProgress`, and the internal `Members` dictionary for `get`; shape tags S-1…S-4 |
| `realtime/unit/presence/local_presence_map.md` | `InternalPresenceMap` (RTP17, keyed by `ClientId`) | as above with `new InternalPresenceMap(…)` |
| `realtime/unit/presence/presence_sync.md` | `PresenceMap` | as `presence_map.md` |
| `realtime/unit/connection/backoff_jitter_test.md` (`realtime/unit/RTB1a/backoff-coefficient-sequence-0`, `realtime/unit/RTB1b/jitter-coefficient-range-0`) | `ReconnectionStrategy.GetBackoffCoefficient(int)`, `GetJitterCoefficient()` (namespace `Ably.PubSub.Shared.Utils`, static) | `get_backoff_coefficient(n)` → `ReconnectionStrategy.GetBackoffCoefficient(n)`; the other RTB1 tests in the file are public-API (observe `RetryIn`) |
| `realtime/integration/delta_decoding_test.md` (`realtime/integration/RTL18/recovery-message-id-mismatch-0`) | `RealtimeChannel.LastSuccessfulMessageIds.LastMessageId` (internal setter "to simulate mismatching LastMessageIds") | `CLEAR channel._lastPayload.messageId` → `((RealtimeChannel)channel).LastSuccessfulMessageIds.LastMessageId = null;`. This is what the RTL20 check reads (`ChannelMessageProcessor.ValidateIfDeltaItHasCorrectPreviousMessageId`), not `MessageDecodingContext.PreviousPayload`. Do it only after messages arrived: before that the property holds the shared `LastMessageIds.Empty` instance |

**Not white-box (public-API data helpers).** `encode_recovery_key({…})` and the recovery key's `fromJson`/`toJson` in `connection_recovery_test.md` and `realtime_client.md`: the key is a JSON document (`connectionKey`, `msgSerial`, `channelSerials`), so build or parse it with `JObject` (`new JObject { … }.ToString(Formatting.None)`, `JObject.Parse`), not with the internal `RecoveryKeyContext`. `client.options.X` (`realtime/unit/client/realtime_timeouts.md`): the IDL has no public options attribute and the client keeps the instance it was given, so read the test's own `ClientOptions` variable with a `// NOTE:`; don't use the internal `client.Options`.

**Shape-deviation vocabulary (S-n) stays in these notes on purpose.** `writing-derived-tests.md` ("Recording deviations") allows the vocabulary in the module's mapping reference. It applies to the PresenceMap white-box specs (`presence_map.md`, `local_presence_map.md`, `presence_sync.md`). The tags are internal-shape adaptations (`writing-derived-tests.md` "Idiomatic translation vs genuine deviations", case 3): assert the equivalent observable and cite the tag in a `// NOTE: S-n` at each use; record them as Adapted Tests at evaluation only if an observable is lost.

| Tag | Spec interface | SDK | Rendering |
|---|---|---|---|
| S-1 | `put(message) -> PresenceMessage?` (the message to emit, or null if stale) | `bool Put(PresenceMessage)` (true when applied; ENTER/UPDATE stored as PRESENT copies) | `IS NOT null` → `.Should().BeTrue()`; `IS null` → `.Should().BeFalse()`; assertions on the returned message read the stored member instead (S-3) |
| S-2 | `remove(message) -> PresenceMessage?` (the LEAVE to emit) | `bool Remove(PresenceMessage)` | as S-1; a LEAVE's fields are asserted from the input message |
| S-3 | `get(memberKey) -> PresenceMessage?` | no method; internal `Members` (`ConcurrentDictionary<string, PresenceMessage>`) keyed by `GetKey(m)`: `PresenceMessage.MemberKey` is `"{ClientId}:{ConnectionId}"` (`InternalPresenceMap` keys by `ClientId`) | a file-local helper `Get(map, connectionId, clientId)` that builds the key with `new PresenceMessage { ConnectionId = …, ClientId = … }.MemberKey` and returns `Members.TryGetValue(…) ? m : null`; don't hard-code the spec's `"conn-1:client-1"` order |
| S-4 | `endSync() -> List<PresenceMessage>` (synthesised LEAVE events); `isSyncInProgress` | `PresenceMessage[] EndSync()` returns the removed members as stored (action PRESENT, not LEAVE); `SyncInProgress` | assert the removed members by `ClientId`/`ConnectionId`; LEAVE synthesis (RTP19) is observable only through `Presence` events in the public-API sync specs |

## 15. Harness & helper-spec coverage <a id="15-harness"></a>

The symbol tables live in the infra README; don't copy signatures here. Realtime rows, as built:

| Spec symbol | C# (file · symbol) | README section |
|---|---|---|
| `mock_websocket.md`: `MockWebSocket(onConnectionAttempt:, onMessageFromClient:, onTextDataFrame:, onBinaryDataFrame:)`, `install_mock`, connection outcomes (`respond_with_success/_refused/_timeout/_dns_error/_error`, `respond_with_connected`), server actions (`send_to_client`, `send_to_client_and_close`, `send_to_client_raw`, `simulate_disconnect`, `close_from_server`), await API, `events`, `active_connection`, `connect_attempts`, `last_connect_url`, `reset()`, alternative API (`on_connect`, `on_message`, `on_client_message`, `await_client_message`) | `MockWebSocket`, `PendingWebSocketConnection`, `MockConnection`, `MockEvent`/`MockEventType`, `MockTransport`, `ClientCloseEvent`, `RecordedUrl` | "Symbol map: `mock_websocket.md`" |
| `send_ping_frame` | `MockConnection.SendPingFrame()` / `MockWebSocket.SendPingFrame()`: records `PING_FRAME` only. RTN23b is not applicable (§12): the platform WebSocket surfaces no ping frames and the SDK always sends `heartbeats=true`, so use `HEARTBEAT_MESSAGE` (RTN23a) | Known gaps `send_ping_frame` |
| Message templates (`CONNECTED_MESSAGE`, `ERROR_MESSAGE`, `PING_MESSAGE`, ATTACHED …) and action constants | `ProtocolMessages` | "Templates (`ProtocolMessages`)" |
| `mock_vcdiff.md`: `MockVCDiffEncoder`, `MockVCDiffDecoder`, `FailingMockVCDiffDecoder`; `plugins: {vcdiff: …}` | `MockVCDiff` file: same names; `InstallMock(decoder)`, `InstallDeltaDecoder(Func<byte[], byte[], byte[]>)` | "Symbol map: `mock_vcdiff.md` and `MockNetworkListener`" |
| `MockNetworkListener`, `simulate_network_lost/available` | `MockNetworkListener` (`SimulateNetworkLost`, `SimulateNetworkAvailable`) | same |
| `mock_http.md` (realtime-driven HTTP: auth, fallback, connectivity check, REST-backed calls) | `MockHttpClient` | "Symbol map: `mock_http.md`" |
| Waits, fake time, record-and-verify, `CONTAINS_IN_ORDER`, `poll_until`, `poll_until_success`, `process_pending_events`, logs, deviations | `UtsWait`, `UtsRecord`, `CaptureList<T>`, `UtsAssert.ContainsInOrder(list, new[] {…})`, `ProcessPendingEvents`, `EnableFakeTimers`/`AdvanceTime`/`Clock`, `CapturingLogSink`, `DeviationFact` | "Waits, time and assertions" |

- **Fixture-helper scope:** the realtime module has no fixture helpers (README "Scope"). Shared fixtures are the sandbox app (one per xUnit collection, `SandboxAppFixture`) and the proxy process (one per test process, "UTS Proxy" collection).
- **Capture recipe:** frames sent by the client come from `mockWs.MessagesFromClientOfAction(ProtocolMessages.Attach)` or `await mockWs.AwaitClientMessage(…)`; per-connection slicing uses `Events` (the helpers span all the mock's connections).

**Sanctioned stand-ins** (disclose in the file header, never in `deviations.md`):

- `plugins: { vcdiff: decoder }` → hook H2 via `InstallMock(decoder)`.
- `TestClock()` / `WITH_CLOCK` → the fake clock.
- `send_ping_frame` records `PING_FRAME` only (README Known gaps).
- One sandbox app per collection.
- Proxy token auth where the spec uses `key:`.

**Shape differences ("use the pattern; don't add the shorthand"):** a handler that references its own mock (`mock_ws.active_connection.send_to_client(…)` inside `onMessageFromClient`) needs the variable declared first: `MockWebSocket mockWs = null; mockWs = new MockWebSocket(…);` or set `mockWs.OnMessageFromClient = …` after construction. `await_client_message(action:)` sees messages sent before the call; `await_next_message_from_client` does not (README "Threading model").

**Known gaps that touch this module** (README Known gaps; a hit is a Mock Infrastructure Limitation, never a substitution): `respond_with_timeout` (WS: the SDK's own connect timer decides), `ClientCloseEvent` (`code`/`reason` fixed), `onBinaryDataFrame`/`MSGPACK_DECODE`, `enable_fake_timers()` after a timer started, `connection.ping()` continuation, `MockVCDiffEncoder` base64 trap.

## 16. Overrides of the generic flow <a id="16-overrides"></a>

Each paragraph names the SKILL.md step it overrides; everything not listed follows SKILL.md.

**OVERRIDES SKILL.md Step 3 (reading list).** Read the spec; `uts/realtime/unit/helpers/mock_websocket.md` (unit), `mock_vcdiff.md` (delta specs), `uts/rest/unit/helpers/mock_http.md` when the spec drives HTTP; these notes; `rest-mapping.md` for REST-backed calls and options; for a delegating spec, the referenced REST spec too.

**Naming and deviations file: no override.** Test naming follows SKILL.md Step 4; deviations go in the shared `src/Ably.PubSub.Tests.Shared/Uts/deviations.md` (SKILL.md Step 6).

**Addition to SKILL.md Step 2 (output path), delegating specs** (not an override). The client-agnostic base of a delegated REST suite lives in that suite's file in the REST unit target directory (`rest-mapping.md` §12); the realtime class that inherits it stays in this module's target directory; a REST-only extra filed under `uts/realtime` (§12) is still written to this module's target directory.

**Applies at SKILL.md Step 7 (expected audit results)** (not an override).

- Omitted not-applicable Test IDs (msgpack-only, RTN23b, PC3 no-plugin) are reported under `idCoverage.notApplicableInCSharp` (their `// UTS-NA:` markers), never under `missingInCSharp`.
- NOTE-annotated omissions, missing-API stubs and MIL stubs show as assertion shortfall: account for each in Step 7.
- Delegating tests inherit their assertions from the REST base: the realtime file's audit covers only its own tag (no pseudo code); the assertions are audited in the REST file.
- Sections with no Test ID (RTAN3a, the `realtime_client.md` "Shared Options" sections) carry a `// NOTE:` instead of a tag.

## 17. Integration & proxy helpers <a id="17-integration"></a>

Realtime-specific items only; the rest is in SKILL.md *Integration tests* and `rest-mapping.md` §13.

- **Base and collection:** direct-sandbox classes derive `UtsIntegrationTestBase` with `[Trait("uts", "integration")]`, `[Trait("type", "integration")]`, `[Collection(UtsSandboxCollection.Name)]`; proxy classes derive `UtsProxyTestBase` with `[Trait("uts", "proxy")]`, `[Trait("type", "integration")]`, `[Collection(UtsProxyCollection.Name)]`. Constructors take `(SandboxAppFixture fixture, ITestOutputHelper output)`.
- **Clients:** `Realtime(options)` / `Rest(options)` (sandbox endpoint and app key unless set), `ProxyRealtime(session, options)` / `ProxyRest(session, options)`. Every corpus proxy spec sets `autoConnect: false`; keep it.
- **Waits:** the bases' `AwaitConnectionState(client.Connection, state, timeout?)` / `AwaitChannelState(channel, state, timeout?)` default to 15 s (`StateTimeout`); `PollUntil` takes the spec's timeout and interval (defaults in SKILL.md *Integration tests*). A spec `WAIT` at these tiers is wall-clock: keep it only where the spec has it. On the proxy tier bound each test with `realtimeRequestTimeout` + 12 s.
- **Helpers:** `App` (`Keys[i].KeyStr`), `ApiKey`, `RandomId()`, `UniqueChannelName(prefix)` (unique names in every test), `GenerateJwt`, `RequestTokenFromSandbox`, `SandboxTokenCallback(apiKey?, onInvoked)`, `AssertSucceeded(result, what)`.
- **Proxy:** `var session = await CreateProxySession(rules: new[] { ProxyRules.Rule(ProxyRules.WsFrameToClient(ProtocolMessage.MessageAction.Attached, channelName), ProxyRules.Suppress(), times: 1) });` (never pass the literal endpoint: `rules:` alone defaults it to the sandbox; `rules: []` is plain `CreateProxySession()`); `session.AddRulesAsync(rules, ProxyRules.Prepend)`; `session.TriggerActionAsync(ProxyRules.Disconnect())` only once CONNECTED (409 otherwise); `await session.GetLogAsync()` returns typed `ProxyEvent`s, filtered with `WsConnects()`, `WsDisconnects()`, `FramesToClient(action?)`, `FramesToServer(action?)`, `HttpRequests(pathContains?)`, `MatchedBy(comment)`; `"__PASSTHROUGH__"` is an ordinary string. Each test creates and closes its own session (teardown closes sessions after clients).

## 18. Worked example <a id="18-worked-example"></a>

`realtime/unit/connection/connection_ping_test.md`, `## RTN13a - Ping sends HEARTBEAT and returns round-trip duration` (`realtime/unit/RTN13a/ping-heartbeat-roundtrip-0`), translated by hand. The spec:

```text
mock_ws = MockWebSocket(
  onConnectionAttempt: (conn) => conn.respond_with_success(
    CONNECTED_MESSAGE(connectionId: "conn-id-1", connectionKey: "conn-key-1")
  ),
  onMessageFromClient: (msg) => {
    IF msg.action == HEARTBEAT:
      mock_ws.active_connection.send_to_client(ProtocolMessage(action: HEARTBEAT, id: msg.id))
  }
)
install_mock(mock_ws)

client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", autoConnect: false))

client.connect()
AWAIT_STATE client.connection.state == ConnectionState.connected
duration = AWAIT client.connection.ping()

ASSERT duration IS NOT null
ASSERT duration >= Duration.zero
heartbeats_sent = mock_ws.events.filter(
  e => e.type == MESSAGE_FROM_CLIENT AND e.message.action == HEARTBEAT
)
ASSERT heartbeats_sent.length == 1
CLOSE_CLIENT(client)
```

As a C# test method (file header, `using` block and class per SKILL.md's unit template):

```csharp
// UTS: realtime/unit/RTN13a/ping-heartbeat-roundtrip-0
[Fact]
[Trait("spec", "RTN13a")]
public async Task RTN13a_PingHeartbeatRoundtrip()
{
    // Setup
    MockWebSocket mockWs = null;
    mockWs = new MockWebSocket(
        onConnectionAttempt: conn => conn.RespondWithSuccess(
            ProtocolMessages.ConnectedMessage(connectionId: "conn-id-1", connectionKey: "conn-key-1")),
        onMessageFromClient: msg =>
        {
            if (msg.Action == ProtocolMessage.MessageAction.Heartbeat)
            {
                // Echo back a HEARTBEAT with matching id
                mockWs.ActiveConnection.SendToClient(new JObject
                {
                    ["action"] = ProtocolMessages.Heartbeat,
                    ["id"] = msg.Id,
                });
            }
        });
    InstallMock(mockWs);

    var client = Realtime(new ClientOptions
    {
        Key = "appId.keyId:keySecret",
        AutoConnect = false,
    });

    // Test Steps
    client.Connect();
    await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

    var pingResult = await client.Connection.PingAsync();
    pingResult.IsSuccess.Should().BeTrue("the AWAITed ping must not fail ({0})", pingResult.Error);
    var duration = pingResult.Value;

    // Assertions
    duration.Should().NotBeNull();
    duration.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);

    var heartbeatsSent = mockWs.Events
        .Where(e => e.Type == MockEventType.MessageFromClient && e.Message.Action == ProtocolMessage.MessageAction.Heartbeat)
        .ToList();
    heartbeatsSent.Should().HaveCount(1);
    await CloseClient(client);
}
```

Mechanical rewrites, in order:

1. `// UTS:` tag, `[Fact]`, `[Trait("spec", "RTN13a")]`, method `RTN13a_PingHeartbeatRoundtrip`.
2. A handler that references its own mock → declare `mockWs` first.
3. `CONNECTED_MESSAGE(connectionId:, connectionKey:)` → `ProtocolMessages.ConnectedMessage(connectionId:, connectionKey:)`.
4. `msg.action == HEARTBEAT` → `msg.Action == ProtocolMessage.MessageAction.Heartbeat`; inline `ProtocolMessage(action: HEARTBEAT, id: msg.id)` → a `JObject` with wire names; `mock_ws.active_connection.send_to_client` → `mockWs.ActiveConnection.SendToClient`.
5. `install_mock` before the factory; `autoConnect: false` kept.
6. `client.connect()` → `Connect()` and `AWAIT_STATE …` → `UtsWait.AwaitConnectionState`.
7. `duration = AWAIT client.connection.ping()` → `PingAsync()` returns `Result<TimeSpan?>`: the `AWAIT` must succeed, so guard `IsSuccess`, then take `Value`.
8. `Duration.zero` → `TimeSpan.Zero`.
9. `mock_ws.events.filter(e => e.type == MESSAGE_FROM_CLIENT AND e.message.action == HEARTBEAT)` → LINQ over `mockWs.Events` with `MockEventType.MessageFromClient`.
10. `CLOSE_CLIENT(client)` → `await CloseClient(client)` in place (teardown would also close it). Spec comments are copied verbatim.

## 19. Symbol index <a id="19-symbol-index"></a>

| Area | Symbols |
|---|---|
| Client | `PubSubRealtimeClient` (`Connect`, `Close`, `Connection`, `Channels`, `Auth`, `ClientId`, `HttpClient`, `Push`, `Device`, `TimeAsync`, `StatsAsync`) |
| Connection | `Connection` (`State`, `Id`, `Key`, `ErrorReason`, `ConnectionStateTtl`, `CreateRecoveryKey`, `PingAsync`, `Connect`, `Close`, `On`/`Once`/`Off`), `ConnectionState`, `ConnectionEvent`, `ConnectionStateChange` |
| Channels | `RealtimeChannels` (`Get`, `Exists`, `Release`), `IRealtimeChannel` (`State`, `Name`, `ErrorReason`, `Properties`, `Params`, `Modes`, `Options`, `AttachAsync`, `DetachAsync`, `SetOptionsAsync`, `Subscribe`, `Unsubscribe`, `PublishAsync`, `UpdateMessageAsync`, `DeleteMessageAsync`, `AppendMessageAsync`, `HistoryAsync`, `GetMessageAsync`, `GetMessageVersionsAsync`, `Presence`, `Annotations`, `On`/`Once`/`Off`), `ChannelState`, `ChannelEvent`, `ChannelStateChange`, `ChannelProperties` |
| Presence and annotations | `Presence` (`EnterAsync`, `UpdateAsync`, `LeaveAsync`, `EnterClientAsync`, `UpdateClientAsync`, `LeaveClientAsync`, `GetAsync`, `Presence.GetParams`, `HistoryAsync`, `Subscribe`, `Unsubscribe`, `SyncComplete`), `RealtimeAnnotations` |
| Wire and results | `ProtocolMessage` (`MessageAction`, `Flag`, `HasFlag`), `Result`, `Result<T>` |
| White-box | `PresenceMap`, `InternalPresenceMap`, `InternalLogger`, `ReconnectionStrategy`, `RealtimeChannel.LastSuccessfulMessageIds` |
| Infra | `UtsUnitTestBase`, `UtsIntegrationTestBase`, `UtsProxyTestBase`, `MockWebSocket`, `PendingWebSocketConnection`, `MockConnection`, `MockEvent`, `MockEventType`, `ProtocolMessages`, `MockVCDiffEncoder`, `MockVCDiffDecoder`, `FailingMockVCDiffDecoder`, `MockNetworkListener`, `MockHttpClient`, `UtsWait`, `UtsRecord`, `CaptureList<T>`, `UtsAssert`, `CapturingLogSink`, `DeviationFact`, `ProxySession`, `ProxyRules`, `ProxyEvent` |
