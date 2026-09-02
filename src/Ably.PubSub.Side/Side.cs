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
        /// Returns the caller's options carrying the agent entry that declares this package's
        /// side.
        /// <para>
        /// Only <see cref="ClientOptions.Agents"/> is replaced, and it is replaced with a new
        /// dictionary rather than mutated, so the caller's own dictionary instance is left
        /// untouched and can be reused for another client. The options object itself is the one
        /// returned and therefore the one the core client consumes — there is no
        /// <c>ClientOptions.Clone()</c> in the core to copy onto, and adding one would put a
        /// core change on the critical path of the split; every option other than
        /// <c>Agents</c> is therefore shared with the object the caller passed in, which is the
        /// same treatment the core's own constructors give it.
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
        /// <returns> The same options instance, with a new <c>Agents</c> dictionary. </returns>
        /// <exception cref="ArgumentNullException"> Thrown when <paramref name="options"/> is null. </exception>
        internal static ClientOptions WithSideAgent(ClientOptions options, string identifier)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var agents = options.Agents == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(options.Agents);

            // Applied last, so the side wins a collision on its own key. Null value => bare
            // token, per the versionless-flag reasoning above.
            agents[identifier] = null;

            options.Agents = agents;

            return options;
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
