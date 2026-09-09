![Ably Pub/Sub Dotnet Header](images/NETSDK-github.png)
[![NuGet version](https://badge.fury.io/nu/ably.io.svg)](https://www.nuget.org/packages/ably.io)
[![License](https://badgen.net/github/license/ably/ably-pubsub-dotnet)](https://github.com/ably/ably-pubsub-dotnet/blob/main/LICENSE)

# Ably Pub/Sub .NET SDK

Build any realtime experience using Ably’s Pub/Sub .NET SDK. Supported on all popular platforms and frameworks, including Unity and MAUI.

Ably Pub/Sub provides flexible APIs that deliver features such as pub-sub messaging, message history, presence, and push notifications. Utilizing Ably’s realtime messaging platform, applications benefit from its highly performant, reliable, and scalable infrastructure.

Find out more:

* [Ably Pub/Sub docs.](https://ably.com/docs/basics)
* [Ably Pub/Sub examples.](https://ably.com/examples?product=pubsub)

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
| Xamarin.Android | 8.0+ |
| Xamarin.iOS | 10.14+ |
| Xamarin.Mac| 3.8+ |
| Unity | 2019.x+ |
| MAUI | .NET 6.0+|

> [!IMPORTANT]
> SDK versions < 1.2.12 will be [deprecated](https://ably.com/docs/platform/deprecate/protocol-v1) from November 1, 2025.

---

## Installation

The SDK is available as a [nuget package](https://www.nuget.org/packages/ably.io/). To get started with your project, install the package from the Package Manager Console or the .NET CLI.

Package Manager Console:

```shell
PM> Install-Package ably.io
```

.NET CLI in your project directory:

```shell
dotnet add package ably.io
```

### MAUI configuration

When using Ably in a MAUI project, be aware of potential issues caused by assembly trimming, as `ably-pubsub-dotnet` relies on the reflection API. 

Add the following to your `.csproj` file to prevent trimming of the `IO.Ably` assembly:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="IO.Ably" />
</ItemGroup>
```

---

## Usage

The following code connects to Ably's realtime messaging service, subscribes to a channel to receive messages, and publishes a test message to that same channel:

```csharp
// Initialize Ably Realtime client
var realtime = new AblyRealtime("your-ably-api-key");

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

- Download latest `ably.io.*.unitypackage` from [releases section](https://github.com/ably/ably-pubsub-dotnet/releases) and include it in the unity project.
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

var realtime = new AblyRealtime(options);
```
