![Ably Pub/Sub .NET Header](https://raw.githubusercontent.com/ably/ably-pubsub-dotnet/main/images/NETSDK-github.png)

[![NuGet: Ably.PubSub.Server](https://img.shields.io/nuget/v/Ably.PubSub.Server?label=Ably.PubSub.Server)](https://www.nuget.org/packages/Ably.PubSub.Server)
[![NuGet: Ably.PubSub.Device](https://img.shields.io/nuget/v/Ably.PubSub.Device?label=Ably.PubSub.Device)](https://www.nuget.org/packages/Ably.PubSub.Device)
[![License](https://badgen.net/github/license/ably/ably-pubsub-dotnet)](https://github.com/ably/ably-pubsub-dotnet/blob/main/LICENSE)

# Ably Pub/Sub .NET SDK

The .NET SDK for [Ably Pub/Sub](https://ably.com/docs/basics): publish and subscribe to channels,
and use message history and presence, from servers and from applications on end-user devices.

## Overview

The SDK is published as two packages, one for each side of the connection. You choose the package
by where your code runs, and create your client through that package's entry point.

| Package | Use it for | Entry point | Platforms |
|---------|------------|-------------|-----------|
| [`Ably.PubSub.Server`](https://www.nuget.org/packages/Ably.PubSub.Server) | Servers and other trusted backend environments | `PubSubServer.CreateRealtimeClient`, `PubSubServer.CreateHttpClient` | .NET, .NET Framework, Mono |
| [`Ably.PubSub.Device`](https://www.nuget.org/packages/Ably.PubSub.Device) | Applications on end-user devices | `PubSubDevice.CreateClient` | .NET, .NET Framework, Mono, .NET for Android and iOS, MAUI, Unity |

### Choosing a package

| Your code runs in | Package | Create clients with |
|-------------------|---------|---------------------|
| An ASP.NET Core app, a worker service, an Azure host, a console job or any other backend you operate | `Ably.PubSub.Server` | `PubSubServer.CreateRealtimeClient(...)` or `PubSubServer.CreateHttpClient(...)` |
| A desktop app (WPF, WinForms), a MAUI or .NET for Android/iOS app, a Unity game, or any other client the end user holds | `Ably.PubSub.Device` | `PubSubDevice.CreateClient(...)` |

Choose by where the code runs, not by which Ably features you use: both packages expose the whole
`Ably.PubSub` API. The device package has a single entry point; for HTTP-only calls on a device,
use the client's `HttpClient` property, as shown in the device quick start below.

## Requirements

| Platform | Requirement |
|----------|-------------|
| .NET | .NET 6.0 or later. Both packages ship `net6.0` and `net7.0` builds; later .NET versions use the `net7.0` build. |
| .NET Framework | 4.6.2 or later, or Mono. |
| .NET for Android, .NET for iOS, MAUI | With `Ably.PubSub.Device`. See "MAUI and assembly trimming" under Platform notes. |
| Xamarin and other .NET Standard 2.0 runtimes | Through the `netstandard2.0` build of either package. |
| Unity | 2019.x or later, with the API Compatibility Level set to .NET Standard 2.0. See "Unity" under Platform notes. |

## Installation

Use exactly one of `Ably.PubSub.Server` or `Ably.PubSub.Device` in an app. Never reference
`Ably.PubSub.Core` directly: it is the shared implementation, and both packages bring in the
matching version for you.

### Server

```shell
dotnet add package Ably.PubSub.Server
```

Or add a package reference to your project file:

```xml
<PackageReference Include="Ably.PubSub.Server" Version="2.0.0" />
```

### Device

```shell
dotnet add package Ably.PubSub.Device
```

Or add a package reference to your project file:

```xml
<PackageReference Include="Ably.PubSub.Device" Version="2.0.0" />
```

### Unity

Unity projects install the `.unitypackage` attached to each
[GitHub release](https://github.com/ably/ably-pubsub-dotnet/releases/latest) instead of a NuGet
package. See "Unity" under Platform notes.

### Migrating from 1.x

Version 2 replaces the `ably.io` package with these two packages and moves the root namespace from
`IO.Ably` to `Ably.PubSub`. Remove `ably.io`, add the package for your side, change each
`using IO.Ably...;` to `using Ably.PubSub...;` (`IO.Ably.Rest` becomes `Ably.PubSub.Http`), and
replace `new AblyRealtime(...)` and `new AblyRest(...)` with the entry points above. They return
`PubSubRealtimeClient` and `PubSubHttpClient`.

```shell
dotnet remove package ably.io
dotnet add package Ably.PubSub.Server   # or Ably.PubSub.Device
```

Other changes that can affect a running app:
[`Endpoint` replaces `Environment`, `RestHost` and `RealtimeHost`](https://github.com/ably/ably-pubsub-dotnet/blob/main/UPDATING.md#choose-the-host-with-endpoint),
[publish methods return a `PublishResult`](https://github.com/ably/ably-pubsub-dotnet/blob/main/UPDATING.md#publish-results-and-new-message-apis),
[releasing a channel that isn't detached throws](https://github.com/ably/ably-pubsub-dotnet/blob/main/UPDATING.md#release-a-realtime-channel-only-once-it-is-detached),
and [device push-receive is not available](https://github.com/ably/ably-pubsub-dotnet/blob/main/UPDATING.md#push-notifications).
See [UPDATING.md](https://github.com/ably/ably-pubsub-dotnet/blob/main/UPDATING.md) for the full list.

## Quick start

### Server

Use an API key on a server. The following program connects, subscribes to a channel, publishes a
message to it and waits for the message to arrive.

```csharp
using System;
using System.Threading.Tasks;
using Ably.PubSub;
using Ably.PubSub.Realtime;
using Ably.PubSub.Server;

PubSubRealtimeClient realtime = PubSubServer.CreateRealtimeClient("xxxxxx.yyyyyy:zzzzzzzz");

realtime.Connection.On(ConnectionEvent.Connected, _ => Console.WriteLine("Connected to Ably"));

// Subscribing attaches the channel. Publishing while the client is still
// connecting queues the message until the connection is established.
IRealtimeChannel channel = realtime.Channels.Get("test-channel");
var received = new TaskCompletionSource<Message>();
channel.Subscribe(msg => received.TrySetResult(msg));

Result<PublishResult> result = await channel.PublishAsync("test-event", "hello world");
if (result.IsFailure)
{
    Console.WriteLine($"Publish failed: {result.Error}");
    return;
}

Message message = await received.Task;
Console.WriteLine($"Received: {message.Data}");

realtime.Close();
```

To publish and read history over HTTP without a realtime connection, create an HTTP client
instead. Publishing returns a `PublishResult` with the serial of each message:

```csharp
using System;
using Ably.PubSub;
using Ably.PubSub.Http;
using Ably.PubSub.Server;

PubSubHttpClient http = PubSubServer.CreateHttpClient("xxxxxx.yyyyyy:zzzzzzzz");
IHttpChannel channel = http.Channels.Get("test-channel");

PublishResult published = await channel.PublishAsync("test-event", "hello world");
Console.WriteLine($"Published, serial {published.Serials[0]}");

PaginatedResult<Message> history = await channel.HistoryAsync();
foreach (Message message in history.Items)
{
    Console.WriteLine($"{message.Name}: {message.Data}");
}
```

Your server also issues the tokens that devices authenticate with. Return a token request from
the endpoint your devices use as their `AuthUrl`:

```csharp
using System;
using Ably.PubSub;
using Ably.PubSub.Server;

PubSubHttpClient http = PubSubServer.CreateHttpClient("xxxxxx.yyyyyy:zzzzzzzz");

// Return this JSON from the endpoint your devices use as their AuthUrl.
string tokenRequest = await http.Auth.CreateTokenRequestAsync(new TokenParams { ClientId = "me" });
Console.WriteLine(tokenRequest);
```

See [token authentication](https://ably.com/docs/auth/token) for how devices use it.

> **Important:** clients from `PubSubServer` declare themselves as servers. Authenticate them with
> an API key. Token authentication works only with a token that carries the signed
> `x-ably-clientType=server` claim, which today means a JWT; with any other token, Ably rejects the
> connection or request with error 40167. Clients from `PubSubDevice` are not affected. See
> [Server clients and token authentication](https://github.com/ably/ably-pubsub-dotnet/blob/main/UPDATING.md#server-clients-and-token-authentication).

### Device

Authenticate a device with tokens issued by your own server, through `AuthUrl` or `AuthCallback`.
Never embed an API key in an app that runs on an end-user device.

```csharp
using System;
using System.Threading.Tasks;
using Ably.PubSub;
using Ably.PubSub.Device;
using Ably.PubSub.Realtime;

PubSubRealtimeClient realtime = PubSubDevice.CreateClient(options =>
{
    options.AuthUrl = new Uri("https://your-server.example.com/ably-auth");
    options.ClientId = "me";
});

// The client connects automatically. Subscribing attaches the channel.
IRealtimeChannel channel = realtime.Channels.Get("test-channel");
var received = new TaskCompletionSource<Message>();
channel.Subscribe(msg => received.TrySetResult(msg));

await channel.PublishAsync("test-event", "hello world");

Message message = await received.Task;
Console.WriteLine($"Received: {message.Data}");

realtime.Close();
```

To get tokens from your own code instead of a URL, set `AuthCallback`. It can return a token
string, a `TokenDetails`, or a token request, including the serialized request from
`Auth.CreateTokenRequestAsync`:

```csharp
PubSubRealtimeClient realtime = PubSubDevice.CreateClient(options =>
{
    options.AuthCallback = async tokenParams =>
    {
        // Ask your server for a token request, token details or token string.
        return await RequestTokenFromYourServerAsync(tokenParams); // your function
    };
    options.ClientId = "me";
});
```

The device package has no separate HTTP entry point. For HTTP calls only, create the client
without connecting and use its `HttpClient`:

```csharp
using System;
using Ably.PubSub;
using Ably.PubSub.Device;

PubSubRealtimeClient client = PubSubDevice.CreateClient(options =>
{
    options.AuthUrl = new Uri("https://your-server.example.com/ably-auth");
    options.AutoConnect = false; // HTTP calls only; call client.Connect() to go realtime
});

PaginatedResult<Message> history = await client.HttpClient.Channels.Get("test-channel").HistoryAsync();
foreach (Message message in history.Items)
{
    Console.WriteLine($"{message.Name}: {message.Data}");
}
```

## Common configuration

Every entry point accepts an API key, a `ClientOptions` object, or an `Action<ClientOptions>`
that configures one.

### Endpoint

`Endpoint` selects the Ably host for both HTTP requests and the realtime connection. It replaces
the 1.x `Environment`, `RestHost` and `RealtimeHost` options.

```csharp
PubSubRealtimeClient realtime = PubSubServer.CreateRealtimeClient(options =>
{
    options.Key = "xxxxxx.yyyyyy:zzzzzzzz";
    options.Endpoint = "nonprod:sandbox";
});
```

Never put a port in `Endpoint`: `localhost:8080` is not a hostname and does not resolve. Set the
port with `TlsPort` instead, or with `Port` when TLS is off (`Tls = false`, which needs token
authentication). See
[Choose the host with `Endpoint`](https://github.com/ably/ably-pubsub-dotnet/blob/main/UPDATING.md#choose-the-host-with-endpoint)
for how each value resolves.

### Proxy

The SDK uses your runtime's default proxy settings
([`HttpClient.DefaultProxy`](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.defaultproxy)),
which come from the `HTTPS_PROXY` and `HTTP_PROXY` environment variables or from the system proxy
settings. Both HTTP requests and the realtime connection go through that proxy.

To set a proxy in code, pass your own `HttpClient`. It applies to HTTP requests only; the
realtime connection still uses the default proxy settings.

```csharp
using System.Net;
using System.Net.Http;
using Ably.PubSub;
using Ably.PubSub.Server;

var handler = new HttpClientHandler
{
    Proxy = new WebProxy("http://proxy.example.com:8080"),
    UseProxy = true,
};

PubSubHttpClient http = PubSubServer.CreateHttpClient(options =>
{
    options.Key = "xxxxxx.yyyyyy:zzzzzzzz";
    options.HttpClient = new HttpClient(handler);
});
```

If the proxy needs authentication, set `WebProxy.Credentials`. Connection reliability then
depends on the proxy, and Ably can't help with errors the proxy introduces.

### Logging

`LogLevel` sets what the SDK logs: `LogLevel.Debug` (the most verbose), `Warning` (the default),
`Error` or `None`. The SDK writes log lines to the `ILoggerSink` you set as `LogHandler`; without
one, log lines aren't written anywhere. This works the same way as in 1.x.

```csharp
using System;
using Ably.PubSub;
using Ably.PubSub.Server;

PubSubRealtimeClient realtime = PubSubServer.CreateRealtimeClient(options =>
{
    options.Key = "xxxxxx.yyyyyy:zzzzzzzz";
    options.LogLevel = LogLevel.Debug;
    options.LogHandler = new ConsoleLogSink();
});

class ConsoleLogSink : ILoggerSink
{
    public void LogEvent(LogLevel level, string message)
    {
        Console.WriteLine($"[{level}] {message}");
    }
}
```

All clients in a process share one logger. Each client you create applies its `LogLevel`, and its
`LogHandler` if set, to that shared logger, so give every client the same logging options.

## Platform notes

### Unity

Download `ably.pubsub.<version>.unitypackage` from the
[latest GitHub release](https://github.com/ably/ably-pubsub-dotnet/releases/latest) and import it
with **Assets > Import Package > Custom Package**. The package bundles `Ably.PubSub.Device`, so
create clients with `PubSubDevice.CreateClient(...)`. Set `ClientOptions.CustomContext` to run
callbacks on the main thread. WebGL builds are not supported. See the
[Unity README](https://github.com/ably/ably-pubsub-dotnet/blob/main/unity/README.md).

### MAUI and assembly trimming

The SDK uses reflection, which assembly trimming can break. In a MAUI or trimmed app, keep the Ably
assembly by adding this to your `.csproj`:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="Ably.PubSub.Core" />
</ItemGroup>
```

### .NET Framework

Both packages run on .NET Framework 4.6.2 or later. In high-throughput scenarios, for example
sending messages larger than 50 KB, the default WebSocket buffer in the .NET Framework can cause
instability or errors
([GitHub issue 446](https://github.com/ably/ably-pubsub-dotnet/issues/446)). Raise it to the
maximum of 64 KB:

```csharp
using Ably.PubSub;
using Ably.PubSub.Server;
using Ably.PubSub.Transport;

var maxBufferSize = 64 * 1024;

var options = new ClientOptions("xxxxxx.yyyyyy:zzzzzzzz")
{
    TransportFactory = new MsWebSocketTransport.TransportFactory(new MsWebSocketOptions
    {
        SendBufferInBytes = maxBufferSize,
        ReceiveBufferInBytes = maxBufferSize,
    }),
};

PubSubRealtimeClient realtime = PubSubServer.CreateRealtimeClient(options);
```

In a device app, set the same `TransportFactory` on the `ClientOptions` you pass to
`PubSubDevice.CreateClient`.

## Known limitations

- Device push-receive (activating a device for push notifications on Android or iOS) is not
  available in 2.0. Its API is public but does not work: `Push.Activate()` and `Push.Deactivate()`
  throw, and the client's `Device` and a channel's `Push` are `null`. To request the feature,
  comment on https://github.com/ably/ably-pubsub-dotnet/issues/1360. See
  [Push notifications](https://github.com/ably/ably-pubsub-dotnet/blob/main/UPDATING.md#push-notifications).
- Push administration (`Push.Admin`) works, and is intended for server-side use with
  `Ably.PubSub.Server`.
- Unity WebGL builds are not supported (https://github.com/ably/ably-pubsub-dotnet/issues/1211).

## Further reading

- [Getting started with Pub/Sub using .NET](https://ably.com/docs/getting-started/dotnet)
- [Ably Pub/Sub documentation](https://ably.com/docs/basics) and [examples](https://ably.com/examples?product=pubsub)
- [Token authentication](https://ably.com/docs/auth/token)
- [UPDATING.md](https://github.com/ably/ably-pubsub-dotnet/blob/main/UPDATING.md): migrating from 1.x
- [CHANGELOG.md](https://github.com/ably/ably-pubsub-dotnet/blob/main/CHANGELOG.md): release notes; all Ably releases are listed on [changelog.ably.com](https://changelog.ably.com)
- [Unity README](https://github.com/ably/ably-pubsub-dotnet/blob/main/unity/README.md): using the SDK in Unity

## Contributing

Read [CONTRIBUTING.md](https://github.com/ably/ably-pubsub-dotnet/blob/main/CONTRIBUTING.md)
before contributing; it also describes the release process. The build uses Cake: run
`./build.sh --target=Build.NetStandard` (or `build.cmd` on Windows) to build and lint before you
push.

## Support, feedback and troubleshooting

For help, visit Ably's [support page](https://ably.com/support). Report bugs and compatibility
problems on [GitHub Issues](https://github.com/ably/ably-pubsub-dotnet/issues).

## License

Licensed under the Apache License, Version 2.0. See
[LICENSE](https://github.com/ably/ably-pubsub-dotnet/blob/main/LICENSE).
