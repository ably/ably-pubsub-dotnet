# Upgrading from ably.io 1.x to Ably Pub/Sub 2.0

Ably Pub/Sub 2.0 splits the single `ably.io` package into device-side and server-side packages. This lets Ably classify every client as device-side or server-side, which the platform's behaviour and monthly-active-user (MAU) billing depend on. The API shape is unchanged, but three things are renamed for 2.0: the root namespace moves from `IO.Ably` to **`Ably.PubSub`** (see [New root namespace](#new-root-namespace)), the client classes are renamed — `AblyRealtime` is now `PubSubRealtimeClient` and `AblyRest` is now `PubSubHttpClient` (see [Renamed types](#renamed-types)) — and both the package you install and the way you construct the client change.

## Package coordinates

| 1.x | 2.0 | Install in |
|-----|-----|------------|
| `ably.io` | `Ably.PubSub.Device` | An end-user device app: mobile, desktop, Unity, a set-top box — any client the end user holds |
| `ably.io` | `Ably.PubSub.Server` | A backend: ASP.NET or Azure host, a worker, a console app — any server the end user does not hold |
| — | `Ably.PubSub.Core` | **Never install directly.** The shared implementation, pulled in transitively by the two packages above. |

Choose the package by *where the code runs*, not by which Ably features you use. Both doors expose the full `Ably.PubSub` API.

## Construct clients through the door factories

The client constructors are internal in 2.0: application code cannot construct a client directly from the core, because a client built that way would carry no device/server classification. Use the factories instead.

Device (was `new AblyRealtime("your-ably-api-key")`):

```csharp
using Ably.PubSub;
using Ably.PubSub.Device;

PubSubRealtimeClient realtime = PubSubDevice.CreateClient("your-ably-api-key");
// also: CreateClient(ClientOptions), CreateClient(o => o.ClientId = "...")
```

Server realtime (was `new AblyRealtime(options)` in a backend):

```csharp
using Ably.PubSub;
using Ably.PubSub.Server;

PubSubRealtimeClient realtime = PubSubServer.CreateRealtimeClient("your-ably-api-key");
```

Server REST (was `new AblyRest(options)`):

```csharp
using Ably.PubSub;
using Ably.PubSub.Server;

PubSubHttpClient rest = PubSubServer.CreateHttpClient("your-ably-api-key");
```

Every factory accepts an API key string, an Ably token string, a `ClientOptions`, or an `Action<ClientOptions>`.

## New root namespace

The single biggest mechanical change for 1.x code: the root namespace moves from `IO.Ably` to **`Ably.PubSub`**, so that namespace, assembly name and package id all agree. Update your directives:

```csharp
// 1.x                          // 2.0
using IO.Ably;                  using Ably.PubSub;
using IO.Ably.Realtime;         using Ably.PubSub.Realtime;
using IO.Ably.Push;             using Ably.PubSub.Push;
using IO.Ably.Rest;             using Ably.PubSub.Http;
```

