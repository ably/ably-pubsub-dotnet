using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Ably.PubSub.Tests.Uts.Infra
{
    /// <summary>
    /// What every UTS test class shares: the clients it built are torn down after each test, whether the body
    /// passed or threw (the specs' <c>AFTER EACH TEST: CLOSE_CLIENT(client)</c>), and a test never hand-rolls
    /// that teardown. Order: clients, then <see cref="OnTeardown"/> cleanups (last in, first out), then
    /// <see cref="OnVerify"/> checks; every step runs, and failures are thrown together as <see cref="UtsTeardownException"/>.
    /// </summary>
    public abstract class UtsTestBase : IAsyncLifetime
    {
        private readonly List<PubSubRealtimeClient> _realtimeClients = new List<PubSubRealtimeClient>();
        private readonly List<Func<Task>> _cleanups = new List<Func<Task>>();
        private readonly List<Action> _verifications = new List<Action>();

        protected UtsTestBase(ITestOutputHelper output)
        {
            Output = output;
        }

        protected ITestOutputHelper Output { get; }

        /// <summary>
        /// How long teardown waits for a client to reach CLOSED before giving up on it and disposing it anyway.
        /// </summary>
        protected virtual TimeSpan CloseTimeout => TimeSpan.FromSeconds(5);

        /// <summary>Whether teardown sends CLOSE and waits for CLOSED before disposing.</summary>
        protected virtual bool CloseClientsOnTeardown => false;

        /// <summary>
        /// How long teardown waits, after disposing a client, for its workflow to process the dispose and stop.
        /// </summary>
        protected virtual TimeSpan DisposeTimeout => TimeSpan.FromSeconds(5);

        Task IAsyncLifetime.InitializeAsync() => InitializeAsync();

        /// <summary>
        /// The teardown described in the class summary. xUnit calls it after every test; an infra self-test may
        /// call it on a helper instance to prove what it does.
        /// </summary>
        /// <returns>A task that faults with <see cref="UtsTeardownException"/> if teardown found problems.</returns>
        public async Task DisposeAsync()
        {
            var errors = new List<Exception>();
            foreach (var client in Snapshot(_realtimeClients))
            {
                try
                {
                    await TearDown(client).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            var cleanups = Snapshot(_cleanups);
            cleanups.Reverse();
            foreach (var cleanup in cleanups)
            {
                try
                {
                    await cleanup().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            foreach (var verification in Snapshot(_verifications))
            {
                try
                {
                    verification();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            foreach (var error in errors)
            {
                Output?.WriteLine($"UTS teardown: {error.GetType().Name}: {error.Message}");
            }

            if (errors.Count > 0)
            {
                throw new UtsTeardownException(errors);
            }
        }

        /// <summary>The specs' <c>CLOSE_CLIENT(client)</c> mid-test, with the same rules teardown uses.</summary>
        public Task CloseClient(PubSubRealtimeClient client) => TearDown(client);

        /// <summary>
        /// True once a disposed client's workflow has stopped: its command channel is completed and drained (the
        /// DisposeCommand ran, aborting the state timer) and nothing is being processed.
        /// </summary>
        internal static bool IsWorkflowStopped(PubSubRealtimeClient client)
            => CommandChannelCompletion(client).IsCompleted && !client.Workflow.IsProcessingCommands();

        /// <summary>
        /// Takes a UTS client off the process-wide OS network-state hook (the SDK's private static
        /// <c>Connection.OsEventSubscribers</c>, so a native test's <c>NotifyOperatingSystemNetworkState</c> can't reach it,
        /// RTN20); calls the private <c>CleanUpNetworkStateEvents</c> by reflection, so a rename in the SDK breaks it.
        /// </summary>
        internal static void DetachFromOsNetworkEvents(PubSubRealtimeClient client)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var cleanUp = typeof(Connection).GetMethod("CleanUpNetworkStateEvents", flags)
                ?? throw new InvalidOperationException(
                    "UTS infra: Connection.CleanUpNetworkStateEvents not found; the SDK's OS network hook changed shape.");
            cleanUp.Invoke(client.Connection, null);
        }

        /// <summary>Registers a realtime client for teardown. The client factories call this.</summary>
        protected PubSubRealtimeClient Track(PubSubRealtimeClient client)
        {
            lock (_realtimeClients)
            {
                _realtimeClients.Add(client);
            }

            return client;
        }

        /// <summary>Registers anything else that must be undone after the test, run last-in first-out.</summary>
        protected void OnTeardown(Func<Task> cleanup)
        {
            lock (_cleanups)
            {
                _cleanups.Add(cleanup);
            }
        }

        /// <summary>
        /// Registers a check that runs after every client and cleanup is torn down; if it throws, the test fails
        /// (see the class summary).
        /// </summary>
        protected void OnVerify(Action verification)
        {
            lock (_verifications)
            {
                _verifications.Add(verification);
            }
        }

        protected virtual Task InitializeAsync() => Task.CompletedTask;

        /// <summary>
        /// <c>Workflow.CommandChannel.Reader.Completion</c>, read by reflection: the net462 head doesn't
        /// reference System.Threading.Channels, so the infra can't name the channel type.
        /// </summary>
        private static Task CommandChannelCompletion(PubSubRealtimeClient client)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var workflow = client.Workflow;
            var channel = workflow.GetType().GetField("CommandChannel", flags)?.GetValue(workflow);
            var reader = channel?.GetType().GetProperty("Reader", flags)?.GetValue(channel);
            var completion = reader?.GetType().GetProperty("Completion", flags)?.GetValue(reader) as Task;
            return completion ?? throw new InvalidOperationException(
                "UTS infra: RealtimeWorkflow.CommandChannel.Reader.Completion not found; the SDK's workflow changed shape.");
        }

        private static List<T> Snapshot<T>(List<T> list)
        {
            lock (list)
            {
                return new List<T>(list);
            }
        }

        private async Task TearDown(PubSubRealtimeClient client)
        {
            if (!client.Disposed)
            {
                await CloseAndDispose(client).ConfigureAwait(false);
            }

            // Dispose() only queues the dispose: wait until the workflow has run it, so no timer or queued
            // command of this client can act after teardown (e.g. start a new connection attempt).
            await UtsWait.PollUntil(
                () => IsWorkflowStopped(client),
                DisposeTimeout,
                TimeSpan.FromMilliseconds(5),
                "the disposed client's workflow to stop").ConfigureAwait(false);
        }

        private async Task CloseAndDispose(PubSubRealtimeClient client)
        {
            // Close only from a state where close() can reach CLOSED (RTN12): INITIALIZED, CLOSED and
            // FAILED can't, so there is nothing to wait for.
            var state = client.Connection.State;
            var canClose = state != ConnectionState.Initialized
                && state != ConnectionState.Closed
                && state != ConnectionState.Failed;
            if (CloseClientsOnTeardown && canClose)
            {
                try
                {
                    var closed = UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed, CloseTimeout);
                    client.Close();
                    await closed.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Output?.WriteLine($"UTS teardown: client didn't reach CLOSED ({ex.Message}); disposing it anyway");
                }
            }

            client.Dispose();
        }
    }

    /// <summary>What <see cref="UtsTestBase.DisposeAsync"/> throws when teardown found problems: every one of them.</summary>
    public sealed class UtsTeardownException : XunitException
    {
        public UtsTeardownException(IReadOnlyList<Exception> errors)
            : base(Describe(errors))
        {
            Errors = errors;
        }

        /// <summary>Every cleanup and verification failure, in the order teardown met them.</summary>
        public IReadOnlyList<Exception> Errors { get; }

        private static string Describe(IReadOnlyList<Exception> errors)
            => $"UTS teardown found {errors.Count} problem(s):" + Environment.NewLine + string.Join(
                Environment.NewLine,
                errors.Select((e, i) => $"{i + 1}. {e.GetType().Name}: {e.Message}"));
    }
}
