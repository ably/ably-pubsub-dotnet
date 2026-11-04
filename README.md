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

| Package | Use it for | Entry point |
|---------|------------|-------------|
| [`Ably.PubSub.Server`](https://www.nuget.org/packages/Ably.PubSub.Server) | Servers and other trusted backend environments | `PubSubServer.CreateRealtimeClient`, `PubSubServer.CreateHttpClient` |
| [`Ably.PubSub.Device`](https://www.nuget.org/packages/Ably.PubSub.Device) | Applications on end-user devices, including desktop apps | `PubSubDevice.CreateClient` |

## Requirements

.NET Standard 2.0 or later (.NET 6 or later, .NET Framework 4.6.2 or later, Mono, Xamarin and
Unity).

## Installation

Use exactly one of `Ably.PubSub.Server` or `Ably.PubSub.Device` in an app.

### Server

```shell
dotnet add package Ably.PubSub.Server
```

### Device

```shell
dotnet add package Ably.PubSub.Device
```

### Unity

Unity projects use a `.unitypackage` instead. See "Unity" under Platform notes.

## Quick start

### Server

Use an API key on a server. The samples read it from the `ABLY_API_KEY` environment variable; set it
to an API key from your [Ably dashboard](https://ably.com/accounts/any/apps/any/app_keys). The
following code connects, subscribes to a channel, publishes a message to it and waits for the
message to arrive.

```csharp
using System;
using System.Threading.Tasks;
using Ably.PubSub;
using Ably.PubSub.Realtime;
using Ably.PubSub.Server;

var ablyApiKey = Environment.GetEnvironmentVariable("ABLY_API_KEY");
PubSubRealtimeClient realtime = PubSubServer.CreateRealtimeClient(ablyApiKey);

// Subscribing attaches the channel. A message published while connecting is queued.
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

To publish over HTTP without a realtime connection, create an HTTP client instead:

```csharp
using System;
using Ably.PubSub;
using Ably.PubSub.Server;

var ablyApiKey = Environment.GetEnvironmentVariable("ABLY_API_KEY");
PubSubHttpClient http = PubSubServer.CreateHttpClient(ablyApiKey);
await http.Channels.Get("test-channel").PublishAsync("test-event", "hello world");
```

> [!IMPORTANT]
> Server clients must authenticate with an API key. To use a token instead, see [Server clients and token authentication](https://ably.com/docs/auth/token#server-token-auth).

The following endpoint issues token requests. Devices set `AuthUrl` to its URL, for example
`https://your-server.example.com/ably-auth`:

```csharp
using System;
using Ably.PubSub;
using Ably.PubSub.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var ablyApiKey = Environment.GetEnvironmentVariable("ABLY_API_KEY");
PubSubHttpClient http = PubSubServer.CreateHttpClient(ablyApiKey);

var app = WebApplication.CreateBuilder(args).Build();

app.MapGet("/ably-auth", async () =>
{
    // Authenticate the caller first. Grant only the operations this user needs.
    var userId = "me"; // the signed-in user's ID, from your session
    var capability = new Capability("{\"test-channel\":[\"publish\",\"subscribe\",\"history\"]}");

    string tokenRequest = await http.Auth.CreateTokenRequestAsync(new TokenParams
    {
        ClientId = userId,
        Capability = capability, // without it, the token gets all of the API key's capabilities
    });
    return Results.Content(tokenRequest, "application/json");
});

app.Run();
```

See [token authentication](https://ably.com/docs/auth/token) for how devices use it.

### Device

Authenticate a device with tokens issued by your own server, such as the endpoint above, through
`AuthUrl` or `AuthCallback`. Never embed an API key in an app that runs on an end-user device.

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

To get tokens from your own code instead of a URL, set `AuthCallback`. It can return a token
string, a `TokenDetails`, a `TokenRequest` or the JSON from `Auth.CreateTokenRequestAsync`.

The device package has no separate HTTP entry point. For HTTP calls only, set `AutoConnect = false`
and use the client's `HttpClient`.

## Migrating from 1.x

Version 2.0 splits the `ably.io` package in two and creates clients with factory methods.

| | 1.x | 2.0 |
|---|---|---|
| Package | `ably.io` | `Ably.PubSub.Server` or `Ably.PubSub.Device` |
| Namespace | `IO.Ably`, `IO.Ably.Rest` | `Ably.PubSub`, `Ably.PubSub.Http` |
| Realtime client | `new AblyRealtime(...)` | `PubSubServer.CreateRealtimeClient(...)` or `PubSubDevice.CreateClient(...)` |
| HTTP client | `new AblyRest(...)` | `PubSubServer.CreateHttpClient(...)`, or a device client's `HttpClient` |

See [UPDATING.md](https://github.com/ably/ably-pubsub-dotnet/blob/main/UPDATING.md) for all other
changes.

## Common configuration

Every entry point accepts an API key or token string, a `ClientOptions` object or an
`Action<ClientOptions>`.

### Endpoint

`Endpoint` selects the Ably host for HTTP requests and the realtime connection. It replaces the
1.x `Environment`, `RestHost` and `RealtimeHost` options.

```csharp
var ablyApiKey = Environment.GetEnvironmentVariable("ABLY_API_KEY");
PubSubRealtimeClient realtime = PubSubServer.CreateRealtimeClient(options =>
{
    options.Key = ablyApiKey;
    options.Endpoint = "nonprod:sandbox";
});
```

- Never put a port in `Endpoint`: `localhost:8080` is not a hostname and does not resolve.
- Set the port with `TlsPort`, or with `Port` when TLS is off.
- With TLS off (`Tls = false`), use token authentication.

See [UPDATING.md](https://github.com/ably/ably-pubsub-dotnet/blob/main/UPDATING.md#choose-the-host-with-endpoint)
for how the old options map to `Endpoint`.

### Proxy

The SDK uses your runtime's default proxy settings for HTTP requests and the realtime connection.
On .NET, these come from
[`HttpClient.DefaultProxy`](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.defaultproxy).

To set a proxy in code, pass your own `HttpClient`. It applies to HTTP requests only. The realtime
connection still uses the default proxy settings.

```csharp
using System;
using System.Net;
using System.Net.Http;
using Ably.PubSub;
using Ably.PubSub.Server;

var handler = new HttpClientHandler
{
    Proxy = new WebProxy("http://proxy.example.com:8080"),
    UseProxy = true,
};

var ablyApiKey = Environment.GetEnvironmentVariable("ABLY_API_KEY");
PubSubHttpClient http = PubSubServer.CreateHttpClient(options =>
{
    options.Key = ablyApiKey;
    options.HttpClient = new HttpClient(handler);
});
```

### Logging

Set `LogLevel` to `LogLevel.Debug`, `Warning` (the default), `Error` or `None`. The SDK writes log
lines to the `ILoggerSink` you set as `LogHandler`. Without one, log lines are not written anywhere.

```csharp
using System;
using Ably.PubSub;
using Ably.PubSub.Server;

var ablyApiKey = Environment.GetEnvironmentVariable("ABLY_API_KEY");
PubSubRealtimeClient realtime = PubSubServer.CreateRealtimeClient(options =>
{
    options.Key = ablyApiKey;
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

All clients in a process share one logger, so give every client the same logging options.

## Platform notes

### Unity

Download `ably.pubsub.<version>.unitypackage` from the
[latest GitHub release](https://github.com/ably/ably-pubsub-dotnet/releases/latest). Import it with
**Assets > Import Package > Custom Package**.

- Use Unity 2019.x or later, with the API Compatibility Level set to .NET Standard 2.0.
- The package bundles `Ably.PubSub.Device`. Create clients with `PubSubDevice.CreateClient(...)`.
- Set `ClientOptions.CustomContext` to run callbacks on the main thread.

See the [Unity README](https://github.com/ably/ably-pubsub-dotnet/blob/main/unity/README.md).

### MAUI and assembly trimming

The SDK uses reflection. In a MAUI or trimmed app, keep the Ably assembly from being trimmed by
adding this to your `.csproj`:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="Ably.PubSub.Core" />
</ItemGroup>
```

### .NET Framework

On .NET Framework, for messages larger than 50 KB, set both WebSocket buffers to the 64 KB maximum:

```csharp
using System;
using Ably.PubSub;
using Ably.PubSub.Server;
using Ably.PubSub.Transport;

var ablyApiKey = Environment.GetEnvironmentVariable("ABLY_API_KEY");
var options = new ClientOptions(ablyApiKey)
{
    TransportFactory = new MsWebSocketTransport.TransportFactory(new MsWebSocketOptions
    {
        SendBufferInBytes = 64 * 1024,
        ReceiveBufferInBytes = 64 * 1024,
    }),
};

PubSubRealtimeClient realtime = PubSubServer.CreateRealtimeClient(options);
```

## Known limitations

- Device push-receive (push activation on Android or iOS) is not available. `Push.Activate()`
  throws an `AblyException`. See
  [Push notifications](https://github.com/ably/ably-pubsub-dotnet/blob/main/UPDATING.md#push-notifications).
  To request the feature, comment on [issue 1360](https://github.com/ably/ably-pubsub-dotnet/issues/1360).
- Push administration (`Push.Admin`) needs the `push-admin` capability. Use it from a backend
  built on `Ably.PubSub.Server`.
- Unity WebGL builds are not supported ([issue 1211](https://github.com/ably/ably-pubsub-dotnet/issues/1211)).

## Further reading

- [Getting started with Pub/Sub using .NET](https://ably.com/docs/getting-started/dotnet)
- [Ably Pub/Sub documentation](https://ably.com/docs/basics) and [examples](https://ably.com/examples?product=pubsub)
- [Token authentication](https://ably.com/docs/auth/token)
- [UPDATING.md](https://github.com/ably/ably-pubsub-dotnet/blob/main/UPDATING.md): migrating from 1.x
- [CHANGELOG.md](https://github.com/ably/ably-pubsub-dotnet/blob/main/CHANGELOG.md): release notes; all Ably releases are listed on [changelog.ably.com](https://changelog.ably.com)

## Contributing

Read [CONTRIBUTING.md](https://github.com/ably/ably-pubsub-dotnet/blob/main/CONTRIBUTING.md)
before contributing; it also describes the release process.

## Support, feedback and troubleshooting

For help, visit Ably's [support page](https://ably.com/support). Report bugs and compatibility
problems on [GitHub Issues](https://github.com/ably/ably-pubsub-dotnet/issues).

## License

Licensed under the Apache License, Version 2.0. See
[LICENSE](https://github.com/ably/ably-pubsub-dotnet/blob/main/LICENSE).