Sub-namespaces map one-to-one, with two deliberate exceptions: `IO.Ably.Rest` becomes `Ably.PubSub.Http` (matching the [type renames](#renamed-types)), and the door namespaces simplify to their package names — `IO.Ably.PubSub.Device`/`IO.Ably.PubSub.Server` become `Ably.PubSub.Device`/`Ably.PubSub.Server`.

Because v1 keeps `IO.Ably.*` and v2 lives under `Ably.PubSub.*`, the two no longer share any type names — see [Do not mix 1.x and 2.0](#do-not-mix-1x-and-20-in-one-project) for what that does and does not buy you.

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

Names that refer to Ably's REST API service or wire options are unchanged (`ClientOptions.RestHost`, `ClientOptions.IdempotentRestPublishing`, and so on).

## Deprecated API removed

2.0 drops every member 1.x had marked `[Obsolete]`, and hides the client constructors behind the door factories.

| Removed | Use instead |
|---------|-------------|
| `new AblyRealtime(...)` / `new AblyRest(...)` (now internal) | `PubSubDevice.CreateClient(...)`, `PubSubServer.CreateRealtimeClient(...)`, `PubSubServer.CreateHttpClient(...)` |
| `Auth.Authorise(...)` / `Auth.AuthoriseAsync(...)` | `Auth.Authorize(...)` / `Auth.AuthorizeAsync(...)` |
| `Auth.CreateTokenRequestObject(...)` / `Auth.CreateTokenRequestObjectAsync(...)` | `Auth.CreateTokenRequest(...)` / `Auth.CreateTokenRequestAsync(...)` (return the serialized token request) |
| `Connection.RecoveryKey` | `Connection.CreateRecoveryKey()` |
| `ClientOptions.FallbackHostsUseDefault` | Nothing — the default fallback hosts apply automatically; set `ClientOptions.FallbackHosts` only to supply custom hosts |
| `ClientOptions.CaptureCurrentSynchronizationContext` | `ClientOptions.CustomContext` (pass the `SynchronizationContext` explicitly) |
| `AblyRest.Request(string method, ..., JToken body, ...)` | `PubSubHttpClient.RequestV2(...)` (string body) or `PubSubHttpClient.Request(HttpMethod, ...)` |
| `HistoryRequestParams` | `PaginatedRequestParams` |
| `IRealtimeChannel.HistoryAsync(bool untilAttach)` / `HistoryAsync(PaginatedRequestParams, bool untilAttach)` | `HistoryAsync()` / `HistoryAsync(PaginatedRequestParams)` |
| `Presence.IsSyncComplete` | `Presence.SyncComplete` |

## Do not mix 1.x and 2.0 in one project

With the namespace move, `ably.io` (all types under `IO.Ably.*`) and `Ably.PubSub.*` (all types under `Ably.PubSub.*`) **no longer collide**: a project that resolves both — even transitively, through a library that still depends on `ably.io` 1.x — compiles side-by-side, with each package's types unambiguous. A dependency that has not migrated yet no longer blocks your own migration.

Do not settle into a mixed graph, though. Two SDKs in one process means two independent realtime connections and two token flows, and v1 clients carry no device/server classification, so they receive the 1.x billing treatment. A library's v1 types (`IO.Ably.Message`, …) are also distinct from your v2 types (`Ably.PubSub.Message`, …) — values crossing that boundary must be converted explicitly. Migrate the remaining `ably.io` consumers when you can; use `dotnet nuget why <project> ably.io` to find them.

## Pin the door and the core at the same version

`Ably.PubSub.Device` and `Ably.PubSub.Server` depend on `Ably.PubSub.Core` with an **exact** version range (e.g. `[2.0.0]`), so restoring a door restores exactly the matching core. Do not add a separate `Ably.PubSub.Core` reference at a different version — keep the whole set on one version, prereleases included (a `2.0.0-beta.2` door pins `[2.0.0-beta.2]`).

## The MAU forcing function

Once MAU-based pricing is live, a client that is not classified as device- or server-side is rejected. The 2.0 core makes such a client impossible to construct from application code — the `PubSubRealtimeClient`/`PubSubHttpClient` constructors are internal, so the door factories are the only way in, and each factory stamps its side's classification. Always go through the factories, and use a fresh or door-appropriate `ClientOptions` per client.

## Xamarin and older device apps

Xamarin-era apps consume `Ably.PubSub.Device` through its `netstandard2.0` asset, the same way they consumed `ably.io`. **One gap:** device push-receive (push activation on Android/iOS) is not in the 2.0 packages yet — the two platform satellites were not carried over. If your app only publishes/subscribes, reads message history or presence, or requests tokens, it is unaffected. If it registers to *receive* push notifications on the device, stay on `ably.io` 1.x; a device push-receive port on `Ably.PubSub.Device` is a possible future follow-up, currently **parked** with no committed milestone. Push administration (sending pushes and managing devices from a backend) is unaffected and available through `Ably.PubSub.Server`.

## Unity

Install the `.unitypackage` attached to the GitHub release. Its bundled plugin, `Ably.PubSub.Device.dll`, is the core merged with the device door and dependencies, so construct your client through the door: `using Ably.PubSub.Device;` then `PubSubDevice.CreateClient(...)` — see the sample under `Assets/Ably/Examples`.

## The 1.x line

`ably.io` 1.x continues to receive security and critical fixes from its maintenance branch for one year from the 2.0 release, then reaches end of life.
