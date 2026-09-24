using System;
using Ably.PubSub.Internal;

namespace Ably.PubSub.Device
{
    /// <summary>
    /// Entry point for Ably Pub/Sub applications that run on an end-user device - a mobile or
    /// desktop app, a Unity game, a set-top box or any other client the end user holds.
    /// <para>
    /// This factory is the only supported way to create a client from this package. The
    /// Ably.PubSub.Core package that carries the implementation is an internal dependency: a
    /// client constructed directly from <see cref="PubSubRealtimeClient"/> or <see cref="PubSubHttpClient"/> is
    /// not classified as device-side, which Ably's platform behaviour and billing depend on.
    /// </para>
    /// <para>
    /// There is one door by design, and it returns the ordinary <see cref="PubSubRealtimeClient"/>, so
    /// the whole of the <c>Ably.PubSub</c> API remains available. Device-side connectionless
    /// operations - message history, presence reads, token requests and
    /// <c>Request</c>/<c>RequestV2</c> - are available on that client through its
    /// <see cref="PubSubRealtimeClient.HttpClient"/> and channel APIs, so per PDR-091 there is
    /// deliberately no device HTTP door.
    /// </para>
    /// <para>
    /// The API-key overload is kept on purpose: per PDR-091, device-side API keys stay allowed
    /// at launch and enforcement is server-side. Token authentication remains the recommended
    /// scheme for anything the end user holds.
    /// </para>
    /// </summary>
    public static class PubSubDevice
    {
        /// <summary>
        /// Creates a realtime client for an end-user device from an API key or an Ably token.
        /// </summary>
        /// <param name="keyOrToken">
        /// A valid Ably API key (of the form <c>APP_ID.KEY_ID:KEY_SECRET</c>) or an Ably token.
        /// The core applies the same colon rule its own constructors use to tell them apart.
        /// </param>
        /// <returns> A connected-on-demand realtime client. </returns>
        public static PubSubRealtimeClient CreateClient(string keyOrToken)
        {
            return CreateClient(new ClientOptions(keyOrToken));
        }

        /// <summary>
        /// Creates a realtime client for an end-user device from a set of client options.
        /// </summary>
        /// <param name="options"> The client options. </param>
        /// <returns> A connected-on-demand realtime client. </returns>
        /// <exception cref="ArgumentNullException"> Thrown when <paramref name="options"/> is null. </exception>
        public static PubSubRealtimeClient CreateClient(ClientOptions options)
        {
            // The core constructor is internal; this door is a sanctioned caller (granted access
            // via InternalsVisibleTo), constructing the client from the side-stamped copy.
            return new PubSubRealtimeClient(Side.WithSideAgent(options, Side.DeviceAgentIdentifier));
        }

        /// <summary>
        /// Creates a realtime client for an end-user device, configured by the supplied action.
        /// </summary>
        /// <param name="configure"> Action that populates the client options. </param>
        /// <returns> A connected-on-demand realtime client. </returns>
        /// <exception cref="ArgumentNullException"> Thrown when <paramref name="configure"/> is null. </exception>
        public static PubSubRealtimeClient CreateClient(Action<ClientOptions> configure)
        {
            return CreateClient(Side.Configure(configure));
        }
    }
}
