using System;
using System.Collections.Generic;

namespace IO.Ably.PubSub.Internal
{
    /// <summary>
    /// Private helper shared by Ably.PubSub.Device and Ably.PubSub.Server. It is compiled into
    /// each door assembly by a <c>&lt;Compile Include&gt;</c> item rather than published, so the two
    /// packages can share this code without a third NuGet package existing for it to live in.
    /// This is the .NET analogue of ably-js's <c>packages/shared/side.ts</c> and ably-java's
    /// <c>shared/.../Side.java</c>.
    /// <para>
    /// PDR-091 keeps Ably.PubSub.Core itself as the shared core, so nothing here may grow into a
    /// general abstraction over the core: it exists only to stamp the side a package declares.
    /// </para>
    /// </summary>
    internal static class Side
    {
        // The `-device` / `-server` suffix on both identifiers below is load-bearing, not
        // cosmetic. On API-key auth the realtime system grants the MAU server exemption by
        // matching an agent entry ending in `-server`, and an identifier that is not yet in the
        // ably-common registry is classified by that suffix alone. Renaming either without
        // preserving its suffix silently reclassifies every client the package constructs.
        //
        // Both live here rather than in the package that uses each, so the naming scheme can be
        // changed in one place.

        /// <summary>
        /// The agent identifier declaring the device side, sent by Ably.PubSub.Device.
        /// </summary>
        internal const string DeviceAgentIdentifier = "ably-pubsub-device";

        /// <summary>
        /// The agent identifier declaring the server side, sent by Ably.PubSub.Server.
        /// <para>
        /// This is the entry that earns the MAU exemption on API-key auth, so its <c>-server</c>
        /// suffix is the one with billing consequences.
        /// </para>
        /// </summary>
        internal const string ServerAgentIdentifier = "ably-pubsub-server";

        /// <summary>
        /// Returns a copy of the caller's options carrying the agent entry that declares this
        /// package's side.
        /// <para>
        /// The core stores the options object it is given <b>by reference</b>, so stamping the
        /// side onto the caller's own instance would leak the flag into any other client built
        /// from the same options — both doors reused on one options object, or a plain
        /// <c>new PubSubRealtimeClient(options)</c>. To prevent that, the options are copied via
        /// <see cref="ClientOptions.Clone()"/> and the side is stamped onto the copy; the caller's
        /// instance and its <c>Agents</c> dictionary are left untouched. The copy lives in the core
        /// (not here) because <c>RestHost</c>/<c>RealtimeHost</c>/<c>FallbackHosts</c> are
        /// write-only from outside the class and cannot be carried across by a copy located here.
        /// </para>
        /// <para>
        /// The caller's <c>Agents</c> entries are preserved alongside the side stamp, so an SDK
        /// layered on top of this package keeps its attribution. The side stamp is applied last
        /// and so wins a collision on its own identifier: which side the package declares is the
        /// package's to state, not the caller's to redefine.
        /// </para>
        /// <para>
        /// The stamp is deliberately <b>versionless</b> — a bare <c>ably-pubsub-server</c> token
        /// rather than <c>ably-pubsub-server/2.0.0</c>. A version on the flag would say
        /// version-of-what: the flag is a cross-SDK statement about where the code runs, the
        /// door package is released in lockstep with the core, whose version the family
        /// identifier (<c>ably-pubsub-dotnet/&lt;version&gt;</c>) already carries, and the
        /// ably-common registry models the flags like <c>browser</c>, with
        /// <c>versioned: false</c>. See ably-js#2297. <c>Agent.AddAgentIdentifier</c> in the core
        /// already emits a bare token for a null or empty version, so a null value here is all
        /// that is needed.
        /// </para>
        /// </summary>
        /// <param name="options"> The options the caller passed to the door. </param>
        /// <param name="identifier"> The side-declaring agent identifier to stamp. </param>
        /// <returns> A copy of the options, with the side stamped into its <c>Agents</c> dictionary. </returns>
        /// <exception cref="ArgumentNullException"> Thrown when <paramref name="options"/> is null. </exception>
        internal static ClientOptions WithSideAgent(ClientOptions options, string identifier)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            // Copy before stamping: the core keeps the options object by reference, so mutating the
            // caller's instance would let this side flag leak into another client built from the
            // same options. Clone() already gives Agents its own dictionary.
            var copy = options.Clone();

            if (copy.Agents == null)
            {
                copy.Agents = new Dictionary<string, string>();
            }

            // Applied last, so the side wins a collision on its own key. Null value => bare
            // token, per the versionless-flag reasoning above.
            copy.Agents[identifier] = null;

            return copy;
        }

        /// <summary>
        /// Builds a fresh <see cref="ClientOptions"/> configured by the supplied action. The core
        /// clients have no such constructor overload, so the doors provide one uniformly.
        /// </summary>
        /// <param name="configure"> Action that populates the new options. </param>
        /// <returns> The populated options. </returns>
        /// <exception cref="ArgumentNullException"> Thrown when <paramref name="configure"/> is null. </exception>
        internal static ClientOptions Configure(Action<ClientOptions> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new ClientOptions();
            configure(options);

            return options;
        }
    }
}
