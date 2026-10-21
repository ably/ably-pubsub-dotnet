# REST module: spec → ably-pubsub-dotnet translation notes

Read this **before translating any spec from `uts/rest`**. The REST specs are written in a language-agnostic pseudocode that mirrors the **ably-js** API; this SDK (ably-pubsub-dotnet) differs in naming (PascalCase, `…Async`), failure model (exceptions, `Result`) and options (`Endpoint` replaces the host options). So almost every spec line needs a mechanical rewrite, not a literal transcription. This doc is that rewrite table. The spec's own Interface Definition (the IDL section of `specifications/features.md` in the spec clone: `class RestClient`, `class Auth`, `class RestChannel`, …) is the canonical bridge; no typed-SDK variant applies. The SDK source (`src/Ably.PubSub.Shared/`) is the ground truth: where this doc, the IDL and the source disagree about the SDK's API, the source wins, so fix this doc.

Symbols are cited by file and member, never by line. "Unit base" is `UtsUnitTestBase`, "integration base" `UtsIntegrationTestBase`, "proxy base" `UtsProxyTestBase`; the infra README is `src/Ably.PubSub.Tests.Shared/Uts/Infra/README.md`. The realtime module's notes (`realtime-mapping.md`) reuse every row below for realtime specs that drive a REST client or a REST-backed realtime method.

SDK facts: `Defaults.ProtocolVersion` is `"6"` (sent as `X-Ably-Version`); `StatsAsync` pins `X-Ably-Version: 2` on `/stats`. The proxy tier needs a uts-proxy binary (no Windows release: README "Proxy tier" and Known gaps `create_proxy_session`).

> ✅ **Runtime status.** The REST SDK surface exists and the mapping entry `rest` has no readiness markers, so `rest` specs are translate **and** evaluate at the unit, integration and proxy tiers (SKILL.md Steps 5 and 6). Evaluate mode is the normal path.

**Consolidations (apply everywhere):**

1. **JSON only.** Msgpack is compiled out (`ClientOptions.UseBinaryProtocol` reads `false` unless `MSGPACK` is defined), so msgpack-only inputs are not applicable (§8).
2. **`Endpoint` replaces the deprecated host options** (`environment`, `restHost`, `realtimeHost`, `fallbackHostsUseDefault`): §3, §8.
3. **REST operations throw `AblyException`** on failure, with two exceptions. `Request` / `RequestV2` do not throw for an HTTP error status: the response carries `StatusCode`, `Success` and `ErrorCode`, and only transport failures after the fallback hosts throw (RSC19e). `DeviceRegistrations.GetAsync` returns `Task<Result<DeviceDetails>>`, but an HTTP error status (including 404) still throws `AblyException` first, because the HTTP requester throws before the method's own NotFound branch (`AblyHttpRequester` before `PushAdmin`).

## Table of contents

