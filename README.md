![Ably Pub/Sub Dotnet Header](images/NETSDK-github.png)
[![License](https://badgen.net/github/license/ably/ably-pubsub-dotnet)](https://github.com/ably/ably-pubsub-dotnet/blob/main/LICENSE)

# Ably Pub/Sub .NET SDK

Build any realtime experience using Ably’s Pub/Sub .NET SDK. Supported on all popular platforms and frameworks, including Unity and MAUI.

Ably Pub/Sub provides flexible APIs that deliver features such as pub-sub messaging, message history, presence, and push notifications. Utilizing Ably’s realtime messaging platform, applications benefit from its highly performant, reliable, and scalable infrastructure.

Find out more:

* [Ably Pub/Sub docs.](https://ably.com/docs/basics)
* [Ably Pub/Sub examples.](https://ably.com/examples?product=pubsub)

> [!NOTE]
> **2.0 is in development on this branch.**
>
> Ably Pub/Sub is being split into a device-side package and a server-side package, so that Ably can tell which side of an application a client belongs to. From 2.0 the .NET SDK ships as a set of NuGet packages:
>
> | Package | Use it for |
> |---------|------------|
> | `Ably.PubSub.Device` | End-user device applications (desktop, mobile, Unity, MAUI, browser-adjacent clients) |
> | `Ably.PubSub.Server` | Server-side and backend applications (ASP.NET, Azure hosts, workers, console apps) |
> | `Ably.PubSub.Core` | Internal implementation shared by the two packages above. Not intended for direct use; you receive it transitively |
>
> Install the package for the side your code runs on, and create clients through that package's factory methods — **they are the supported entry points**. `Ably.PubSub.Core` is internal: a client constructed directly from `AblyRealtime` or `AblyRest` is not classified as device-side or server-side, which Ably's platform behaviour and billing depend on.
>
> ```sh
> dotnet add package Ably.PubSub.Server
> ```
>
> ```csharp
> using IO.Ably.PubSub.Server;
>
> var realtime = PubSubServer.CreateRealtimeClient("<API_KEY>");
> var http = PubSubServer.CreateHttpClient("<API_KEY>");
> ```
>
> ```sh
> dotnet add package Ably.PubSub.Device
> ```
>
> ```csharp
> using IO.Ably.PubSub.Device;
>
> var realtime = PubSubDevice.CreateClient("<API_KEY>");
> ```
>
> Each factory also takes a `ClientOptions` or an `Action<ClientOptions>`. The returned clients are the ordinary `AblyRealtime` and `AblyRest`, so the whole of the `IO.Ably` API remains available — including device-side connectionless operations such as message history, presence reads and token requests, which is why the device package has one door and no separate HTTP factory.
>
> All three packages are released together, always, at one version, and each door package declares an **exact** dependency on `[<that version>]` of `Ably.PubSub.Core`. There is no supported combination of different versions of them: NuGet will refuse to resolve a `Ably.PubSub.Device` and a `Ably.PubSub.Server` that were not built from the same release, which is deliberate — it is what guarantees that a project cannot end up running two copies of the core, and that the door you installed is the door that was tested against the core it gets. When you upgrade, upgrade all the `Ably.PubSub.*` packages you reference to the same version. This applies to prereleases too: a `2.0.0-beta.1` door pins exactly `[2.0.0-beta.1]` of the core, so keep every `Ably.PubSub.*` reference on the same full prerelease version.
>
> The compiled assembly is now `Ably.PubSub.Core.dll`. The code namespace is unchanged: `using IO.Ably;` and every public type name stay as they are for now.
>
> Today's [`ably.io`](https://www.nuget.org/packages/ably.io) 1.x package is unaffected and continues from a 1.x maintenance branch for a year after 2.0 becomes generally available; it is never published from this branch again. The same applies to `ably.io.push.android` and `ably.io.push.ios`, whose Xamarin-era projects are not part of the 2.0 set (see [PushNotifications.md](./PushNotifications.md)).
>
> Never reference `ably.io` and `Ably.PubSub.*` from the same project: they share the `IO.Ably` namespace, so mixing them is a compile error by design.
>
> This also applies **transitively**. NuGet dedupes only by package ID, so a graph that pulls both `ably.io` 1.x (often via a library dependency) and any `Ably.PubSub.*` package loads *both* assemblies, and every `IO.Ably.*` type then exists twice: you get compile error CS0433 where your own code names those types, and runtime type-identity failures (`InvalidCastException`-class) where a library exposes `IO.Ably` types across its API. There is no type-forwarding between the packages. Detect it with `dotnet nuget why <project> ably.io`; if a dependency genuinely forces both, isolate them with an [`extern alias`](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/extern-alias) — note `<Aliases>` applies only to a **direct** `PackageReference`, so first promote `ably.io` to a direct reference of the affected project, then add `<Aliases>ablyLegacy</Aliases>` to it and `extern alias ablyLegacy;` in the consuming file — otherwise treat a both-packages graph as unsupported and migrate the transitive dependency off `ably.io`.

---

## Getting started

Everything you need to get started with Ably:

* [Quickstart in Pub/Sub using C# .NET.](https://ably.com/docs/getting-started/quickstart?lang=csharp)
* [SDK Setup for C# .NET.](https://ably.com/docs/getting-started/setup?lang=csharp)

---

## Supported platforms

| Platform | Support |
|----------|---------|
| .NET Standard | 2.0+|
| .NET | 6.0+, .NET Core 2.0+ |
| .NET Framework | 4.6.2+ |
| Mono | 5.4+ |
| .NET for Android, .NET for iOS and MAUI | via `netstandard2.0` |
| Unity | 2019.x+ |

> [!IMPORTANT]
> SDK versions < 1.2.12 are [deprecated](https://ably.com/docs/platform/deprecate/protocol-v1) (protocol v1, retired November 1, 2025).

---

## Installation

Install the package for the side your application runs on. Server-side and backend applications use `Ably.PubSub.Server`; end-user device applications (desktop, mobile, Unity, MAUI) use `Ably.PubSub.Device`. Both bring in `Ably.PubSub.Core` transitively — never install `Ably.PubSub.Core` directly.

Server-side (.NET CLI):

```shell
dotnet add package Ably.PubSub.Server
```

Device-side (.NET CLI):

```shell
dotnet add package Ably.PubSub.Device
```

### MAUI configuration

When using Ably in a MAUI project, be aware of potential issues caused by assembly trimming, as `ably-pubsub-dotnet` relies on the reflection API. 

Add the following to your `.csproj` file to prevent trimming of the Ably assembly:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="Ably.PubSub.Core" />
</ItemGroup>
```

---

## Usage

The following code connects to Ably's realtime messaging service, subscribes to a channel to receive messages, and publishes a test message to that same channel:

```csharp
// Initialize an Ably Realtime client through the server-side door
using IO.Ably;
using IO.Ably.PubSub.Server;

var realtime = PubSubServer.CreateRealtimeClient("your-ably-api-key");
// Device-side applications use: PubSubDevice.CreateClient("your-ably-api-key");

// Wait for connection to be established
realtime.Connection.On(ConnectionEvent.Connected, args =>
{
   Console.WriteLine("Connected to Ably");
});

// Get a reference to the 'test' channel
IRealtimeChannel channel = realtime.Channels.Get("test");

// Subscribe to all messages published to this channel
channel.Subscribe(message =>
{
   Console.WriteLine($"Received message: {message.Data}");
});

// Publish a test message to the channel
await channel.PublishAsync("test-event", "Hello World!");
```

Enable logging using a new class that implements `ILoggerSink` interface.

```csharp
class CustomLogHandler : ILoggerSink
{
    public void LogEvent(LogLevel level, string message)
    {
        Console.WriteLine($"Handler LogLevel : {level}, Data :{message}");
    }
}
```

Update clientOptions for `LogLevel` and `LogHandler`.

```csharp
clientOpts.LogLevel = LogLevel.Debug;
clientOpts.LogHandler = new CustomLogHandler();
```

### Unity usage

- Download latest `ably.pubsub.*.unitypackage` from [releases section](https://github.com/ably/ably-pubsub-dotnet/releases) and include it in the unity project.
- For more information, check [Unity README](./unity/README.md)

## Releases

The [CHANGELOG.md](./CHANGELOG.md) contains details of the latest releases for this SDK. You can also view all Ably releases on [changelog.ably.com](https://changelog.ably.com).

---

## Contributing

Read the [CONTRIBUTING.md](./CONTRIBUTING.md) guidelines to contribute to Ably.

---

## Support, feedback and troubleshooting

For help or technical support, visit Ably's [support page](https://ably.com/support) or [GitHub Issues](https://github.com/ably/ably-pubsub-dotnet/issues) for community-reported bugs and discussions.

### Increasing transport send and receive buffers for .NET framework

In high-throughput scenarios, for example, sending messages >50KB, the default WebSocket buffer in the .NET Framework can cause instability or errors. This issue is discussed in [GitHub issue #446](https://github.com/ably/ably-pubsub-dotnet/issues/446).

To mitigate this, increase the WebSocket buffer size to the maximum allowed (64KB):

```csharp
var maxBufferSize = 64 * 1024;

var options = new ClientOptions();
var websocketOptions = new MsWebSocketOptions
{
    SendBufferInBytes = maxBufferSize,
    ReceiveBufferInBytes = maxBufferSize
};

options.TransportFactory = new MsWebSocketTransport.TransportFactory(websocketOptions);

var realtime = PubSubServer.CreateRealtimeClient(options);
```
