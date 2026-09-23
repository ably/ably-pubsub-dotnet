# Upgrading from ably.io 1.x to Ably Pub/Sub 2.0

Ably Pub/Sub 2.0 splits the single `ably.io` package into device-side and server-side packages. This lets Ably classify every client as device-side or server-side, which the platform's behaviour and monthly-active-user (MAU) billing depend on. The runtime API is unchanged — the namespace is still `IO.Ably`, and you still get an `AblyRealtime` or `AblyRest` — only the package you install and the way you construct the client change.

## Package coordinates

| 1.x | 2.0 | Install in |
|-----|-----|------------|
| `ably.io` | `Ably.PubSub.Device` | An end-user device app: mobile, desktop, Unity, a set-top box — any client the end user holds |
| `ably.io` | `Ably.PubSub.Server` | A backend: ASP.NET or Azure host, a worker, a console app — any server the end user does not hold |
| — | `Ably.PubSub.Core` | **Never install directly.** The shared implementation, pulled in transitively by the two packages above. |

Choose the package by *where the code runs*, not by which Ably features you use. Both doors expose the full `IO.Ably` API.

## Construct clients through the door factories

The `new AblyRealtime(...)` / `new AblyRest(...)` constructors are internal in 2.0: application code cannot construct a client directly from the core, because a client built that way would carry no device/server classification. Use the factories instead.

Device (was `new AblyRealtime("your-ably-api-key")`):

```csharp
using IO.Ably;
using IO.Ably.PubSub.Device;

AblyRealtime realtime = PubSubDevice.CreateClient("your-ably-api-key");
// also: CreateClient(ClientOptions), CreateClient(o => o.ClientId = "...")
```

Server realtime (was `new AblyRealtime(options)` in a backend):

```csharp
using IO.Ably;
using IO.Ably.PubSub.Server;

AblyRealtime realtime = PubSubServer.CreateRealtimeClient("your-ably-api-key");
```

Server REST (was `new AblyRest(options)`):

```csharp
using IO.Ably;
using IO.Ably.PubSub.Server;

AblyRest rest = PubSubServer.CreateHttpClient("your-ably-api-key");
```

Every factory accepts an API key string, an Ably token string, a `ClientOptions`, or an `Action<ClientOptions>`.

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
| `AblyRest.Request(string method, ..., JToken body, ...)` | `AblyRest.RequestV2(...)` (string body) or `AblyRest.Request(HttpMethod, ...)` |
| `HistoryRequestParams` | `PaginatedRequestParams` |
| `IRealtimeChannel.HistoryAsync(bool untilAttach)` / `HistoryAsync(PaginatedRequestParams, bool untilAttach)` | `HistoryAsync()` / `HistoryAsync(PaginatedRequestParams)` |
| `Presence.IsSyncComplete` | `Presence.SyncComplete` |

## Do not mix 1.x and 2.0 in one project

`ably.io` and `Ably.PubSub.*` both define the `IO.Ably` types. They are independent packages with no type-forwarding between them, so any project that resolves **both** — even transitively, through a library that still depends on `ably.io` 1.x — has each `IO.Ably` type defined twice. That is a compile error (CS0433) where your code names the type, or a runtime type-identity failure where a library exposes an `IO.Ably` type across its API. Move the whole graph to `Ably.PubSub.*` in one step; use `dotnet nuget why <project> ably.io` to find a stray transitive reference.

## Pin the door and the core at the same version

`Ably.PubSub.Device` and `Ably.PubSub.Server` depend on `Ably.PubSub.Core` with an **exact** version range (e.g. `[2.0.0]`), so restoring a door restores exactly the matching core. Do not add a separate `Ably.PubSub.Core` reference at a different version — keep the whole set on one version, prereleases included (a `2.0.0-beta.2` door pins `[2.0.0-beta.2]`).

## The MAU forcing function

Once MAU-based pricing is live, a client that is not classified as device- or server-side is rejected. The 2.0 core makes such a client impossible to construct from application code — the `AblyRealtime`/`AblyRest` constructors are internal, so the door factories are the only way in, and each factory stamps its side's classification. Always go through the factories, and use a fresh or door-appropriate `ClientOptions` per client.

## Xamarin and older device apps

Xamarin-era apps consume `Ably.PubSub.Device` through its `netstandard2.0` asset, the same way they consumed `ably.io`. **One gap:** device push-receive (push activation on Android/iOS) is not in the 2.0 packages yet — the two platform satellites were not carried over. If your app only publishes/subscribes, reads message history or presence, or requests tokens, it is unaffected. If it registers to *receive* push notifications on the device, stay on `ably.io` 1.x; a device push-receive port on `Ably.PubSub.Device` is a possible future follow-up, currently **parked** with no committed milestone. Push administration (sending pushes and managing devices from a backend) is unaffected and available through `Ably.PubSub.Server`.

## Unity

Install the `.unitypackage` attached to the GitHub release. Its bundled plugin, `Ably.PubSub.Device.dll`, is the core merged with the device door and dependencies, so construct your client through the door: `using IO.Ably.PubSub.Device;` then `PubSubDevice.CreateClient(...)` — see the sample under `Assets/Ably/Examples`.

## The 1.x line

`ably.io` 1.x continues to receive security and critical fixes from its maintenance branch for one year from the 2.0 release, then reaches end of life.
