using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;

namespace Ably.PubSub.PackageConsumption
{
    /// <summary>
    /// Offline checks shared by the package-consumption apps. Each app references one door package
    /// the way a customer does, creates clients only through that door, and exercises code that
    /// crosses from the door into the core and through the core's internals: client construction,
    /// a channel lookup, a REST request and a realtime connection attempt. No Ably credentials or
    /// network access are needed: REST requests go to a handler that records the request and fails
    /// it, and the realtime connection targets a closed localhost port.
    /// </summary>
    internal static class ConsumptionChecks
    {
        private const string FakeKey = "appId.keyId:keySecret";

        public static async Task<int> RunAsync(string sideAgent, Assembly doorAssembly, Func<ClientOptions, PubSubRealtimeClient> createRealtimeClient, Func<ClientOptions, PubSubHttpClient> createHttpClient)
        {
            try
            {
                Console.WriteLine($"Runtime: {RuntimeName()} {Environment.Version} ({typeof(object).Assembly.Location})");
                Describe("Door", doorAssembly);
                Describe("Core", typeof(PubSubRealtimeClient).Assembly);

                await CheckRealtimeClientAsync(sideAgent, createRealtimeClient);

                if (createHttpClient != null)
                {
                    await CheckHttpClientAsync(sideAgent, createHttpClient);
                }

                Console.WriteLine("PACKAGE CONSUMPTION CHECKS PASSED");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("PACKAGE CONSUMPTION CHECKS FAILED");
                Console.WriteLine(ex);
                return 1;
            }
        }

        private static async Task CheckRealtimeClientAsync(string sideAgent, Func<ClientOptions, PubSubRealtimeClient> createRealtimeClient)
        {
            var handler = new RecordingHandler();

            // The door calls the core's internal client constructor here. A missing
            // InternalsVisibleTo grant for the door fails at this call with MethodAccessException.
            var client = createRealtimeClient(CreateOptions(handler));
            Pass("created the realtime client through the door");

            var channel = client.Channels.Get("package-consumption");
            Ensure(channel != null && channel.Name == "package-consumption", "Channels.Get returned an unexpected channel");
            Pass("Channels.Get");

            await ExpectAblyExceptionAsync("realtime client TimeAsync", () => client.TimeAsync());
            EnsureSideAgent(handler, sideAgent);
            Pass($"REST request carried the '{sideAgent}' agent");

            client.Connect();
            var state = await WaitForStateAsync(client, TimeSpan.FromSeconds(30), ConnectionState.Disconnected, ConnectionState.Suspended, ConnectionState.Failed);
            Pass($"realtime connection attempt against a closed port ended {state}");

            client.Close();
            await WaitForStateAsync(client, TimeSpan.FromSeconds(30), ConnectionState.Closed, ConnectionState.Failed);
            Pass("Close");
        }

        private static async Task CheckHttpClientAsync(string sideAgent, Func<ClientOptions, PubSubHttpClient> createHttpClient)
        {
            var handler = new RecordingHandler();

            var client = createHttpClient(CreateOptions(handler));
            Pass("created the HTTP client through the door");

            var channel = client.Channels.Get("package-consumption");
            Ensure(channel != null, "Channels.Get returned null");
            Pass("HTTP Channels.Get");

            await ExpectAblyExceptionAsync("HTTP client TimeAsync", () => client.TimeAsync());
            await ExpectAblyExceptionAsync("HTTP channel PublishAsync", () => channel.PublishAsync("event", "data"));
            EnsureSideAgent(handler, sideAgent);
            Pass($"HTTP client requests carried the '{sideAgent}' agent");
        }

        private static ClientOptions CreateOptions(RecordingHandler handler)
        {
            return new ClientOptions(FakeKey)
            {
                // A hostname endpoint has no fallback hosts, so nothing leaves the machine.
                Endpoint = "localhost",
                Tls = false,
                Port = GetClosedPort(),
                AutoConnect = false,
                HttpMaxRetryCount = 0,
                HttpOpenTimeout = TimeSpan.FromSeconds(5),
                HttpRequestTimeout = TimeSpan.FromSeconds(5),
                DisconnectedRetryTimeout = TimeSpan.FromSeconds(60),
                LogLevel = LogLevel.Error,
                HttpClient = new HttpClient(handler),
            };
        }

        private static async Task ExpectAblyExceptionAsync(string name, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (AblyException ex)
            {
                Pass($"{name} failed offline with AblyException {ex.ErrorInfo?.Code}");
                return;
            }

            throw new InvalidOperationException($"{name} succeeded, but every request in these checks is failed by the test handler");
        }

        private static void EnsureSideAgent(RecordingHandler handler, string sideAgent)
        {
            Ensure(handler.Agents.Count > 0, "no REST request reached the HTTP handler");
            foreach (var agent in handler.Agents)
            {
                var tokens = agent.Split(' ');
                Ensure(tokens.Contains(sideAgent), $"Ably-Agent '{agent}' does not declare '{sideAgent}'");
                Ensure(tokens.Any(t => t.StartsWith("ably-pubsub-dotnet/", StringComparison.Ordinal)), $"Ably-Agent '{agent}' has no ably-pubsub-dotnet/<version> entry");
            }
        }

        private static async Task<ConnectionState> WaitForStateAsync(PubSubRealtimeClient client, TimeSpan timeout, params ConnectionState[] states)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var state = client.Connection.State;
                if (states.Contains(state))
                {
                    return state;
                }

                await Task.Delay(100);
            }

            throw new TimeoutException($"connection stayed {client.Connection.State}; expected one of {string.Join(", ", states)} within {timeout}");
        }

        private static int GetClosedPort()
        {
            // Bind an ephemeral port and release it: nothing listens there afterwards, so a
            // connection attempt is refused straight away.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string RuntimeName()
        {
            if (Type.GetType("Mono.Runtime") != null)
            {
                return "Mono";
            }

            return Environment.Version.Major == 4 ? ".NET Framework" : ".NET";
        }

        private static void Describe(string label, Assembly assembly)
        {
            var name = assembly.GetName();
            var token = name.GetPublicKeyToken();
            var tokenText = token == null || token.Length == 0 ? "unsigned" : BitConverter.ToString(token).Replace("-", string.Empty).ToLowerInvariant();
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Console.WriteLine($"{label}: {name.Name} {informational} (PublicKeyToken={tokenText}) from {assembly.Location}");
        }

        private static void Ensure(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void Pass(string message)
        {
            Console.WriteLine($"  ok: {message}");
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public List<string> Agents { get; } = new List<string>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Headers.TryGetValues("Ably-Agent", out var values))
                {
                    lock (Agents)
                    {
                        Agents.Add(string.Join(" ", values));
                    }
                }

                throw new HttpRequestException("Package-consumption checks run offline; every REST request is failed here.");
            }
        }
    }
}
