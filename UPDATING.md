# Upgrading from ably.io 1.x to Ably Pub/Sub 2.0

Ably Pub/Sub 2.0 splits the single `ably.io` package into device-side and server-side packages. This lets Ably classify every client as device-side or server-side, which the platform's behaviour and monthly-active-user (MAU) billing depend on. The API is largely unchanged. The breaking changes are: the package you install changes; the root namespace moves from `IO.Ably` to **`Ably.PubSub`** (see [New root namespace](#new-root-namespace)); the client classes are renamed — `AblyRealtime` is now `PubSubRealtimeClient` and `AblyRest` is now `PubSubHttpClient` (see [Renamed types](#renamed-types)); clients are created through door factories and can no longer be subclassed; deprecated 1.x members are removed; `Endpoint` replaces `Environment`/`RestHost`/`RealtimeHost`; publish methods return a `PublishResult`; releasing a channel that isn't detached now throws; and device push-receive is not available, so its API, which stays public, throws or returns `null`. Each is covered below.

## Package coordinates

| 1.x | 2.0 | Install in |
|-----|-----|------------|
| `ably.io` | `Ably.PubSub.Device` | An end-user device app: mobile, desktop, Unity, a set-top box — any client the end user holds |
| `ably.io` | `Ably.PubSub.Server` | A backend: ASP.NET or Azure host, a worker, a console app — any server the end user does not hold |
| — | `Ably.PubSub.Core` | **Never install directly.** The shared implementation, pulled in transitively by the two packages above. |

Choose the package by *where the code runs*, not by which Ably features you use. Both doors expose the full `Ably.PubSub` API.

## Construct clients through the door factories

The client constructors are internal in 2.0: application code cannot construct a client directly from the core, because a client built that way would carry no device/server classification. Use the factories instead.

Device (was `new AblyRealtime("xxxxxx.yyyyyy:zzzzzzzz")`):

```csharp
using Ably.PubSub;
using Ably.PubSub.Device;

PubSubRealtimeClient realtime = PubSubDevice.CreateClient("xxxxxx.yyyyyy:zzzzzzzz");
// also: CreateClient(ClientOptions), CreateClient(o => o.ClientId = "...")
```

Server realtime (was `new AblyRealtime(options)` in a backend):

```csharp
using Ably.PubSub;
using Ably.PubSub.Server;

PubSubRealtimeClient realtime = PubSubServer.CreateRealtimeClient("xxxxxx.yyyyyy:zzzzzzzz");
```

Server REST (was `new AblyRest(options)`):

```csharp
using Ably.PubSub;
using Ably.PubSub.Server;

PubSubHttpClient rest = PubSubServer.CreateHttpClient("xxxxxx.yyyyyy:zzzzzzzz");
```

Device REST (was `new AblyRest(...)` in a device app). There is deliberately no device HTTP factory; create a device client without connecting and use its HTTP client:

```csharp
using Ably.PubSub;
using Ably.PubSub.Device;

var realtime = PubSubDevice.CreateClient(o => { o.Key = "xxxxxx.yyyyyy:zzzzzzzz"; o.AutoConnect = false; });
PubSubHttpClient rest = realtime.HttpClient;
```

