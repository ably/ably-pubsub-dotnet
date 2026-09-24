using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.MessageEncoders;
using Ably.PubSub.Push;
using Ably.PubSub.Realtime;
using Ably.PubSub.Realtime.Workflow;
using Ably.PubSub.Transport;
using Newtonsoft.Json.Linq;

namespace Ably.PubSub
{
    /// <summary>
    /// PubSubRealtimeClient
    /// The top-level class for the Ably Realtime library.
    /// </summary>
    public class PubSubRealtimeClient : IPubSubRealtimeClient, IDisposable
    {
        private SynchronizationContext _synchronizationContext;

        internal ILogger Logger { get; set; } = DefaultLogger.LoggerInstance;

        internal RealtimeWorkflow Workflow { get; private set; }

        internal volatile bool Disposed;
        private static readonly Func<ClientOptions, IMobileDevice, PubSubHttpClient> CreateRestFunc = (clientOptions, mobileDevice) => new PubSubHttpClient(clientOptions, mobileDevice);

        /// <summary>
        /// Initializes a new instance of the <see cref="PubSubRealtimeClient"/> class with an ably key.
        /// Not public: application code obtains a client from <c>PubSubDevice.CreateClient</c> or
        /// <c>PubSubServer.CreateRealtimeClient</c>, which stamp the device/server classification
        /// the platform and MAU-based billing depend on. The door assemblies (and this SDK's own
        /// tests) reach this constructor via <c>InternalsVisibleTo</c>.
        /// </summary>
        /// <param name="key">String key (obtained from application dashboard).</param>
        internal PubSubRealtimeClient(string key)
            : this(new ClientOptions(key))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PubSubRealtimeClient"/> class with the given options.
        /// Not public: application code obtains a client from <c>PubSubDevice.CreateClient</c> or
        /// <c>PubSubServer.CreateRealtimeClient</c>, which stamp the device/server classification
        /// the platform and MAU-based billing depend on. The door assemblies (and this SDK's own
        /// tests) reach this constructor via <c>InternalsVisibleTo</c>.
        /// </summary>
        /// <param name="options"><see cref="ClientOptions"/>.</param>
        internal PubSubRealtimeClient(ClientOptions options)
            : this(options, CreateRestFunc, IoC.MobileDevice)
        {
        }

        internal PubSubRealtimeClient(ClientOptions options, IMobileDevice mobileDevice)
            : this(options, CreateRestFunc, mobileDevice)
        {
        }

        internal PubSubRealtimeClient(ClientOptions options, Func<ClientOptions, IMobileDevice, PubSubHttpClient> createRestFunc, IMobileDevice mobileDevice = null)
        {
            if (options.Logger != null)
            {
                Logger = options.Logger;
            }

            Logger.LogLevel = options.LogLevel;

            if (options.LogHandler != null)
            {
                Logger.LoggerSink = options.LogHandler;
            }

            CaptureSynchronizationContext(options);
            HttpClient = createRestFunc != null ? createRestFunc.Invoke(options, mobileDevice) : new PubSubHttpClient(options, mobileDevice);
            Push = new PushRealtime(HttpClient, Logger);

            Connection = new Connection(this, options.NowFunc, Logger);
            Connection.Initialise();

            if (options.AutomaticNetworkStateMonitoring)
            {
                IoC.RegisterOsNetworkStateChanged();
            }

            Channels = new RealtimeChannels(this, Connection, mobileDevice);
            HttpClient.AblyAuth.OnAuthUpdated = ConnectionManager.OnAuthUpdated;

            State = new RealtimeState(options.GetFallbackHosts()?.Shuffle().ToList(), options.NowFunc);

            Workflow = new RealtimeWorkflow(this, Logger);
            Workflow.Start();

            if (options.AutoConnect)
            {
                Connect();
            }
        }

        private void CaptureSynchronizationContext(ClientOptions options)
        {
            if (options.CustomContext != null)
            {
                _synchronizationContext = options.CustomContext;
            }
        }

        /// <summary>
        /// Gets the initialised HttpClient.
        /// </summary>
        public PubSubHttpClient HttpClient { get; }

        internal MessageHandler MessageHandler => HttpClient.MessageHandler;

        /// <inheritdoc/>
        public IAblyAuth Auth => HttpClient.AblyAuth;

        /// <inheritdoc/>
        public PushRealtime Push { get; }

        /// <inheritdoc/>
        public string ClientId => Auth.ClientId;

        internal ClientOptions Options => HttpClient.Options;

        internal ConnectionManager ConnectionManager => Connection.ConnectionManager;

        /// <inheritdoc/>
        public RealtimeChannels Channels { get; private set; }

        /// <inheritdoc/>
        public Connection Connection { get; }

        internal RealtimeState State { get; }

        /// <summary>
        /// The local device instance represents the current state of the device in respect of it being a target for push notifications.
        /// </summary>
        public LocalDevice Device => HttpClient.Device;

        /// <inheritdoc/>
        public Task<PaginatedResult<Stats>> StatsAsync()
        {
            return HttpClient.StatsAsync();
        }

        /// <inheritdoc/>
        public Task<PaginatedResult<Stats>> StatsAsync(StatsRequestParams query)
        {
            return HttpClient.StatsAsync(query);
        }

        /// <inheritdoc/>
        public PaginatedResult<Stats> Stats()
        {
            return HttpClient.Stats();
        }

        /// <inheritdoc/>
        public PaginatedResult<Stats> Stats(StatsRequestParams query)
        {
            return HttpClient.Stats(query);
        }

        /// <inheritdoc/>
        public void Connect()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException("This instance has been disposed. Please create a new one.");
            }

            Connection.Connect();
        }

        /// <inheritdoc/>
        public void Close()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException("This instance has been disposed. Please create a new one.");
            }

            Connection.Close();
        }

        /// <inheritdoc/>
        public Task<DateTimeOffset> TimeAsync()
        {
            return HttpClient.TimeAsync();
        }

        internal void NotifyExternalClients(Action action)
        {
            var context = Volatile.Read(ref _synchronizationContext);
            if (context != null)
            {
                context.Post(delegate { action(); }, null);
            }
            else
            {
                action();
            }
        }

        /// <summary>
        /// Debug method to get the full library state.
        /// Useful when trying to figure out the full state of the library.
        /// </summary>
        /// <returns>json object of the full state of the library.</returns>
        public string GetCurrentState()
        {
            var result = new JObject
            {
                ["options"] = JObject.FromObject(Options),
                ["state"] = State.WhatDoIHave(),
                ["channels"] = Channels.GetCurrentState(),
                ["isDisposed"] = Disposed,
            };
            return result.ToString();
        }

        /// <summary>
        /// Disposes the current instance.
        /// Once disposed, it closes the connection and the library can't be used again.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Disposes the current instance.
        /// Once disposed, it closes the connection and the library can't be used again.
        /// </summary>
        /// <param name="disposing">Whether the dispose method triggered it directly.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (Disposed)
            {
                return;
            }

            if (disposing)
            {
                try
                {
                    Connection?.RemoveAllListeners();
                    Channels?.CleanupChannels();
                    Push.Dispose();
                }
                catch (Exception e)
                {
                    Logger.Error("Error disposing Ably Realtime", e);
                }
            }

            Workflow.QueueCommand(DisposeCommand.Create().TriggeredBy($"PubSubRealtimeClient.Dispose({disposing}"));

            Disposed = true;
        }
    }
}
