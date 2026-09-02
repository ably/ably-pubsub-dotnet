using System;
using IO.Ably.PubSub.Internal;

namespace IO.Ably.PubSub.Server
{
    /// <summary>
    /// Entry point for Ably Pub/Sub applications that run on a server - an ASP.NET or Azure host,
    /// a worker, a console app or any other backend the end user does not hold.
    /// <para>
    /// These factory methods are the only supported way to create a client from this package.
    /// The Ably.PubSub.Core package that carries the implementation is an internal dependency: a
    /// client constructed directly from <see cref="AblyRealtime"/> or <see cref="AblyRest"/> is
    /// not classified as server-side, which Ably's platform behaviour and billing depend on.
    /// </para>
    /// <para>
    /// The returned clients are the ordinary <see cref="AblyRealtime"/> and
    /// <see cref="AblyRest"/>, so the whole of the <c>IO.Ably</c> API remains available;
    /// installing this package rather than Ably.PubSub.Device states where the code runs. What
    /// the doors add is an agent entry declaring the server side, and per PDR-091 that entry is
    /// what earns the monthly-active-user exemption on API-key authentication.
    /// </para>
    /// </summary>
    public static class PubSubServer
    {
        /// <summary>
        /// Creates a server-side realtime client from an API key or an Ably token.
        /// </summary>
        /// <param name="keyOrToken">
        /// A valid Ably API key (of the form <c>APP_ID.KEY_ID:KEY_SECRET</c>) or an Ably token.
        /// The core applies the same colon rule its own constructors use to tell them apart.
        /// </param>
        /// <returns> A connected-on-demand realtime client. </returns>
        public static AblyRealtime CreateRealtimeClient(string keyOrToken)
        {
            return CreateRealtimeClient(new ClientOptions(keyOrToken));
        }

        /// <summary>
        /// Creates a server-side realtime client from a set of client options.
        /// </summary>
        /// <param name="options"> The client options. </param>
        /// <returns> A connected-on-demand realtime client. </returns>
        /// <exception cref="ArgumentNullException"> Thrown when <paramref name="options"/> is null. </exception>
        public static AblyRealtime CreateRealtimeClient(ClientOptions options)
        {
            return new AblyRealtime(Side.WithSideAgent(options, Side.ServerAgentIdentifier));
        }

        /// <summary>
        /// Creates a server-side realtime client, configured by the supplied action.
        /// </summary>
        /// <param name="configure"> Action that populates the client options. </param>
        /// <returns> A connected-on-demand realtime client. </returns>
        /// <exception cref="ArgumentNullException"> Thrown when <paramref name="configure"/> is null. </exception>
        public static AblyRealtime CreateRealtimeClient(Action<ClientOptions> configure)
        {
            return CreateRealtimeClient(Side.Configure(configure));
        }

        /// <summary>
        /// Creates a server-side HTTP (REST) client from an API key or an Ably token.
        /// </summary>
        /// <param name="keyOrToken">
        /// A valid Ably API key (of the form <c>APP_ID.KEY_ID:KEY_SECRET</c>) or an Ably token.
        /// The core applies the same colon rule its own constructors use to tell them apart.
        /// </param>
        /// <returns> An HTTP client. </returns>
        public static AblyRest CreateHttpClient(string keyOrToken)
        {
            return CreateHttpClient(new ClientOptions(keyOrToken));
        }

        /// <summary>
        /// Creates a server-side HTTP (REST) client from a set of client options.
        /// </summary>
        /// <param name="options"> The client options. </param>
        /// <returns> An HTTP client. </returns>
        /// <exception cref="ArgumentNullException"> Thrown when <paramref name="options"/> is null. </exception>
        public static AblyRest CreateHttpClient(ClientOptions options)
        {
            return new AblyRest(Side.WithSideAgent(options, Side.ServerAgentIdentifier));
        }

        /// <summary>
        /// Creates a server-side HTTP (REST) client, configured by the supplied action.
        /// </summary>
        /// <param name="configure"> Action that populates the client options. </param>
        /// <returns> An HTTP client. </returns>
        /// <exception cref="ArgumentNullException"> Thrown when <paramref name="configure"/> is null. </exception>
        public static AblyRest CreateHttpClient(Action<ClientOptions> configure)
        {
            return CreateHttpClient(Side.Configure(configure));
        }
    }
}