Every factory accepts an API key string, an Ably token string, a `ClientOptions`, or an `Action<ClientOptions>`. A server client given a plain Ably token is rejected with error 40167; see [Server clients and token authentication](#server-clients-and-token-authentication).

`PubSubRealtimeClient` is `sealed` in 2.0 (as `PubSubHttpClient`, formerly `AblyRest`, already was), and its `protected virtual Dispose(bool)` is now private. Code that subclassed `AblyRealtime` must wrap a client instead; call `Dispose()` to release a client.

## Server clients and token authentication

Clients created with `PubSubServer` declare themselves as servers. Authenticate them with an API key. Token authentication is accepted only for a token that carries the signed `x-ably-clientType=server` claim, which today means a JWT. With any other token (for example one from `Auth.RequestTokenAsync`), Ably rejects the realtime connection or the HTTP request with error 40167. Clients created with `PubSubDevice` are not affected and are normally authenticated with tokens.

This applies only to how a server client authenticates itself. A server client authenticated with an API key can still issue tokens and token requests for devices, with `Auth.CreateTokenRequestAsync` or `Auth.RequestTokenAsync`.

## New root namespace

The single biggest mechanical change for 1.x code: the root namespace moves from `IO.Ably` to **`Ably.PubSub`**, so that namespace, assembly name and package id all agree. Update your directives:

```csharp
// 1.x                          // 2.0
using IO.Ably;                  using Ably.PubSub;
using IO.Ably.Realtime;         using Ably.PubSub.Realtime;
using IO.Ably.Push;             using Ably.PubSub.Push;
using IO.Ably.Rest;             using Ably.PubSub.Http;
```

Sub-namespaces map one-to-one, with one deliberate exception: `IO.Ably.Rest` becomes `Ably.PubSub.Http` (matching the [type renames](#renamed-types)). The door factories are new in 2.0 and live in `Ably.PubSub.Device` and `Ably.PubSub.Server`.

Because v1 keeps `IO.Ably.*` and v2 lives under `Ably.PubSub.*`, the two no longer share any type names (except the internal delta-decoding helper `IO.Ably.DeltaCodec.dll`, which keeps its namespace and is unified as one assembly) — see [Do not mix 1.x and 2.0](#do-not-mix-1x-and-20-in-one-project) for what that does and does not buy you.

## Renamed types

2.0 renames the client-facing "REST" identifiers to "HTTP", matching the other Ably Pub/Sub SDKs. Only the type and member names change here; the namespace move is covered in [New root namespace](#new-root-namespace).

| 1.x name | 2.0 name |
|----------|----------|
| `AblyRealtime` | `PubSubRealtimeClient` |
| `AblyRest` | `PubSubHttpClient` |
| `IRealtimeClient` | `IPubSubRealtimeClient` |
| `IRestClient` | `IPubSubHttpClient` |
| `RestChannel` | `HttpChannel` |
| `RestChannels` | `HttpChannels` |
| `IRestChannel` | `IHttpChannel` |
| `AblyRealtime.RestClient` (property) | `PubSubRealtimeClient.HttpClient` |
| `PushRest` (the `AblyRest.Push` type) | `PushHttp` (the `PubSubHttpClient.Push` type) |

Names that refer to Ably's REST API service or wire options are unchanged (`ClientOptions.IdempotentRestPublishing`, and so on). The annotations API, new in 2.0, follows the same rule: `IHttpChannel.Annotations` is of type `HttpAnnotations` (the specification's `RestAnnotations`).

## Deprecated API removed

2.0 drops the members 1.x had marked `[Obsolete]`, with two exceptions that stay and are no longer marked `[Obsolete]`:

- `Presence.IsSyncInProgress` is a supported property. It has no public replacement, so it was never removed.
- `IRealtimeChannel.HistoryAsync(bool untilAttach)` and `HistoryAsync(PaginatedRequestParams, bool untilAttach)` are supported (RTL10b): with `untilAttach: true` they pass the channel's attach serial as `fromSerial`, and throw if the channel is not attached. Their 1.x deprecation was meant for presence history, which has no `untilAttach` in the specification (RTP12).

Instead, `Presence.HistoryAsync(bool untilAttach)` and `Presence.HistoryAsync(PaginatedRequestParams, bool untilAttach)` are now `[Obsolete]`, and 2.0 adds `Presence.HistoryAsync()` and `Presence.HistoryAsync(PaginatedRequestParams)`, which existing calls without `untilAttach` now bind to. The deprecated overloads still work.

Separately, 2.0 hides the client constructors behind the `PubSubDevice` / `PubSubServer` door factories; those constructors were never marked `[Obsolete]` in 1.x, but they are listed here because they are no longer part of the public API.

| Removed | Use instead |
|---------|-------------|
| `new AblyRealtime(...)` / `new AblyRest(...)` (now internal) | `PubSubDevice.CreateClient(...)`, `PubSubServer.CreateRealtimeClient(...)`, `PubSubServer.CreateHttpClient(...)` (device apps that used `AblyRest`: `PubSubDevice.CreateClient(...).HttpClient`) |
| `AblyRealtime.Dispose(bool)` (`protected virtual`, now private) | `Dispose()`; `PubSubRealtimeClient` is sealed |
| `Auth.Authorise(...)` / `Auth.AuthoriseAsync(...)` | `Auth.Authorize(...)` / `Auth.AuthorizeAsync(...)` |
| `Auth.CreateTokenRequestObject(...)` / `Auth.CreateTokenRequestObjectAsync(...)` | `Auth.CreateTokenRequest(...)` / `Auth.CreateTokenRequestAsync(...)` (return the serialized token request, which an `AuthCallback` can return as is) |
| `Connection.RecoveryKey` | `Connection.CreateRecoveryKey()` |
| `ClientOptions.FallbackHostsUseDefault` | Nothing — the default fallback hosts apply automatically unless `Endpoint` is a hostname (see [Choose the host with `Endpoint`](#choose-the-host-with-endpoint)); set `ClientOptions.FallbackHosts` only to supply custom hosts |
| `ClientOptions.CaptureCurrentSynchronizationContext` | `ClientOptions.CustomContext` (pass the `SynchronizationContext` explicitly) |
| `AblyRest.Request(string method, ..., JToken body, ...)` | `PubSubHttpClient.RequestV2(...)` (string body) or `PubSubHttpClient.Request(HttpMethod, ...)` |
| `HistoryRequestParams` | `PaginatedRequestParams` |
| `Presence.IsSyncComplete` | `Presence.SyncComplete` |
| `ClientOptions.Environment`, `ClientOptions.RestHost`, `ClientOptions.RealtimeHost` | `ClientOptions.Endpoint` — see [Choose the host with `Endpoint`](#choose-the-host-with-endpoint) |
| `ClientOptions.FullRestHost()` / `ClientOptions.FullRealtimeHost()` / `ClientOptions.GetFallbackHosts()` | Nothing — `Connection.Host` reports the host the realtime connection is using |

## Choose the host with `Endpoint`

2.0 replaces `ClientOptions.Environment`, `RestHost` and `RealtimeHost` with a single `ClientOptions.Endpoint` option. REST requests and the realtime connection now always use the same host.

| 1.x | 2.0 |
|-----|-----|
| No host options | No change; traffic moves to `main.realtime.ably.net` (see below) |
| `Environment = "sandbox"` | `Endpoint = "nonprod:sandbox"` |
| `Environment = "acme"` (dedicated cluster) | `Endpoint = "acme"` |
| `RestHost` / `RealtimeHost = "localhost"` | `Endpoint = "localhost"` |
| `RestHost` and `RealtimeHost` set to the same custom host | `Endpoint = "<that host>"` |
| `RestHost` and `RealtimeHost` set to different hosts | Not supported; use one host that serves both, or contact Ably |
| Custom `FallbackHosts` | Unchanged |

How `Endpoint` resolves:

| `Endpoint` | Primary host | Default fallback hosts |
|------------|--------------|------------------------|
| Unset | `main.realtime.ably.net` | `main.[a-e].fallback.ably-realtime.com` |
| A routing policy name, e.g. `acme` | `acme.realtime.ably.net` | `acme.[a-e].fallback.ably-realtime.com` |
| `nonprod:` plus a name, e.g. `nonprod:sandbox` | `sandbox.realtime.ably-nonprod.net` | `sandbox.[a-e].fallback.ably-realtime-nonprod.com` |
| A hostname (contains `.` or `::`, or is `localhost`) | The value as given | None |

An explicit `FallbackHosts` always replaces the default fallback hosts. A custom `Port` or `TlsPort` no longer disables the default fallback hosts.

**Before (1.x):**

```csharp
var options = new ClientOptions("xxxxxx.yyyyyy:zzzzzzzz") { Environment = "sandbox" };
var realtime = new AblyRealtime(options);
```

**After (2.0):**

```csharp
var options = new ClientOptions("xxxxxx.yyyyyy:zzzzzzzz") { Endpoint = "nonprod:sandbox" };
var realtime = PubSubServer.CreateRealtimeClient(options);
```

**Firewalls and proxies.** 1.x connected to `rest.ably.io`, `realtime.ably.io` and `[a-e].ably-realtime.com` by default. If your network restricts outbound traffic, allow `*.realtime.ably.net` and `*.fallback.ably-realtime.com` before upgrading. If you use a non-production endpoint (`nonprod:…`), also allow `*.realtime.ably-nonprod.net` and `*.fallback.ably-realtime-nonprod.com`.

## Publish results and new message APIs

Publishing now reports what the service did with the messages. Typical callers only need to recompile; code that assigns the result of a publish to a variable of the old type needs the new type.

| 1.x | 2.0 |
|-----|-----|
| `IRestChannel.PublishAsync(...)` returned `Task` | `IHttpChannel.PublishAsync(...)` returns `Task<PublishResult>` |
| `IRestChannel.Publish(...)` returned `void` | `IHttpChannel.Publish(...)` returns `PublishResult` |
| `IRealtimeChannel.PublishAsync(...)` returned `Task<Result>` | `IRealtimeChannel.PublishAsync(...)` returns `Task<Result<PublishResult>>` |

`PublishResult.Serials` holds one serial per published message, in order. A serial is `null` for a message the service discarded because of a conflation rule. On a realtime channel, `Result<PublishResult>.Value` is `null` when the service's acknowledgement carried no result.

**Behaviour change:** a successful REST publish whose response body is not a JSON object (for example plain text) now throws an `AblyException`. 1.x ignored the response body of a publish. An empty or absent body is still accepted and gives a `PublishResult` with no serials.

**Behaviour change:** HTTP `PATCH` requests now send their request body. In 1.x the body of a `PATCH` was silently dropped, which affected the push device-registration update (`PATCH /push/deviceRegistrations/:id`, which now sends its `{"push":{"recipient":...}}` body) and any `PATCH` made through `PubSubHttpClient.RequestV2("PATCH", path, ..., body)`, whose body is now sent. If you worked around the missing body, remove the workaround.

**Behaviour change:** a realtime publish whose serial falls below the range of a later `ACK` now completes as a failure (error code 50000) instead of silently succeeding. An `ACK` covers only `msgSerial` to `msgSerial + count - 1`, and the protocol treats skipped messages as not acknowledged; java and cocoa behave the same way. Entries inside the range are unaffected.

New in 2.0 on `IHttpChannel` and `IRealtimeChannel`, for channels with message updates and deletes enabled:

| API | Notes |
|-----|-------|
| `UpdateMessageAsync`, `DeleteMessageAsync`, `AppendMessageAsync` | Take a `Message` with a populated `Serial`, an optional `MessageOperation` (`ClientId`, `Description`, `Metadata`) and optional params. The message you pass is never modified. `IHttpChannel` returns `Task<UpdateDeleteResult>` and throws on failure; `IRealtimeChannel` returns `Task<Result<UpdateDeleteResult>>`, like `PublishAsync`. `UpdateDeleteResult.VersionSerial` is `null` if the message was superseded by a later update. |
| `GetMessageAsync`, `GetMessageVersionsAsync` | Retrieve the latest version, or all versions, of a message by serial. |
| `Annotations` | Publish, delete, retrieve and (on realtime channels) subscribe to annotations of a message. Experimental. |
| `Message.Serial`, `Message.Version`, `Message.Action`, `Message.Annotations` | Populated on messages received from the service. |

`IHttpChannel` and `IRealtimeChannel` gain the members above, so code that implements these interfaces (for example, test fakes) must add them. `IHttpChannel.Annotations` is of type `HttpAnnotations`; the annotations API is experimental.

**Behaviour change:** the SDK now declares wire protocol version 6 (1.x declared 2). The version is sent as the realtime `v` parameter and the REST `X-Ably-Version` header, and the service sends the newer message structure. The stats endpoint remains pinned to the v2 response format, so `StatsAsync` results are unchanged.

**Behaviour change:** `Request()`/`RequestV2()` callers that send no `X-Ably-Version` header now receive protocol-6 response shapes (for example, `GET /channels` returns channel names, and a batch `POST /messages` returns a single results envelope). Pass the `X-Ably-Version` header to pin a version.

## Release a realtime channel only once it is detached

In 1.x, `Channels.Release(name)` and `Channels.ReleaseAll()` on a realtime channel that was not in the `Initialized`, `Detached` or `Failed` state detached it and removed it once the detach completed. In 2.0 they instead throw an `AblyException` with error code 90011 and status code 400, and leave the channel as it was. `ReleaseAll()` checks every channel first, and releases none of them if any one is in another state. Detach the channel and wait for the detach to complete before releasing it:

```csharp
// 1.x
realtime.Channels.Release("my-channel");

// 2.0
var channel = realtime.Channels.Get("my-channel");
await channel.DetachAsync();
realtime.Channels.Release("my-channel");
```

## Do not mix 1.x and 2.0 in one project

With the namespace move, `ably.io` (all types under `IO.Ably.*`) and `Ably.PubSub.*` (all types under `Ably.PubSub.*`, apart from the shared delta-decoding helper `IO.Ably.DeltaCodec.dll`) **no longer collide**: a project that resolves both — even transitively, through a library that still depends on `ably.io` 1.x — compiles side-by-side, with each package's types unambiguous. A dependency that has not migrated yet no longer blocks your own migration.

Do not settle into a mixed graph, though. Two SDKs in one process means two independent realtime connections and two token flows, and v1 clients carry no device/server classification, so they receive the 1.x billing treatment. A library's v1 types (`IO.Ably.Message`, …) are also distinct from your v2 types (`Ably.PubSub.Message`, …) — values crossing that boundary must be converted explicitly. Migrate the remaining `ably.io` consumers when you can; use `dotnet nuget why <project> ably.io` to find them.

## Pin the door and the core at the same version

`Ably.PubSub.Device` and `Ably.PubSub.Server` depend on `Ably.PubSub.Core` with an **exact** version range (e.g. `[2.0.0]`), so restoring a door restores exactly the matching core. Do not add a separate `Ably.PubSub.Core` reference at a different version — keep the whole set on one version.

## Push notifications

**Device push-receive is not available in 2.0.** The 1.x Xamarin satellite packages `ably.io.push.android` and `ably.io.push.ios`, with their `AndroidMobileDevice.Initialise(...)` / `AppleMobileDevice.Initialise(...)` entry points and `OnNewRegistrationToken(...)` callbacks, were not carried over, and nothing in 2.0 replaces them. Without them a device cannot be activated. The device push-receive API stays public, but it does not work in 2.0:

| API | 2.0 behaviour |
|-----|---------------|
| `realtime.Push.Activate()` / `realtime.Push.Deactivate()` | Throw an `AblyException` that says device push is not available and links the feature request |
| `client.Device` (`LocalDevice`) on `PubSubRealtimeClient` / `PubSubHttpClient` | Returns `null` |
| `channel.Push` (`PushChannel`) on realtime and HTTP channels | Returns `null`, and logs why |
| `IMobileDevice`, `PushCallbacks`, `RegistrationToken`, `LocalDevice` | Public, but there is no way to supply a device |

If your app registers to *receive* push notifications on the device, stay on `ably.io` 1.x. To request device push-receive for 2.x, comment on https://github.com/ably/ably-pubsub-dotnet/issues/1360. See also [PushNotifications.md](https://github.com/ably/ably-pubsub-dotnet/blob/main/PushNotifications.md).

**Push administration is unchanged.** `client.Push.Admin` (`PushAdmin`, with `DeviceRegistrations` and `ChannelSubscriptions`) and its types (`DeviceDetails`, `PushChannelSubscription`, `ListDeviceDetailsRequest`, `ListSubscriptionsRequest`, `DeviceFormFactor`) stay public on clients from both doors. Push administration is intended for server-side use: it needs the `push-admin` capability, which Ably enforces server-side, so use it from a backend built on `Ably.PubSub.Server`.

## Xamarin and older device apps

Xamarin-era apps consume `Ably.PubSub.Device` through its `netstandard2.0` asset, the same way they consumed `ably.io`. **One gap:** device push-receive (push activation on Android/iOS) is not in the 2.0 packages — see [Push notifications](#push-notifications). If your app only publishes/subscribes, reads message history or presence, or requests tokens, it is unaffected.

## Unity

Install the `.unitypackage` attached to the GitHub release. Its bundled plugin, `Ably.PubSub.Device.dll`, is the core merged with the device door and dependencies, so construct your client through the door: `using Ably.PubSub.Device;` then `PubSubDevice.CreateClient(...)` — see the sample under `Assets/Ably/Examples`.

## The 1.x line

`ably.io` 1.x continues to receive security and critical fixes from its maintenance branch for one year from the 2.0 release, then reaches end of life.
