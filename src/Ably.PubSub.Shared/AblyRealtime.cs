using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IO.Ably.MessageEncoders;
using IO.Ably.Push;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Transport;
using Newtonsoft.Json.Linq;

namespace IO.Ably
{
    /// <summary>
    /// AblyRealtime
    /// The top-level class for the Ably Realtime library.
    /// </summary>
    public class AblyRealtime : IRealtimeClient, IDisposable
    {
        private SynchronizationContext _synchronizationContext;

        internal ILogger Logger { get; set; } = DefaultLogger.LoggerInstance;

        internal RealtimeWorkflow Workflow { get; private set; }

        internal volatile bool Disposed;
        private static readonly Func<ClientOptions, IMobileDevice, AblyRest> CreateRestFunc = (clientOptions, mobileDevice) => new AblyRest(clientOptions, mobileDevice);

        /// <summary>
        /// Initializes a new instance of the <see cref="AblyRealtime"/> class with an ably key.
        /// Prefer <c>PubSubDevice.CreateClient</c> or <c>PubSubServer.CreateRealtimeClient</c> on
        /// the device/server packages: a client constructed directly here is not classified as
        /// device-side or server-side.
        /// </summary>
        /// <remarks>
        /// The device/server classification is an agent entry the door factories stamp onto the
        /// options; Ably's platform behaviour and billing depend on it, and an unclassified
        /// client will be rejected once MAU-based pricing is live.
        /// </remarks>
        /// <param name="key">String key (obtained from application dashboard).</param>
        public AblyRealtime(string key)
            : this(new ClientOptions(key))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AblyRealtime"/> class with the given options.
        /// Prefer <c>PubSubDevice.CreateClient</c> or <c>PubSubServer.CreateRealtimeClient</c> on
        /// the device/server packages: a client constructed directly here is not classified as
        /// device-side or server-side.
        /// </summary>
        /// <remarks>
        /// The device/server classification is an agent entry the door factories stamp onto the
        /// options; Ably's platform behaviour and billing depend on it, and an unclassified
        /// client will be rejected once MAU-based pricing is live.
        /// </remarks>
        /// <param name="options"><see cref="ClientOptions"/>.</param>
        public AblyRealtime(ClientOptions options)
            : this(options, CreateRestFunc, IoC.MobileDevice)
        {
        }

        internal AblyRealtime(ClientOptions options, IMobileDevice mobileDevice)
            : this(options, CreateRestFunc, mobileDevice)
        {
        }

        internal AblyRealtime(ClientOptions options, Func<ClientOptions, IMobileDevice, AblyRest> createRestFunc, IMobileDevice mobileDevice = null)
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
            RestClient = createRestFunc != null ? createRestFunc.Invoke(options, mobileDevice) : new AblyRest(options, mobileDevice);
            Push = new PushRealtime(RestClient, Logger);

            Connection = new Connection(this, options.NowFunc, Logger);
            Connection.Initialise();

            if (options.AutomaticNetworkStateMonitoring)
            {
                IoC.RegisterOsNetworkStateChanged();
            }

            Channels = new RealtimeChannels(this, Connection, mobileDevice);
            RestClient.AblyAuth.OnAuthUpdated = ConnectionManager.OnAuthUpdated;

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
            else if (options.CaptureCurrentSynchronizationContext)
            {
                _synchronizationContext = SynchronizationContext.Current;
            }
        }

        /// <summary>
        /// Gets the initialised RestClient.
        /// </summary>
        public AblyRest RestClient { get; }

        internal MessageHandler MessageHandler => RestClient.MessageHandler;

        /// <inheritdoc/>
        public IAblyAuth Auth => RestClient.AblyAuth;

        /// <inheritdoc/>
        public PushRealtime Push { get; }

        /// <inheritdoc/>
        public string ClientId => Auth.ClientId;

        internal ClientOptions Options => RestClient.Options;

        internal ConnectionManager ConnectionManager => Connection.ConnectionManager;

        /// <inheritdoc/>
        public RealtimeChannels Channels { get; private set; }

        /// <inheritdoc/>
        public Connection Connection { get; }

        internal RealtimeState State { get; }

        /// <summary>
        /// The local device instance represents the current state of the device in respect of it being a target for push notifications.
        /// </summary>
        public LocalDevice Device => RestClient.Device;

        /// <inheritdoc/>
        public Task<PaginatedResult<Stats>> StatsAsync()
        {
            return RestClient.StatsAsync();
        }

        /// <inheritdoc/>
        public Task<PaginatedResult<Stats>> StatsAsync(StatsRequestParams query)
        {
            return RestClient.StatsAsync(query);
        }

        /// <inheritdoc/>
        public PaginatedResult<Stats> Stats()
        {
            return RestClient.Stats();
        }

        /// <inheritdoc/>
        public PaginatedResult<Stats> Stats(StatsRequestParams query)
        {
            return RestClient.Stats(query);
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
            return RestClient.TimeAsync();
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

            Workflow.QueueCommand(DisposeCommand.Create().TriggeredBy($"AblyRealtime.Dispose({disposing}"));

            Disposed = true;
        }
    }
}
