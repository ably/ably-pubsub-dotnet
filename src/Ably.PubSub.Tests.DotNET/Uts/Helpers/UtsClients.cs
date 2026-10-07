using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Push;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Push;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// The specs' <c>install_mock(m)</c> + <c>Rest(...)</c> / <c>Realtime(...)</c>, in one place.
    ///
    /// Every UTS unit test builds its client through here so that the seam wiring and the unit-tier
    /// defaults live in one file rather than being copied into ~95 test files.
    /// </summary>
    public static class UtsClients
    {
        /// <summary>The specs' <c>ClientOptions(key: "appId.keyId:keySecret")</c>.</summary>
        public const string ValidKey = "appId.keyId:keySecret";

        /// <summary>
        /// The deadline every wait here uses unless a test asks for another.
        ///
        /// Fifteen seconds, not five. Nothing in the unit tier legitimately takes anywhere near
        /// that - these are mocks - so the figure is not an expectation, it is headroom. The tier
        /// is ~900 tests running in parallel in Release, and a five-second deadline was measured
        /// losing to scheduling under that load about once every three full runs, on a test with
        /// nothing wrong with it. A genuinely stuck test still fails, ten seconds later.
        /// </summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Options wired to the mocks with unit-tier defaults. Callers override via <paramref name="configure"/>,
        /// which runs last so a spec can always have the last word.
        /// </summary>
        public static ClientOptions Options(
            MockHttpClient mockHttp = null,
            MockWebSocket mockWebSocket = null,
            TestClock clock = null,
            Action<ClientOptions> configure = null)
        {
            var options = new ClientOptions
            {
                Key = ValidKey,

                // The realtime specs drive the connection explicitly; only the specs that are *about*
                // auto-connect pass AutoConnect = true.
                AutoConnect = false,

                // Fallbacks are off unless a spec is about them: otherwise one injected 5xx fans out into
                // a request per host and every request count in the file is wrong.
                FallbackHosts = Array.Empty<string>(),

                // CanConnectToAbly() is reached from the realtime fallback path
                // (Realtime/AttemptsHelpers.cs:12). Left on, it either puts a surprise request into
                // CapturedRequests or — with no mock HTTP installed — reaches the real internet from a
                // unit test. Only the specs that are about connectivity checking turn it back on.
                SkipInternetCheck = true,

                // Off, because a unit test must not be driven by the host's network state. This
                // option defaults to *true* outside Unity, which subscribes the client to the
                // operating system's network-change events — and
                // ConnectionManager.HandleNetworkStateChange raises RTN20a's 80017 with
                // `retryInstantly: true` (ConnectionManager.cs:370-379). Measured on a developer
                // machine, that is enough to inject DISCONNECTED into an unrelated test and to make
                // the client reconnect without waiting for DisconnectedRetryTimeout, which showed up
                // as attempt counts that varied run to run and as a DISCONNECTED nobody asked for.
                // Only the RTN20 specs, which are about network state, turn it back on.
                AutomaticNetworkStateMonitoring = false,
            };

            if (mockHttp != null)
            {
                options.HttpClient = mockHttp.AsHttpClient();
            }

            if (mockWebSocket != null)
            {
                options.TransportFactory = mockWebSocket.TransportFactory;
            }

            if (clock != null)
            {
                options.NowFunc = clock.NowFunc;
                if (mockHttp != null)
                {
                    mockHttp.NowFunc = clock.NowFunc;
                }
            }

            configure?.Invoke(options);
            return options;
        }

        /// <summary>The specs' <c>Rest(options: ...)</c> on the HTTP seam.</summary>
        public static PubSubHttpClient RestClient(
            MockHttpClient mockHttp,
            TestClock clock = null,
            Action<ClientOptions> configure = null)
            => WithOwnDevice(new PubSubHttpClient(
                Options(mockHttp, clock: clock, configure: configure),
                new FakeMobileDevice()));

        /// <summary>
        /// Gives a client its own <c>LocalDevice</c>, so it neither reads nor writes the static
        /// <c>LocalDevice.Instance</c>.
        ///
        /// <para>
        /// This is D48 defence, and it is why even the plain <see cref="RestClient"/> is built with
        /// a mobile device. <c>PubSubHttpClient.OnAuthClientIdChanged</c> guards on the static
        /// <c>LocalDevice.IsLocalDeviceInitialized</c> and then dereferences the *instance*
        /// <c>Device</c>, so a client with no device throws a <c>NullReferenceException</c> out of
        /// <c>AuthorizeAsync</c> whenever some other client in the process has touched the static -
        /// and the repo's own push tests set and clear it while everything else is running.
        /// Measured: UTS auth tests failing in roughly one Release run in two, on nothing they do
        /// themselves.
        /// </para>
        ///
        /// <para>
        /// Nothing under test changes. The device is never read by an auth, channel or presence
        /// test; it only means <c>channel.Push</c> is non-null, which is the shape a real client on
        /// a device has anyway. <see cref="RestClientWithDevice"/> remains the way to control
        /// *which* device a push test presents.
        /// </para>
        /// </summary>
        private static PubSubHttpClient WithOwnDevice(PubSubHttpClient client)
        {
            client.Device = LocalDevice.Create(mobileDevice: new FakeMobileDevice());
            return client;
        }

        /// <summary>
        /// A REST client that believes it is running on a push-capable device, for the push specs'
        /// <c>client.device = LocalDevice(...)</c> setup.
        ///
        /// <para>
        /// <c>HttpChannel.Push</c> is null unless the client was built with an
        /// <c>IMobileDevice</c>, which a plain desktop client does not have. So the fake device from
        /// the repo's own test helpers is injected through the internal constructor, and the
        /// <c>LocalDevice</c> is assigned to <c>PubSubHttpClient.Device</c> — an internal setter
        /// documented as existing for exactly this.
        /// </para>
        /// </summary>
        /// <param name="mockHttp">The HTTP seam.</param>
        /// <param name="device">
        /// The local device to present, or null to leave whatever the fake device yields.
        /// </param>
        /// <param name="clock">An optional fake clock.</param>
        /// <param name="configure">Last-word option overrides.</param>
        /// <returns>A client whose channels expose a usable <c>Push</c>.</returns>
        public static PubSubHttpClient RestClientWithDevice(
            MockHttpClient mockHttp,
            LocalDevice device = null,
            TestClock clock = null,
            Action<ClientOptions> configure = null)
        {
            var mobileDevice = new FakeMobileDevice();
            var client = new PubSubHttpClient(
                Options(mockHttp, clock: clock, configure: configure),
                mobileDevice);

            // Always assigned, even when the caller did not supply one - see WithOwnDevice for
            // why no UTS client is allowed to reach the static LocalDevice.Instance.
            client.Device = device ?? LocalDevice.Create(mobileDevice: mobileDevice);

            return client;
        }

        /// <summary>The specs' <c>Realtime(options: ...)</c> on up to three seams.</summary>
        public static PubSubRealtimeClient RealtimeClient(
            MockWebSocket mockWebSocket,
            MockHttpClient mockHttp = null,
            TestClock clock = null,
            Action<ClientOptions> configure = null)
        {
            var client = new PubSubRealtimeClient(
                Options(mockHttp, mockWebSocket, clock, configure),
                new FakeMobileDevice());

            // The same D48 defence as the REST helper - see WithOwnDevice.
            client.HttpClient.Device = LocalDevice.Create(mobileDevice: new FakeMobileDevice());

            return client;
        }

        /// <summary>
        /// The specs' <c>AWAIT_STATE client.connection.state == X</c>.
        ///
        /// Not <c>async</c>, so the listener is registered before the method returns — a test can set the
        /// wait up and only then provoke the transition. Returns immediately when the state is already
        /// current, which is a trap for any spec whose point is a *fresh* entry into a state it already
        /// holds: "drop the connection and reconnect" asserts nothing if you wait for CONNECTED while
        /// still connected. Use <see cref="NextConnectionState"/> there.
        /// </summary>
        public static Task AwaitConnectionState(
            Connection connection,
            ConnectionState state,
            TimeSpan? timeout = null)
        {
            if (connection.State == state)
            {
                return Task.CompletedTask;
            }

            // Subscribe first, then look again. Checking and only then subscribing leaves a gap
            // the transition can slip through - measured as an intermittent "timed out waiting for
            // state X (current: X)", which is the contradiction that gives it away.
            var waiter = NextConnectionState(connection, state, timeout);

            return connection.State == state ? Task.CompletedTask : waiter;
        }

        /// <summary>Waits for the next *fresh* entry into a state, ignoring the state currently held.</summary>
        public static Task NextConnectionState(
            Connection connection,
            ConnectionState state,
            TimeSpan? timeout = null)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<ConnectionStateChange> listener = null;
            listener = change =>
            {
                if (change.Current == state)
                {
                    connection.Off(listener);
                    completion.TrySetResult(true);
                }
            };

            connection.On(listener);
            return WithDeadline(
                completion.Task,
                timeout,
                () => $"connection state {state} (current: {connection.State})");
        }

        public static Task AwaitChannelState(
            IRealtimeChannel channel,
            ChannelState state,
            TimeSpan? timeout = null)
        {
            if (channel.State == state)
            {
                return Task.CompletedTask;
            }

            // Subscribe first, then look again - see the note on AwaitConnectionState.
            var waiter = NextChannelState(channel, state, timeout);

            return channel.State == state ? Task.CompletedTask : waiter;
        }

        public static Task NextChannelState(
            IRealtimeChannel channel,
            ChannelState state,
            TimeSpan? timeout = null)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<ChannelStateChange> listener = null;
            listener = change =>
            {
                if (change.Current == state)
                {
                    channel.Off(listener);
                    completion.TrySetResult(true);
                }
            };

            channel.On(listener);
            return WithDeadline(
                completion.Task,
                timeout,
                () => $"channel '{channel.Name}' state {state} (current: {channel.State})");
        }

        /// <summary>
        /// The specs' <c>AWAIT UNTIL</c> / <c>poll_until</c>, for a premise no state transition captures —
        /// "a second connection attempt has been made", say. Spins on real loop time, so it cannot wait
        /// for anything only a <see cref="TestClock"/> advance would make true.
        /// </summary>
        public static async Task PollUntil(
            Func<bool> condition,
            string description,
            TimeSpan? timeout = null)
        {
            var deadline = DateTimeOffset.UtcNow.Add(timeout ?? DefaultTimeout);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(10).ConfigureAwait(false);
            }

            if (condition())
            {
                return;
            }

            throw new TimeoutException($"Timed out waiting for {description}.");
        }

        /// <summary>The specs' <c>CONTAINS_IN_ORDER</c>: every expected item appears, in this order.</summary>
        public static bool ContainsInOrder<T>(IEnumerable<T> observed, params T[] expected)
        {
            var index = 0;
            foreach (var item in observed)
            {
                if (index < expected.Length && Equals(item, expected[index]))
                {
                    index++;
                }
            }

            return index == expected.Length;
        }

        /// <summary>
        /// Records every connection state the client enters, from before it is connected. This is the
        /// shape <c>mock_websocket.md</c> prescribes for transient states: a rapid
        /// DISCONNECTED → CONNECTING cannot be caught by waiting for it, but it is always in the record.
        /// </summary>
        public static List<ConnectionState> RecordConnectionStates(Connection connection)
        {
            var states = new List<ConnectionState>();
            connection.On(change =>
            {
                lock (states)
                {
                    states.Add(change.Current);
                }
            });

            return states;
        }

        /// <summary>
        /// Records the full state changes, not just the states, so a test can assert on the
        /// <c>Reason</c> that accompanied a particular transition.
        ///
        /// Prefer this over sampling <c>Connection.ErrorReason</c> after a wait. The property holds
        /// whichever error came with the *most recent* transition, and several spec scenarios produce two
        /// in quick succession — a server ERROR followed by the server closing the socket, say — so a
        /// later transition can legitimately replace the error the test is about.
        /// </summary>
        public static List<ConnectionStateChange> RecordConnectionStateChanges(Connection connection)
        {
            var changes = new List<ConnectionStateChange>();
            connection.On(change =>
            {
                lock (changes)
                {
                    changes.Add(change);
                }
            });

            return changes;
        }

        public static List<ChannelState> RecordChannelStates(IRealtimeChannel channel)
        {
            var states = new List<ChannelState>();
            channel.On(change =>
            {
                lock (states)
                {
                    states.Add(change.Current);
                }
            });

            return states;
        }

        /// <summary>
        /// The specs' <c>simulate_network_lost()</c> / <c>simulate_network_available()</c>, applied to
        /// <strong>one</strong> client.
        ///
        /// The public hook, <c>Connection.NotifyOperatingSystemNetworkState</c>, is static and every
        /// <c>Connection</c> registers itself with it in its constructor, unconditionally — setting
        /// <c>AutomaticNetworkStateMonitoring = false</c> does not opt out, it only stops the client
        /// subscribing to the real OS events. So the static hook reaches every live client in the
        /// process. Measured: using it destabilised three of the repo's own unit tests running
        /// concurrently, because each of their clients was handed RTN20a's 80017 DISCONNECTED with
        /// `retryInstantly`.
        ///
        /// This drives the same path per client — the two lines the private
        /// <c>Connection.HandleNetworkStateChange</c> runs — through the internal surface the test
        /// assembly already has access to. White-box adaptation of an internal API in a unit test,
        /// which <c>writing-derived-tests.md</c> permits; the observable the specs assert on is
        /// unchanged.
        /// </summary>
        public static void NotifyNetworkState(Connection connection, NetworkState state)
        {
            connection.NetworkState = state;
            connection.ConnectionManager.HandleNetworkStateChange(state);
        }

        /// <summary>A snapshot of a recorder list, safe to assert on while the client is still running.</summary>
        public static List<T> Snapshot<T>(List<T> recorded)
        {
            lock (recorded)
            {
                return new List<T>(recorded);
            }
        }

        private static async Task WithDeadline(Task task, TimeSpan? timeout, Func<string> describe)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeout ?? DefaultTimeout)).ConfigureAwait(false);
            if (completed != task)
            {
                throw new TimeoutException($"Timed out waiting for {describe()}.");
            }

            await task.ConfigureAwait(false);
        }
    }
}