1. [The layers](#1-the-layers)
2. [Entry point & client construction](#2-entry-point)
3. [Client & channel options](#3-options)
4. [Async and failures](#4-async)
5. [Type mapping](#5-type-mapping)
6. [Operations (pseudocode → SDK)](#6-operations)
7. [Errors & error codes](#7-errors)
8. [Not applicable, missing APIs & Mock Infrastructure Limitations](#8-not-applicable)
9. [Known SDK behaviour differences](#9-known-differences)
10. [White-box specs](#10-white-box)
11. [Harness & helper-spec coverage](#11-harness)
12. [Overrides of the generic flow](#12-overrides)
13. [Integration & proxy helpers](#13-integration)
14. [Worked example](#14-worked-example)
15. [Symbol index](#15-symbol-index)

---

## 1. The layers <a id="1-the-layers"></a>

| Spec name | This SDK | Note |
|---|---|---|
| `Rest` / `RestClient` | `PubSubHttpClient` (internal constructors; public type) | Built only by the base factories (§2), never `PubSubServer` or the Device door |
| `RestChannel` | `IHttpChannel` (what `Channels.Get` returns) and the public class `HttpChannel` behind it | Members not on the interface (`Options` setter) need a cast to `HttpChannel` |
| `RestPresence` | `IPresence`, implemented explicitly by `HttpChannel` (`channel.Presence` returns the channel itself) | Call through `channel.Presence`, never on the channel |
| `RestAnnotations` | `RestAnnotations` (`channel.Annotations`) | |
| `Auth` | `IAblyAuth` (`client.Auth`); the implementation `AblyAuth` is internal | `IAblyAuth` lacks `tokenDetails` and `revokeTokens` (§8) |
| `ErrorInfo` / `AblyException` | `ErrorInfo` (data) and `AblyException` (thrown, `.ErrorInfo`) | `AblyException.Message` is `ErrorInfo.ToString()`, not the error message (§7) |
| `PaginatedResult` | `PaginatedResult<T>` | |
| `HttpPaginatedResponse` | `HttpPaginatedResponse : PaginatedResult<JToken>` | `request()` result |
| `MessageAction` | `Ably.PubSub.MessageAction` (message-level: `MessageCreate` … `MessageAppend`) | Different from the wire enum `ProtocolMessage.MessageAction` (namespace `Ably.PubSub.Types`) |
| `Push` / `PushAdmin` | `client.Push` (`PushHttp`) and `client.Push.Admin` (`PushAdmin`, exposing `DeviceRegistrations` and `ChannelSubscriptions` interfaces) | |
| `LocalDevice` | `Ably.PubSub.Push.LocalDevice : DeviceDetails` | `client.Device`; the setter is internal (§10) |

## 2. Entry point & client construction <a id="2-entry-point"></a>

`Rest(options: ClientOptions(...))` renders as the tier base's factory, which wires the doubles and keeps the client for teardown:

| Tier | Rendering | Notes |
|---|---|---|
| unit | `var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });` | `UtsUnitTestBase.Rest` calls the internal `new PubSubHttpClient(options, mobileDevice)` with a `FakeMobileDevice` |
| integration | `Rest(options)` on the integration base | sets `Endpoint` to the sandbox and `Key` to the app's first key unless the test set any auth |
| proxy | `ProxyRest(session, options)` | sets `Endpoint = "localhost"`, `Port`, `TlsPort`, `Tls = false`, and a sandbox token callback unless the test set token auth |

The factories change the options in place. `httpClient: mock_http` (a second argument to `Rest(...)`) and `install_mock(mock_http)` both render as `InstallMock(mockHttp)` **before** the factory call: a mock applies only to clients built afterwards. The unit factory pre-seeds nothing but the doubles, so translate the spec's `key:` exactly (SKILL.md *Client construction*). `useBinaryProtocol: false` may be translated or dropped (it is the only behaviour); `useBinaryProtocol: true` is a not-applicable case (§8).

## 3. Client & channel options <a id="3-options"></a>

Options are PascalCase properties; every duration is a `TimeSpan` (spec values are milliseconds: `TimeSpan.FromMilliseconds(n)`).

| Spec option | C# | Type and notes |
|---|---|---|
| `key` | `Key` | `string`. `new ClientOptions("k")` also parses a key or, without `:`, a token |
| `token` | `Token` | `string` |
| `tokenDetails` | `TokenDetails` | `TokenDetails` |
| `authCallback: (params) => …` | `AuthCallback = async tokenParams => …` | `Func<TokenParams, Task<object>>`; return a `TokenDetails`, a `TokenRequest`, or a string (§9, RSA8d) |
| `authUrl` | `AuthUrl = new Uri("…")` | `Uri` |
| `authMethod: "POST"` | `AuthMethod = HttpMethod.Post` | `HttpMethod`; default `HttpMethod.Get` |
| `authHeaders`, `authParams` | `AuthHeaders`, `AuthParams` | `Dictionary<string, string>` (initialised empty) |
| `queryTime`, `useTokenAuth` | `QueryTime`, `UseTokenAuth` | `bool?`, default `null` (unset behaves as false) |
| `defaultTokenParams` | `DefaultTokenParams = new TokenParams { … }` | `TokenParams` |
| `clientId` | `ClientId` | `string`; the setter throws `InvalidOperationException` for `"*"` (RSA7c) |
| `endpoint` | `Endpoint` | `string`: routing policy (`main`), `nonprod:<id>`, or a hostname (contains `.` or `::`, or is `localhost`) with no default fallbacks (REC1) |
| `fallbackHosts` | `FallbackHosts = new[] { … }` | `string[]`, **set-only**; an empty array disables fallback. No public getter (§8) |
| `port`, `tlsPort`, `tls` | `Port`, `TlsPort`, `Tls` | `int`, `int`, `bool` |
| `httpOpenTimeout`, `httpRequestTimeout` | `HttpOpenTimeout`, `HttpRequestTimeout` | `TimeSpan` (defaults 4 s, 10 s) |
| `httpMaxRetryCount` | `HttpMaxRetryCount` | `int` (default 3) |
| `httpMaxRetryDuration` | `HttpMaxRetryDuration` | `TimeSpan` (default 15 s) |
| `fallbackRetryTimeout` | `FallbackRetryTimeout` | `TimeSpan` (default 10 min) |
| `idempotentRestPublishing` | `IdempotentRestPublishing` | `bool` (default true) |
| `addRequestIds` | `AddRequestIds` | `bool` (sent as a header: §9) |
| `logLevel` | `LogLevel` | `LogLevel` enum: `Debug`, `Warning`, `Error`, `None`; mapping below |
| `logHandler` | `LogHandler = sink` | `ILoggerSink` (`LogEvent(LogLevel, string)`), not a delegate: use `CapturingLogSink` |
| `useBinaryProtocol` | `UseBinaryProtocol` | no-op; reads `false` always |
| `pushFullWait` | `PushAdminFullWait` | `bool` |
| `environment`, `restHost`, `realtimeHost`, `fallbackHostsUseDefault` | none (removed in favour of `Endpoint`) | §8, deprecated options |
| `maxMessageSize`, `connectivityCheckUrl` | none | §8, missing APIs |

**Log levels.** The SDK has no Info or Verbose level.

| Spec | C# |
|---|---|
| `logLevel: verbose` / `info` / `debug`, `LOG_INFO` | `LogLevel.Debug` |
| `warn` / `warning`, `WARN` | `LogLevel.Warning` |
| `error` | `LogLevel.Error` |
| `none` | `LogLevel.None` |

The SDK filters `level < LogLevel`, so `Debug` passes everything. A test that asserts a message was logged *at info level* is a deviation candidate at evaluation: don't rewrite it to `Debug` silently (add a `// NOTE:` naming the mapping). A handler's third `context` argument doesn't exist (`ILoggerSink.LogEvent(level, message)`): context assertions are language-inapplicable (README Known gaps `(level, message, context)`). Each factory-built client gets its own logger, so a `CapturingLogSink` sees only that client.

**Channel options.** `ChannelOptions` is shared with realtime: `new ChannelOptions(encrypted: true, @params: cipherParams, modes: new ChannelModes(ChannelMode.Subscribe), channelParams: new ChannelParams { ["rewind"] = "1" })`. The parameter named `@params` is the `CipherParams`; the string map is `channelParams`. `new ChannelOptions(cipherParams)` and `new ChannelOptions(byte[] key)` set `Encrypted`. `CipherParams(byte[] key)`, `CipherParams(string algorithm, string base64Key, …)` and `Crypto.GetDefaultParams(…)` (namespace `Ably.PubSub.Encryption`) build cipher params.

## 4. Async and failures <a id="4-async"></a>

- **REST operations are `XxxAsync` methods returning `Task<T>` and throwing `AblyException` on failure** (`TimeAsync`, `StatsAsync`, `PublishAsync`, `HistoryAsync`, `GetMessageAsync`, `StatusAsync`, `Auth.AuthorizeAsync`, `RequestTokenAsync`, `CreateTokenRequestAsync`, annotation and push calls). `AWAIT x` → `await x`. Most also have blocking overloads (`Time()`, `Publish(…)`); never use them in tests.
- **Exceptions to "throws":** `Request` / `RequestV2` and `DeviceRegistrations.GetAsync` (consolidation 3 above). `DeviceRegistrations.GetAsync` returns `Task<Result<DeviceDetails>>`, but a 404 still throws first (§6). Push `List(…)` and `ChannelSubscriptions` calls throw like the rest. `Request` / `RequestV2` set `NoExceptionOnHttpError`.
- **Callbacks:** an `authCallback` runs on a thread-pool thread (`Task.Run`, bounded by `RealtimeRequestTimeout`); a `MockHttpClient` handler runs synchronously inside `HttpClient.SendAsync` and must not block (README "Threading model"). Capture into `CaptureList<T>`.
- **No real timers at unit tier:** `EnableFakeTimers()` and `await AdvanceTime(ms)` (the HTTP timeout runs on fake time; README Known gaps `respond_with_timeout`).

`FAILS WITH error` on a throwing API (the FluentAssertions form accepts derived types such as `AblyInsecureRequestException`; see SKILL.md *Assertions (FluentAssertions)*). Then assert `error.Code`, `error.StatusCode` and `error.Message`:

```text
AWAIT channel.publish(name: "e", data: "d") FAILS WITH error
ASSERT error.code == 40160
```

```csharp
Func<Task> act = () => channel.PublishAsync("e", "d");
var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
error.Code.Should().Be(40160);
```

On a `Result`-returning API (`DeviceRegistrations.GetAsync` when it returns, and the realtime operations): `result.IsSuccess.Should().BeFalse(); var error = result.Error;`.

## 5. Type mapping <a id="5-type-mapping"></a>

| Spec | C# | Notes |
|---|---|---|
| Time (ms since epoch) | `DateTimeOffset` | `result.millisecondsSinceEpoch` → `result.ToUnixTimeMilliseconds()`; build with `DateTimeOffset.FromUnixTimeMilliseconds(ms)` (or the SDK's public `DateExtensions.FromUnixTimeInMilliseconds`) |
| `TokenDetails.expires` / `issued` | `TokenDetails.Expires` / `Issued` | `DateTimeOffset`, not nullable; unset is `DateTimeOffset.MinValue` |
| `timestamp` fields | `TokenParams.Timestamp`, `TokenRequest.Timestamp`, `Message.Timestamp`, `PresenceMessage.Timestamp` | `DateTimeOffset?` |
| `ttl` | `TokenParams.Ttl` / `TokenRequest.Ttl` | `TimeSpan?`; every option timeout is `TimeSpan` |
| Wire form of durations | `TimeSpanJsonConverter`, `DateTimeOffsetJsonConverter` | milliseconds on the wire; assert decoded objects with SDK values, wire bodies with wire values |
| `Capability` | `Capability` | `new Capability("{\"*\":[\"subscribe\"]}")`, compare with `.ToJson()` (normalised); `Capability.AllowAll` |
| `Message.data` | `Message.Data` (`object`) | a `string`, `byte[]`, or a Newtonsoft `JToken` (`JObject`/`JArray`) for JSON-encoded data; assert with a cast (`((JObject)msg.Data)["key"]`). Publishing a C# anonymous object or `JObject` JSON-encodes it |
| `extras` | `MessageExtras` | `new MessageExtras(JToken)`, `.ToJson()`, `.Delta` |
| `PresenceAction` | `PresenceAction` | `Absent`, `Present`, `Enter`, `Leave`, `Update` |
| `MessageAction` | `Ably.PubSub.MessageAction` | `MessageCreate`=0, `MessageUpdate`, `MessageDelete`, `Meta`, `MessageSummary`, `MessageAppend`; **not** the nested `ProtocolMessage.MessageAction`; `(int)action`, `(MessageAction)n` |
| `AnnotationAction` | `AnnotationAction` | `Create`, `Delete` |
| `ChannelMode` | `Ably.PubSub.ChannelMode` | `Presence`, `Publish`, `Subscribe`, `PresenceSubscribe`, `AnnotationPublish`, `AnnotationSubscribe` (the first four are what the REST specs use) |
| `direction`, `unit` strings | `QueryDirection` (`Forwards`, `Backwards`), `StatsIntervalGranularity` (`Minute`, `Hour`, `Day`, `Month`) | spec strings (`"forwards"`, `"hour"`) → enum values; numeric wire actions stay numbers in wire bodies |
| `toJson()` | `JsonHelper.Serialize(obj)` → string | parse with `JObject.Parse`; idiomatic, not a deviation |
| `fromJson()` | `JsonHelper.Deserialize<T>(json)`, `JsonHelper.DeserializeObject<T>(JObject)` | `ErrorInfo.fromJson(…)` → `JsonHelper.DeserializeObject<ErrorInfo>(jobject)` (its `JsonProperty` names are `code`, `statusCode`, `message`, `href`, `cause`) |
| `Message.fromEncoded(obj)`, `fromEncodedArray` | `Message.FromEncoded(json.ToString(Formatting.None))` or `Message.FromEncoded(Message, ChannelOptions)`; `Message.FromEncodedArray(…)` | |
| `ASSERT x IS DateTime` / `IS PaginatedResult` / `IS ErrorInfo` on a typed member | keep the spec line as a comment naming the compile-time guarantee | statically guaranteed; `// ASSERT result IS DateTime: guaranteed at compile time (TimeAsync returns DateTimeOffset)` |
| `error.cause IS ErrorInfo OR IS Exception` | `ErrorInfo.Cause` is `ErrorInfo` | an exception cause lives in `ErrorInfo.InnerException` |
| `ErrorInfo.statusCode` | `ErrorInfo.StatusCode` (`HttpStatusCode?`) | `((int?)error.StatusCode).Should().Be(401)` or `.Should().Be(HttpStatusCode.Unauthorized)`; `HttpPaginatedResponse.StatusCode` is non-nullable `HttpStatusCode` |

## 6. Operations (pseudocode → SDK) <a id="6-operations"></a>

Every row was read in the SDK source. "Throws" means `AblyException`.

### Client: time, stats, request

| Pseudocode | C# | Result / failure |
|---|---|---|
| `client.time()` | `await client.TimeAsync()` | `DateTimeOffset`; throws. Parses the body as `List<long>` (first element) |
| `client.stats(start:, end:, direction:, limit:, unit:)` | `await client.StatsAsync(new StatsRequestParams { Start = …, End = …, Direction = QueryDirection.Forwards, Limit = 10, Unit = StatsIntervalGranularity.Hour })` | `PaginatedResult<Stats>`; throws. `StatsRequestParams` defaults `Unit = Minute`, `Limit = 100` |
| `client.request(method, path, version:, params:, body:, headers:)` | `await client.Request(new HttpMethod(method), path, paramsDict, bodyJToken, headersDict)` (or `RequestV2(string method, …, string body, …)`) | `HttpPaginatedResponse`; no `version` argument (§8) |
| `client.connection.checkConnectivity()` (`rest/unit/fallback.md`, REC3; a realtime client) | `await client.HttpClient.CanConnectToAbly()` | `bool`; GETs `Defaults.InternetCheckUrl` through the injected `HttpClient` (no retry) |
| `client.device` | `client.Device` (`LocalDevice`: `Id`, `ClientId`, `DeviceIdentityToken`, `DeviceSecret`, …) | `client.device = …` is white-box (§10) |

### Channels: publish, history, status, messages, versions

| Pseudocode | C# | Result / failure |
|---|---|---|
| `client.channels.get(name[, options])`, `channels.exists`, `channels.release` | `client.Channels.Get(name[, options])`, `Exists(name)`, `Release(name)` (bool) | `Get` with options on an existing channel replaces its options |
| `channel.name` | `channel.Name` | |
| `channel.publish(name:, data:)` / `publish(message: Message(…))` / `publish(messages: […])` | `PublishAsync(name, data[, clientId])`, `PublishAsync(new Message(name, data, clientId, extras))` or an object initialiser, `PublishAsync(new[] { … })` | `PublishResult` (`Serials`, never null); throws. Client-supplied ids are kept; otherwise ids are generated when `IdempotentRestPublishing` |
| `channel.history(params)` | `HistoryAsync(new PaginatedRequestParams { Start, End, Direction, Limit })` | `PaginatedResult<Message>`; throws |
| `channel.status()` | `StatusAsync()` | `ChannelDetails` (`ChannelId`, `Status.IsActive`, `Status.Occupancy.Metrics.{Connections, Publishers, Subscribers, PresenceConnections, PresenceMembers, PresenceSubscribers}`) |
| `channel.getMessage(serialOrMessage)` | `GetMessageAsync(serial)` / `GetMessageAsync(message)` | `Message`; throws (empty serial throws before any request) |
| `channel.getMessageVersions(serial, params)` | `GetMessageVersionsAsync(serial, PaginatedRequestParams)` or `GetMessageVersionsAsync(Message, PaginatedRequestParams)` | `PaginatedResult<Message>`; the `Message` overload needs a populated serial |
| `channel.updateMessage(msg, operation?, params?)`, `deleteMessage`, `appendMessage` | `UpdateMessageAsync(Message, MessageOperation, IDictionary<string, string>)`, `DeleteMessageAsync`, `AppendMessageAsync` | `UpdateDeleteResult` (`VersionSerial`) |
| `MessageOperation(clientId:, description:, metadata:)` | `new MessageOperation { ClientId, Description, Metadata }` | |
| `channel.setOptions(options)` | `((HttpChannel)channel).Options = options` | Idiomatic property (not on `IHttpChannel`); `null` becomes empty options |

### Presence

| Pseudocode | C# | Result / failure |
|---|---|---|
| `channel.presence.get(limit:, clientId:, connectionId:)` | `channel.Presence.GetAsync(limit, clientId, connectionId)` or `GetAsync(PaginatedRequestParams)` | `PaginatedResult<PresenceMessage>`; `limit` outside 0..1000 throws `ArgumentException` |
| `channel.presence.history(params)` | `channel.Presence.HistoryAsync(PaginatedRequestParams)` | |

### Annotations

| Pseudocode | C# | Result / failure |
|---|---|---|
| `channel.annotations.publish(serialOrMsg, Annotation(type:, name:, …))`, `.delete(…)` | `channel.Annotations.PublishAsync(serial, new Annotation(type, name, data, count, clientId))`, `DeleteAsync(…)` | `Task`; throws |
| `channel.annotations.get(serial, params: { "k": "v" })` | `channel.Annotations.GetAsync(serial, new AnnotationsRequestParams { ExtraParameters = new Dictionary<string, string> { ["k"] = "v" } })` | `PaginatedResult<Annotation>`; every entry, `limit` included, goes to the querystring as given (the typed `Limit` is the idiomatic alternative) |

### Auth & tokens

| Pseudocode | C# | Result / failure |
|---|---|---|
| `client.auth.authorize(tokenParams?, authOptions?)` | `client.Auth.AuthorizeAsync(tokenParams, authOptions)` | `TokenDetails`; throws |
| `client.auth.requestToken(…)` | `client.Auth.RequestTokenAsync(tokenParams, authOptions)` | `TokenDetails`; throws |
| `client.auth.createTokenRequest(…)` | `JsonHelper.Deserialize<TokenRequest>(await client.Auth.CreateTokenRequestAsync(tokenParams, authOptions))` | returns the signed request as a **JSON string**; parse it into `TokenRequest` (or `JObject`) to assert fields; no key throws 40101 |
| `client.auth.clientId` | `client.Auth.ClientId` | |
| `TokenParams(ttl:, capability:, clientId:, timestamp:, nonce:)` | `new TokenParams { Ttl = TimeSpan…, Capability = new Capability(json), ClientId, Timestamp, Nonce }` | `ToRequestParams()` gives the query form |
| `AuthOptions(…)` | `new AuthOptions { … }` (same members as §3) | `AuthOptions(string key)` parses a key |
| `RETURN NEVER_RESOLVING_FUTURE` from an `authCallback` | `AuthCallback = tokenParams => new TaskCompletionSource<object>().Task` | a callback that never completes |

### Pagination & `HttpPaginatedResponse`

| Pseudocode | C# | Result / failure |
|---|---|---|
| `result.items`, `.hasNext()`, `.isLast()`, `.next()` | `.Items` (`List<T>`), `.HasNext`, `.IsLast` (properties), `await .NextAsync()` | `NextAsync()` with no next page returns an empty page, not null |
| `response.first()` | `await response.FirstAsync()` | `HttpPaginatedResponse` only |
| `response.statusCode`, `.success`, `.errorCode`, `.errorMessage`, `.headers`, `.items` | `(int)response.StatusCode`, `Success`, `ErrorCode`, `ErrorMessage`, `Headers` (`HttpHeaders`: `GetValues(name)`), `Items` (`List<JToken>`; a non-array body is one item) | `ErrorMessage`: §9 |

### Push

| Pseudocode | C# | Result / failure |
|---|---|---|
| `client.push.admin.publish(recipient, payload)` | `await client.Push.Admin.PublishAsync(JObject recipient, JObject payload)` | throws |
| `push.admin.deviceRegistrations.save(device)` | `client.Push.Admin.DeviceRegistrations.SaveAsync(DeviceDetails)` | `DeviceDetails` |
| `deviceRegistrations.get(id)` | `DeviceRegistrations.GetAsync(id)` | `Result<DeviceDetails>` (`.Value`); a 404 **throws** (the requester throws before the method's own NotFound branch), so `FAILS WITH` catches `AblyException` |
| `deviceRegistrations.list({"deviceId": …})` / `({"clientId": …})` / `({"limit": "2"})` | `DeviceRegistrations.List(ListDeviceDetailsRequest.WithDeviceId(id))` / `.WithClientId(id)` / `ListDeviceDetailsRequest.Empty(2)` | note `List`, not `ListAsync` |
| `deviceRegistrations.remove(id)`, `.removeWhere({…})` | `RemoveAsync(string)` (or `RemoveAsync(DeviceDetails)`), `RemoveWhereAsync(Dictionary<string, string>)` | |
| `channelSubscriptions.list({…})` | `ChannelSubscriptions.ListAsync(request)` with `var request = ListSubscriptionsRequest.Empty(limit); request.Channel = …; request.ClientId = …; request.DeviceId = …;` | spec string `"5"` → `int` 5 |
| `channelSubscriptions.listChannels({…})` | `ChannelSubscriptions.ListChannelsAsync(new PaginatedRequestParams { Limit = 1 })` | `PaginatedResult<string>` |
| `channelSubscriptions.save(sub)`, `.remove(sub)`, `.removeWhere({…})` | `SaveAsync(PushChannelSubscription)`, `RemoveAsync(PushChannelSubscription)`, `RemoveWhereAsync(IDictionary<string, string>)` | `PushChannelSubscription.ForDevice(channel, deviceId)` / `.ForClientId(channel, clientId)` |
| `channel.push.subscribeDevice()`, `subscribeClient()`, `unsubscribeDevice()`, `unsubscribeClient()` | `channel.Push.SubscribeDevice()` etc. (no `Async` suffix) | `Task`; throws. `channel.Push` is null for a client without a mobile device; the base factories always give one |
| `channel.push.listSubscriptions({"limit": "10"})` | `channel.Push.ListSubscriptions(ListSubscriptionsRequest.Empty(10))` | overrides clientId, deviceId and channel from the local device |
| `DeviceDetails(…)` | `new DeviceDetails { Id, Platform, FormFactor, ClientId, Metadata = JObject, Push = new DeviceDetails.PushData { Recipient = JObject } }` | `FormFactor`/`Platform` are strings |

**REST behaviour that shapes the request** (for assertions on captured requests): `Accept: application/json` always; `Content-Type` only on POST/PUT/PATCH bodies; `X-Ably-Version: 6` (stats: 2) and `Ably-Agent` on every request; `PaginatedRequestParams` always sends `direction` (default `backwards`) and `limit` (default `100`), and `StatsRequestParams` adds `by` (default `minute`); presence `GetAsync(limit, …)` always sends `limit`.

## 7. Errors & error codes <a id="7-errors"></a>

**Reading an error:** `ErrorInfo.Code` (`int`), `ErrorInfo.StatusCode` (`HttpStatusCode?`), `ErrorInfo.Message` (`string`), `ErrorInfo.Href` (`string`; the constructor fills `https://help.ably.io/error/<code>` when none is given and `code > 0`), `ErrorInfo.Cause` (`ErrorInfo`), `ErrorInfo.InnerException` (`Exception`). From an exception: `ex.ErrorInfo.X`. **Never** assert `ex.Message`: `AblyException.Message` is `ErrorInfo.ToString()` (`"[ErrorInfo Reason: …; Code: …]"`). Spec `exception.code`/`.statusCode`/`.message`/`.errorInfo` → `ex.ErrorInfo.Code`/`.StatusCode`/`.Message`/`ex.ErrorInfo`.

**Constructing errors:** `new ErrorInfo(message, code, statusCode, href, cause, innerException)` (named arguments help: `new ErrorInfo("Bad request", 40000, HttpStatusCode.BadRequest)`); note the argument order (message first). `new AblyException(errorInfo)`; `AblyException(errorInfo, innerException)`.

| Spec failure | SDK |
|---|---|
| An HTTP error response | `ErrorInfo.Parse` takes `code` and `message` from the JSON body's `error` object and `statusCode` from the HTTP status (a 403 without a usable body gives 40300, otherwise 50000); `href` and `cause` in the body are not read. The thrown error's `Message` is rewritten to `"Error executing request. " + <body message>` (prefixed with the request id when `AddRequestIds` is on), so an exact-equality `error.message ==` assertion on a REST failure is a deviation candidate; `CONTAINS` assertions work |
| A non-Ably exception inside `ExecuteRequest` | becomes `new AblyException(ex)`: code 50000, message `"Unexpected error :" + …`, the original as `InnerException` |
| An `authCallback` that throws or returns an unsupported type | the token request fails with code 80019 (RSA4c1's code), statusCode 401 (403 when the callback's own `AblyException` is 403), `Cause` = the callback's `ErrorInfo` (or `new ErrorInfo(ex.Message)`), `InnerException` = the exception (`AblyAuth.RequestTokenAsync`). RSA4e asks for 40170 on a REST request, so RSA4e assertions are deviation candidates (§9) |
| An `authCallback` that returns `null` or times out | raises 40170 internally, which becomes the 80019 error's `Cause` |

**Error codes:** assert literals (`40160`), not the internal `ErrorCodes` constants: a public-API spec uses only the public API. **Timestamps:** decoded `DateTimeOffset`; wire bodies hold milliseconds.

## 8. Not applicable, missing APIs & Mock Infrastructure Limitations <a id="8-not-applicable"></a>

How each row is rendered:

- **Not applicable** (the input cannot exist in this SDK): omit the whole test and leave one marker alone on its line, in the standard form `// UTS-NA: <full Test ID> — not applicable: <reason>`. These are never deviations or coverage gaps. For a row or part of a test, keep the spec line as a comment under a `// NOTE:` instead.
- **Missing API, whole test depends on it:** a missing-API stub as in SKILL.md *Test patterns for a diagnosed failure* (skip reason `Missing SDK public API: <Type#member or option> (<spec point>); see deviations.md (Missing SDK Public API / Features)`), recorded under *Missing SDK Public API / Features* in `deviations.md` in evaluate mode and listed in the run summary in translate-only mode. A missing *optional* feature is not applicable (`// UTS-NA:`) instead.
- **Missing API, only part of a test needs it:** comment out just those lines under the exact skip-reason text (`// Missing SDK public API: <Type#member or option> (<spec point>); see deviations.md (Missing SDK Public API / Features)`) and keep the rest running; stub the whole test only if nothing meaningful would remain.
- **Adapt first** (SKILL.md *First, try to adapt the test*): when another public mechanism, or at the unit tier an existing internal member, reaches the behaviour, the test runs through it, marked `// DEVIATION: <Type#member> absent; using <equivalent>` and recorded under *Adapted Tests* noting the absent public API. Adapted Tests always takes precedence; a missing-API stub or partial test is only for when adapting fails.

### Msgpack-only inputs (not applicable)

Msgpack is compiled out of this SDK build (`UseBinaryProtocol` is a no-op). Whole tests, one marker each:

```csharp
// rest/unit/rest_client.md
// UTS-NA: rest/unit/RSC8/error-decoded-from-msgpack-0 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
// UTS-NA: rest/unit/RSC8d/mismatched-response-content-type-0 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
// rest/unit/request.md
// UTS-NA: rest/unit/RSC19c/protocol-headers-msgpack-1 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
// rest/unit/auth/token_renewal.md
// UTS-NA: rest/unit/RSA4b/renewal-msgpack-response-4 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
// rest/unit/encoding/message_encoding.md
// UTS-NA: rest/unit/RSL4/msgpack-protocol-content-type-3 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
// UTS-NA: rest/unit/RSL4c/binary-direct-msgpack-protocol-1 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
// UTS-NA: rest/unit/RSL6/msgpack-binary-stays-binary-0 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
// UTS-NA: rest/unit/RSL6/msgpack-string-stays-string-1 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
// rest/unit/presence/rest_presence.md
// UTS-NA: rest/unit/RSP5/decode-msgpack-binary-3 — not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op)
```

Partial cases (translate the rest; mark the omitted part with a `// NOTE:`):

| Spec | Test ID or section | What is omitted |
|---|---|---|
| `rest/unit/rest_client.md` | `rest/unit/RSC8a/protocol-selection-0` | table row 1 (`true`, `application/x-msgpack`) |
| `rest/unit/request.md` | `rest/unit/RSC19c/body-encoded-per-protocol-2`, `rest/unit/RSC19c/response-decoded-by-content-type-3` | the msgpack parts (translate the JSON parts) |
| `rest/unit/encoding/message_encoding.md` | `rest/unit/RSL4/encoding-fixtures-ably-common-0` | fixtures with `use_binary_protocol: true` (the test also needs `encoding.json`, which doesn't exist: a UTS spec error, see below) |
| `rest/unit/encoding/msgpack_interop.md` | both `RSL6a3` sections (no Test IDs) | whole file: no file is written; list it in the run summary |
| `rest/unit/types/options_types.md` | `rest/unit/TO3/client-options-attributes-0` | the line `ASSERT options.useBinaryProtocol == true` (comment it with the reason) |
| integration specs with `## Protocol Variants` (`batch_presence`, `history`, `publish`, `presence`, `mutable_messages`) | | the msgpack variant: one JSON test, tag and name singular, with SKILL.md's `// NOTE: msgpack variant not applicable: …` line |

### Deprecated host options (not applicable)

The SDK replaced `environment`, `restHost`, `realtimeHost` and `fallbackHostsUseDefault` with `Endpoint`. The features spec marks all four "(optional, deprecated)" (TO3k1, TO3k2, TO3k3, TO3k7: "Client libraries with no backwards-compatibility requirement may omit this option"; REC1b1 and REC2a1 apply "only to client libraries that support these deprecated options"). Tests about those options are therefore not applicable (default). Fifteen tests, one marker each:

```csharp
// UTS-NA: rest/unit/REC1b1/endpoint-conflicts-environment-0 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC1b1/endpoint-conflicts-resthost-1 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC1b1/endpoint-conflicts-realtimehost-2 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC1b1/endpoint-conflicts-fallback-default-3 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC1c1/environment-conflicts-resthost-0 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC1c1/environment-conflicts-realtimehost-1 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC1c2/environment-sets-primary-domain-0 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC1d/resthost-precedence-over-realtimehost-0 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC1d1/resthost-sets-primary-domain-0 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC1d2/realtimehost-sets-primary-domain-0 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC2a1/fallback-hosts-conflicts-use-default-0 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC2b/fallback-hosts-use-default-0 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC2c5/production-environment-fallback-domains-0 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC2c6/custom-resthost-no-fallbacks-0 — not applicable: removed option (Endpoint replaces it)
// UTS-NA: rest/unit/REC2c6/custom-realtimehost-no-fallbacks-1 — not applicable: removed option (Endpoint replaces it)
```

`rest/unit/TO/conflicting-options-validation-1` is a Test Cases table: translate rows 1 (`key` + `authCallback`, valid) and 3 (no auth options, invalid), and omit row 2 (`restHost` + `endpoint`) with a `// NOTE:` (removed option). Where the deprecated option is incidental (`rest/unit/TO3/client-options-custom-hosts-1`'s `restHost` line; `rest/unit/TG/pagination-relative-urls-5`'s `restHost: "rest.ably.io"`), keep the spec line as a comment with a `// NOTE:`; where a host is needed, `Endpoint = "<host>"` gives the same primary domain (REC1b2), noted as such.

### Missing APIs

| Missing | Spec point | Tests | Rendering |
|---|---|---|---|
| `RestClient#batchPresence` | RSC24 | `rest/unit/batch_presence.md`, `rest/integration/batch_presence.md` | missing-API stub per test |
| `RestClient#batchPublish` | RSC22 | `rest/unit/batch_publish.md` | missing-API stub per test |
| `Auth#revokeTokens` | RSA17 | `rest/unit/auth/revoke_tokens.md`, `rest/integration/revoke_tokens.md` | missing-API stub per test |
| `maxMessageSize` option and the 40009 check | TO3l8, RSL1i | `rest/unit/channel/publish.md` (the RSL1i test); in `options_types.md` the single `maxMessageSize` line | no equivalent: missing-API stub for the RSL1i test; the `options_types.md` line commented out under the skip-reason comment |
| `connectivityCheckUrl` option | REC3b | `rest/unit/REC3b/custom-connectivity-check-url-0` | no equivalent (the check URL is the constant `Defaults.InternetCheckUrl`): missing-API stub |
| `PresenceMessage.fromEncoded` / `fromEncodedArray` | TP4 | `rest/unit/TP4/from-encoded-presence-0` | not missing for the unit test: the internal `MessageHandler.FromEncoded<PresenceMessage>` / `FromEncodedArray<PresenceMessage>` decode the same way (equivalent, unit tier only), so call them, marked `// DEVIATION: PresenceMessage#fromEncoded absent; using internal MessageHandler.FromEncoded<PresenceMessage>` (and the array form), and record an Adapted Test noting the public method is absent |
| `PresenceMessage.size` | TP5 | `rest/unit/TP5/presence-message-size-0` | no equivalent (no size calculation exists, public or internal): missing-API stub |
| `PresenceMessage.extras` | TP3i | the `extras` assertions in `rest/unit/TP3a/presence-message-attributes-0` and `rest/unit/TP3/presence-from-json-0` | no equivalent: those lines commented out under the skip-reason comment, the rest of each test runs |
| `ChannelMetrics.objectPublishers` / `objectSubscribers` | CHM2g, CHM2h | the object* assertions in `rest/unit/CHM2/parses-all-metrics-fields-0`, `zero-and-missing-metrics-1` | no equivalent: those lines commented out under the skip-reason comment, the rest of each test runs |
| `RestClient#clientId` | RSC17 | `rest/unit/RSC17/client-id-from-options-0`, `client-id-matches-auth-1` | not missing: the IDL defines no `RestClient#clientId` (only `RealtimeClient#clientId`, RTC17) and RSC17 requires `Auth#clientId`, so assert `client.Auth.ClientId` with a `// NOTE:`, keep `client.clientId == client.auth.clientId` as a comment, and record a UTS Spec Errors entry (adapted, not skipped) |
| `MessageOperation#toJson` | MOP2 | the serialisation lines of `rest/unit/MOP2a/message-operation-fields-0` | adapted: send the operation with `channel.UpdateMessageAsync(new Message { Serial = … }, op)` against a capturing `MockHttpClient` and assert the `version` object of the request body (RSL15b7 sends the operation as the message version), marked `// DEVIATION: MessageOperation#toJson absent; using the "version" object of the UpdateMessageAsync request body`; Adapted Test noting the public `toJson` is absent |
| `PaginatedResult#first` | TG5 (UTS label TG4) | `rest/unit/TG4/first-returns-first-page-0`, `rest/integration/TG4/first-retrieves-page-0` | adapted: `channel.HistoryAsync(page.FirstQueryParams)` re-runs the first-page query, marked `// DEVIATION: PaginatedResult#first absent; using channel.HistoryAsync(page2.FirstQueryParams)`; Adapted Test. The unit test is gated on the TG link-parser deviation |
| `Auth#tokenDetails` (public) | RSA16 | every `client.auth.tokenDetails` read. The value exists internally (`((AblyAuth)client.Auth).CurrentToken`) | adapted: read the internal one at the unit tier, marked `// DEVIATION: Auth#tokenDetails absent; using internal AblyAuth.CurrentToken` at each test's first read; Adapted Test noting the public accessor is absent |
| `version` argument of `request()` | RSC19f, RSC19f1 | the SDK owner's idiom is a request-level `X-Ably-Version` header (`headers: {"X-Ably-Version": "3"}` takes precedence over the client default, RSC7e) | adapted wherever the spec passes `version`: add the header, marked `// DEVIATION: RestClient#request(version) absent; using a request-level X-Ably-Version header`; `rest/unit/RSC19f1/version-param-sets-header-0` is the Adapted Test entry |
| `ClientOptions.fallbackHosts` getter | TO3k6 | `rest/unit/TO3/client-options-custom-hosts-1` | adapted at the unit tier: read the internal `options.GetFallbackHosts()` (it returns the array that was set), marked `// DEVIATION: ClientOptions#fallbackHosts absent; using internal ClientOptions.GetFallbackHosts()`; Adapted Test noting the public getter is absent. At integration or proxy tier, where internals are out of bounds, comment the line out under the skip-reason comment instead |

### Mock Infrastructure Limitations

`load_fixtures("encoding.json")` is **not** a MIL: no `encoding.json` exists in ably-common, neither vendored (`common` @ 133665b) nor upstream (origin/main bb51cc9). `test-resources` has only `messages-encoding.json` and `presence-messages-encoding.json`, whose shape (`data`, `encoding`, `expectedType`, `expectedValue`) doesn't match the spec's fields (`input_data`, `expected_wire_data`, `expected_encoding`, `use_binary_protocol`). The spec references a fixture that doesn't exist, so `rest/unit/RSL4/encoding-fixtures-ably-common-0` is a **UTS spec error**: a skipped spec-error placeholder plus a UTS Spec Errors entry. The draft fix: point the spec at `messages-encoding.json` and use its field names, or add the fixture to ably-common. Don't substitute `messages-encoding.json` in the test.

## 9. Known SDK behaviour differences <a id="9-known-differences"></a>

Predictions read from the SDK source, **not yet run**. They are non-binding: translate faithfully, and let evaluate mode decide; record a deviation only if the run shows it, with the features-spec quote.

> **RSA8d, token string from `authCallback`.** A `string` return is parsed as a `TokenRequest` JSON (`AblyAuth.RequestTokenAsync`, `GetTokenRequest`), so `RETURN "eyJ…"` fails. Deviation candidate (RSA8d: "expects either a token string, a TokenDetails object or a TokenRequest object"). Evaluate mode decides; record only if the run shows it.

> **RSC7c, `addRequestIds`.** `request_id` is added with `AblyRequest.AddHeaders`, so it travels as an HTTP header, not the `request_id` query parameter RSC7c requires. Evaluate mode decides; record only if the run shows it.

> **HP7, `errorMessage`.** `HttpPaginatedResponse` fills `ErrorMessage` from the `X-Ably-Errorcode` header (it reads the error-code header twice); HP7 says `X-Ably-Errormessage`. Evaluate mode decides; record only if the run shows it.

> **RSH6a, push device auth header.** `PushAdmin.AddDeviceAuthenticationToRequest` sends `X-Ably-DeviceIdentityToken` (`Defaults.DeviceIdentityTokenHeader`); RSH6a says `X-Ably-DeviceToken`. Evaluate mode decides; record only if the run shows it.

> **TS12c and RSC6b4, stats unit.** `Stats` has no `unit` (it maps `intervalGranularity`, pre-2.1 schema: no `entries`, `appId`, `inProgress`, `schema`), and `StatsRequestParams` sends the unit as `by`, not `unit`. Evaluate mode decides; record only if the run shows it.

> **TP3h, `memberKey` order.** `PresenceMessage.MemberKey` is `"{ClientId}:{ConnectionId}"`; `rest/unit/TP3h/member-key-combines-ids-0` expects `"conn-1:user-1"`. TP3h fixes no order, so classify at evaluation (over-specified spec versus SDK non-compliance). Evaluate mode decides.

> **RSC6b2/RSC6b3 and RSP3 absent defaults.** The SDK always sends `direction` and `limit` (and `by`); the IDL marks these "api-default" ("unnecessary to send"), not forbidden, so `ASSERT "limit" NOT IN query_params` failures are spec-versus-SDK questions for evaluation.

> **RSL8 encoding.** `HttpChannel.StatusAsync` builds `"/channels/" + Name` without `EncodeUriPart` (other channel paths encode), so `rest/unit/RSL8/status-special-chars-encoded-1` may fail (suspected; depends on what `System.Uri` escapes).

> **`/time` mocks as objects.** Many specs stub `/time` with `{"time": N}` (`fallback.md`, `rest_client.md`, `request_endpoint.md`, `authorize.md`); `TimeAsync` deserialises `List<long>`, so `client.time()` throws there. Translate faithfully; at evaluation this is likely a UTS spec question (the REST `/time` endpoint returns `[ms]`), suspected, unverified.

> **Exact REST error messages.** The thrown message carries the prefix `"Error executing request. "` (§7).

> **RSA4e, `authCallback` failure on a REST request.** The SDK raises 80019 (with the cause nested), not 40170 (§7); `realtime/unit/RSA4e/rest-callback-error-40170-0` and any REST RSA4e test expecting 40170 are deviation candidates.

> **`QueryTime` / `UseTokenAuth` defaults.** `null`, not `false`; `ASSERT options.queryTime == false` fails as written (deviation candidate; don't coalesce).

## 10. White-box specs <a id="10-white-box"></a>

Public-API specs use only the public API plus the UTS infra, even though internals are visible to both test heads. Only the specs below reach internals (SKILL.md *Accessing SDK internals*). This list's only home:

| Spec | Internal member | Why |
|---|---|---|
| `rest/unit/push/push_channels.md` | `PubSubHttpClient.Device` internal setter ("only for testing purposes"): `client.Device = device;` with `var device = LocalDevice.Create("test-client", new FakeMobileDevice()); device.Id = "…"; device.DeviceIdentityToken = "…";` | `client.device = LocalDevice(…)` sets up the local device; the public API has no setter (RSH8 is read-only) |
| `rest/integration/push_channels.md` | the same setter | An integration spec that requires the same setup write: the spec itself prescribes the internal write, so it is listed here rather than treated as a public-API violation |

`FakeMobileDevice` is the native `Ably.PubSub.Tests.Push.FakeMobileDevice` the infra already reuses. REST has no shape-deviation vocabulary: these specs write one internal setter during setup and assert only public behaviour.

## 11. Harness & helper-spec coverage <a id="11-harness"></a>

The symbol tables live in the infra README; don't copy signatures here. REST rows, as built:

| Spec symbol | C# | README section |
|---|---|---|
| `mock_http.md`: `MockHttpClient(onConnectionAttempt:, onRequest:)`, `install_mock`, await API, `reset()`, connection outcomes, request fields, `respond_with`, `respond_with_delay`, `respond_with_timeout`, legacy `queue_*` and `captured_requests` | `MockHttpClient`, `PendingHttpConnection`, `PendingHttpRequest`, `MockHttpOutcome`, `RecordedUrl`; `InstallMock`, `UninstallMocks` on the unit base | "Symbol map: `mock_http.md`" |
| Waits, polls, fake time, `CONTAINS_IN_ORDER`, `FAIL`, log capture, `CLOSE_CLIENT`, deviations | `UtsWait`, `UtsRecord`, `CaptureList<T>`, `UtsAssert`, `CapturingLogSink`, `DeviationFact`, `EnableFakeTimers`/`AdvanceTime`/`Clock` (`FakeClock`) | "Waits, time and assertions" |
| Strict default HTTP (unexpected requests fail at teardown), per-client logger and device | unit-base factories | "Unit-tier defaults" |
| Sandbox provisioning, keys, JWT, sandbox tokens | `SandboxApp`, `SandboxAppFixture`, integration base helpers | "Integration tier" |
| `create_proxy_session`, rules, log | `UtsProxyTestBase.CreateProxySession`, `ProxySession`, `ProxyRules`, `ProxyEvent` | "Proxy tier" |

- **Fixture-helper scope:** the REST module has no fixture helpers (README "Scope": nothing to index). The only shared fixture is the sandbox app: one app per xUnit collection (`SandboxAppFixture` through `[Collection(UtsSandboxCollection.Name)]`; the proxy collection provisions its own).
- **Fixtures:** `ResourceHelper.GetResource("<name>")` (native, namespace `Ably.PubSub.Tests`) reads the embedded ably-common files: `crypto-data-128.json`, `crypto-data-256.json`, `messages-encoding.json`, `test-app-setup.json`. `encoding.json` exists nowhere in ably-common (a UTS spec error, §8).

**Sanctioned stand-ins** (disclose in the file header, never in `deviations.md`):

- `TestClock()` / `WITH_CLOCK` / `test_clock.advance` → `EnableFakeTimers()` + `AdvanceTime` (the block scope is the test; `test_clock.now()` → `Clock.Now`).
- One sandbox app per collection instead of per spec file (the `POST https://sandbox…/apps` row).
- Proxy clients authenticate with a sandbox token callback even where the spec uses `key:`.
- The `mutable:` namespace is added by `SandboxApp`.

**Shape differences ("use the pattern; don't add the shorthand"):** a value-returning poll is `T x = await UtsWait.PollUntil<T>(…)` (reference types only: wrap a value type and return it); `poll_until_success` is its own helper; `parse_json(request.body)` is `JObject.Parse(request.BodyText)` (or `JArray.Parse`).

## 12. Overrides of the generic flow <a id="12-overrides"></a>

Each paragraph names the SKILL.md step it overrides; everything not listed follows SKILL.md.

**OVERRIDES SKILL.md Step 3 (reading list).** Read the spec; `uts/rest/unit/helpers/mock_http.md` for unit specs; these notes; the infra README sections named in §11 for the tier. For a test that drives a realtime client (`rest/unit/fallback.md` REC3, `rest/integration/presence.md`, `batch_presence.md`, `revoke_tokens.md`), also read `realtime-mapping.md`.

**Naming and deviations file: no override.** Test naming follows SKILL.md Step 4; deviations go in the shared `src/Ably.PubSub.Tests.Shared/Uts/deviations.md` (SKILL.md Step 6).

**Delegated REST specs: client-agnostic base classes (addition to SKILL.md Step 2).** Six realtime unit specs delegate to REST unit specs ("the tests in `uts/rest/unit/…` should be used to test a `RealtimeClient`/`RealtimeChannel` instance"): `time.md` (RTC6), `stats.md` (RTC5), `request.md` (RTC9), `channel/history.md` (RTL10a), `channel/get_message.md` (RTL28), `channel/message_versions.md` (RTL31); `channel/annotations.md` (RTAN3a) is the seventh, but its realtime section has no Test ID. Translate each of those REST specs into one file holding two classes:
- `public abstract class <ClassName>Base` (generic over the client, and the channel for the channel specs, where the two clients share no interface: `TimeTestsBase<TClient>`, `HistoryTestsBase<TClient, TChannel>`, `GetMessageTestsBase<TClient, TChannel>`, `MessageVersionsTestsBase<TClient, TChannel>`; `StatsTestsBase` uses `IStatsCommands` and `RequestTestsBase` uses `PubSubHttpClient`, which both clients expose). It derives `UtsUnitTestBase`, holds every translated `[UtsFact]` with its `// UTS: rest/unit/…` tag, carries no `[Trait("uts", …)]`, and reaches the client only through small abstract hooks: `NewClient(ClientOptions)` for the spec's `client = Rest(options: …)`, plus `GetChannel(client, name)` and the operation (`TimeAsync(client)`, `HistoryAsync(channel[, query])`, `GetMessageAsync(channel, serial)`, `GetMessageVersionsAsync(channel, serial, query)`) where no shared interface carries it. The test bodies stay a line-by-line translation; only `Rest(` → `NewClient(` and `x.Op(` → `Op(x, ` change.
- `[Trait("uts", "unit")] public class <ClassName> : <ClassName>Base<…>`: the REST implementation of the hooks (`Rest(options)`, `client.Channels.Get(name)`, `channel.Op(…)`).
The realtime class (`realtime-mapping.md` §12) derives the same base. xUnit runs the inherited facts once per concrete class, under each class's own name and traits. No other REST spec gets a base: add one only when a realtime spec delegates to it.

**Applies at SKILL.md Step 7 (expected audit results)** (not an override).

- Each not-applicable Test ID in §8 is reported under `idCoverage.notApplicableInCSharp` (its `// UTS-NA:` marker), never under `missingInCSharp`.
- `rest/unit/encoding/msgpack_interop.md` has no Test IDs and is wholly not applicable: it gets no file and is listed in the run summary.
- NOTE-annotated omissions, missing-API stubs and MIL stubs show as assertion shortfall: account for each in Step 7.
- The delegated REST specs keep their tests in the base class in the same file, so the audit of the REST file sees every tag and assertion (no helper to review by hand).

## 13. Integration & proxy helpers <a id="13-integration"></a>

REST-specific items only; the rest is in SKILL.md *Integration tests*.

- **Base and collection:** direct-sandbox classes derive `UtsIntegrationTestBase`, carry `[Trait("uts", "integration")]`, `[Trait("type", "integration")]` and `[Collection(UtsSandboxCollection.Name)]`, and take `(SandboxAppFixture fixture, ITestOutputHelper output)`. Proxy classes derive `UtsProxyTestBase` with `[Trait("uts", "proxy")]`, `[Trait("type", "integration")]` and `[Collection(UtsProxyCollection.Name)]`.
- **App and keys:** `App` (`SandboxApp`: `Keys[i].KeyStr`, `KeyName`, `KeySecret`, `Capability`), `ApiKey` (first key), `SandboxEndpoint`. `keys[2]` in a spec → `App.Keys[2].KeyStr`.
- **Helpers:** `RandomId()`, `UniqueChannelName(prefix)`, `ExtractKeyName`, `ExtractKeySecret`, `GenerateJwt(keyName, keySecret, ttl?, clientId?, capability?, expiresAt?)` (spec ttl in ms → `TimeSpan.FromMilliseconds`; spec `expires_at: now() - N` → `expiresAt: DateTimeOffset.UtcNow.AddSeconds(-N)`, an expired rather than invalid token), `RequestTokenFromSandbox(apiKey?, tokenParams?)`, `SandboxTokenCallback(apiKey?, onInvoked)` (counts `auth_callback_count++`), `AssertSucceeded(result, what)` for `Result`s. Use a unique channel name in every integration test.
- **Waits:** wall-clock; `PollUntil` takes the spec's timeout and interval (defaults in SKILL.md *Integration tests*).
- **Proxy:** `var session = await CreateProxySession(rules: new[] { ProxyRules.Rule(ProxyRules.HttpRequest(…), ProxyRules.HttpRespond(503)) });` (no endpoint argument: it defaults to the sandbox; `rules: []` is plain `CreateProxySession()`), then `ProxyRest(session, options)`; `session.GetLogAsync()` returns typed `ProxyEvent`s (filter with `HttpRequests(pathContains)`, `HttpResponses()`). The spec-local `token_auth_callback(api_key)` in `rest/integration/proxy/rest_fallback.md` (a sandbox token fetched directly, bypassing the proxy) is `SandboxTokenCallback(ApiKey)`; `http_replace_response` is `ProxyRules.HttpReplaceResponse(status, body, headers)`. Never add file-local helpers for infra gaps: extend the infra.

## 14. Worked example <a id="14-worked-example"></a>

`rest/unit/time.md`, `## RSC16 - time() returns server time` (`rest/unit/RSC16/returns-server-time-0`), translated by hand. The spec:

```text
captured_requests = []
server_time_ms = 1704067200000  # 2024-01-01 00:00:00 UTC

mock_http = MockHttpClient(
  onConnectionAttempt: (conn) => conn.respond_with_success(),
  onRequest: (req) => {
    captured_requests.push(req)
    req.respond_with(200, [server_time_ms])
  }
)
install_mock(mock_http)

client = Rest(options: ClientOptions(key: "app.key:secret"))

result = AWAIT client.time()

ASSERT result IS DateTime
ASSERT result.millisecondsSinceEpoch == server_time_ms
ASSERT captured_requests.length == 1
ASSERT captured_requests[0].method == "GET"
ASSERT captured_requests[0].path == "/time"
```

As a C# test method (file header, `using` block and class per SKILL.md's unit template):

```csharp
// UTS: rest/unit/RSC16/returns-server-time-0
[Fact]
[Trait("spec", "RSC16")]
public async Task RSC16_ReturnsServerTime()
{
    // Setup
    var capturedRequests = new CaptureList<PendingHttpRequest>();
    const long serverTimeMs = 1704067200000; // 2024-01-01 00:00:00 UTC

    var mockHttp = new MockHttpClient(
        onConnectionAttempt: conn => conn.RespondWithSuccess(),
        onRequest: req =>
        {
            capturedRequests.Add(req);
            req.RespondWith(200, new[] { serverTimeMs });
        });
    InstallMock(mockHttp);

    var client = Rest(new ClientOptions { Key = "app.key:secret" });

    // Test Steps
    var result = await client.TimeAsync();

    // Assertions
    // ASSERT result IS DateTime: guaranteed at compile time (TimeAsync returns DateTimeOffset)
    result.ToUnixTimeMilliseconds().Should().Be(serverTimeMs);

    capturedRequests.Should().HaveCount(1);
    var request = capturedRequests[0];
    request.Method.Should().Be("GET");
    request.Path.Should().Be("/time");
}
```

Mechanical rewrites, in order:

1. `// UTS:` tag, `[Fact]`, `[Trait("spec", "RSC16")]`, method `RSC16_ReturnsServerTime` (the slug without its trailing `-0`).
2. `captured_requests = []` + `.push` → `CaptureList<PendingHttpRequest>` + `.Add` (handlers run on SDK threads).
3. `MockHttpClient(onConnectionAttempt:, onRequest:)` with named arguments; `req.respond_with(200, [server_time_ms])` → `RespondWith(200, new[] { serverTimeMs })` (serialised as a JSON array).
4. `install_mock` → `InstallMock` before the factory.
5. `Rest(options: ClientOptions(key: …))` → `Rest(new ClientOptions { Key = … })`.
6. `AWAIT client.time()` → `await client.TimeAsync()`.
7. `ASSERT result IS DateTime` kept as a comment (statically guaranteed); `millisecondsSinceEpoch` → `ToUnixTimeMilliseconds()`.
8. `request.method` / `request.path` → `Method` / `Path`.
9. Spec comments copied verbatim under the spec's section headings. Teardown is automatic (`UtsTestBase.DisposeAsync`, which also fails the test on unexpected strict-default requests).

## 15. Symbol index <a id="15-symbol-index"></a>

| Area | Symbols |
|---|---|
| Client | `PubSubHttpClient` (`Channels`, `Auth`, `Push`, `Device`, `TimeAsync`, `StatsAsync`, `Request`, `RequestV2`, `CanConnectToAbly`), `ClientOptions`, `AuthOptions` |
| Channels | `HttpChannels` (`Get`, `Exists`, `Release`), `IHttpChannel` / `HttpChannel` (`PublishAsync`, `HistoryAsync`, `GetMessageAsync`, `GetMessageVersionsAsync`, `UpdateMessageAsync`, `DeleteMessageAsync`, `AppendMessageAsync`, `StatusAsync`, `Options`, `Presence`, `Annotations`, `Push`), `IPresence` (`GetAsync`, `HistoryAsync`), `RestAnnotations` |
| Auth | `IAblyAuth` (`ClientId`, `AuthorizeAsync`, `RequestTokenAsync`, `CreateTokenRequestAsync`), `TokenDetails`, `TokenParams`, `TokenRequest`, `Capability` |
| Errors and results | `ErrorInfo`, `AblyException`, `Result` / `Result<T>`, `PaginatedResult<T>`, `HttpPaginatedResponse`, `PaginatedRequestParams`, `StatsRequestParams` |
| Data types | `Message`, `MessageExtras`, `MessageOperation`, `MessageVersion`, `MessageAnnotations`, `PresenceMessage`, `Annotation`, `PublishResult`, `UpdateDeleteResult`, `ChannelDetails`, `Stats` |
| Channel options | `ChannelOptions`, `ChannelModes`, `ChannelParams`, `CipherParams`, `Crypto` |
| Push | `PushAdmin`, `IDeviceRegistrations`, `IPushChannelSubscriptions`, `PushChannel`, `DeviceDetails`, `LocalDevice`, `ListDeviceDetailsRequest`, `ListSubscriptionsRequest`, `PushChannelSubscription` |
| Helpers and logging | `JsonHelper`, `DateExtensions`, `LogLevel`, `ILoggerSink` |
| Infra | `UtsUnitTestBase`, `UtsIntegrationTestBase`, `UtsProxyTestBase`, `MockHttpClient`, `PendingHttpConnection`, `PendingHttpRequest`, `RecordedUrl`, `CaptureList<T>`, `UtsWait`, `UtsAssert`, `CapturingLogSink`, `DeviationFact`, `SandboxApp`, `ProxySession`, `ProxyRules`, `ProxyEvent` |
