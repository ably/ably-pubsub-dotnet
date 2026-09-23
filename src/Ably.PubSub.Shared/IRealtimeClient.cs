using System;
using System.Threading.Tasks;
using IO.Ably.Push;
using IO.Ably.Realtime;

namespace IO.Ably
{
    /// <summary>
    /// The top-level interface for the Ably Realtime library.
    /// </summary>
    public interface IRealtimeClient : IStatsCommands
    {
        /// <summary>
        /// Initiate a connection.
        /// </summary>
        void Connect();

        /// <summary>
        ///     This simply calls connection.close. Causes the connection to close, entering the closed state. Once
        ///     closed, the library will not attempt to re-establish the connection without a call to connect().
        /// </summary>
        void Close();

        /// <summary>
        /// Gets the initialised Auth.
        /// </summary>
        IAblyAuth Auth { get; }

        /// <summary>A reference to the connection object for this library instance.</summary>
        Connection Connection { get; }

        /// <summary>
        /// Methods for managing Push notifications subscriptions and device registrations.
        /// </summary>
        PushRealtime Push { get; }

        /// <summary>
        /// Current client id.
        /// </summary>
        string ClientId { get; }

        /// <summary>The collection of channels instanced, indexed by channel name.</summary>
        RealtimeChannels Channels { get; }

        /// <summary>
        /// The local device instance represents the current state of the device in respect of it being a target for push notifications.
        /// </summary>
        LocalDevice Device { get; }

        /// <summary>
        /// Retrieves the ably service time.
        /// </summary>
        /// <returns>returns current server time as DateTimeOffset.</returns>
        Task<DateTimeOffset> TimeAsync();
    }
}
