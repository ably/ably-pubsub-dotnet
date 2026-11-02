using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Ably.PubSub.Tests.Uts.Infra.Integration
{
    /// <summary>
    /// The specs' <c>BEFORE ALL TESTS</c> / <c>AFTER ALL TESTS</c> sandbox provisioning as an xUnit collection
    /// fixture (guide 5.4): one <see cref="SandboxApp"/> per collection, created before the collection's first
    /// test and deleted (best-effort) after its last.
    /// </summary>
    public sealed class SandboxAppFixture : IAsyncLifetime
    {
        private readonly IMessageSink _diagnostics;
        private int _initializeCount;

        /// <summary>
        /// Creates the fixture. xUnit passes its diagnostic sink, which reports the app's creation and its
        /// <c>DELETE</c> status (shown with <c>-- xUnit.DiagnosticMessages=true</c>, or in the runner's
        /// diagnostics).
        /// </summary>
        /// <param name="diagnostics">xUnit's diagnostic message sink; null when the fixture is built by hand.</param>
        public SandboxAppFixture(IMessageSink diagnostics = null)
        {
            _diagnostics = diagnostics;
        }

        /// <summary>The collection's app; set before any test of the collection runs.</summary>
        public SandboxApp App { get; private set; }

        /// <summary>How many times this fixture provisioned an app: 1 for the whole collection.</summary>
        public int InitializeCount => Volatile.Read(ref _initializeCount);

        public async Task InitializeAsync()
        {
            Interlocked.Increment(ref _initializeCount);
            App = await SandboxApp.CreateAsync().ConfigureAwait(false);
            Report($"UTS sandbox: created app {App.AppId} on {App.Host}");
        }

        public async Task DisposeAsync()
        {
            var app = App;
            if (app != null)
            {
                await app.DeleteAsync().ConfigureAwait(false);
                Report(
                    $"UTS sandbox: DELETE /apps/{app.AppId} -> {app.LastDeleteStatus?.ToString() ?? "no response"} " +
                    $"(deleted: {app.Deleted}{(app.LastDeleteError == null ? string.Empty : ", " + app.LastDeleteError.Message)})");
            }
        }

        private void Report(string message)
        {
            _diagnostics?.OnMessage(new DiagnosticMessage(message));
        }
    }

    /// <summary>
    /// The direct-sandbox collection: every UTS integration class joins it with <c>[Collection("UTS
    /// Sandbox")]</c>, so the classes share one app and run one after another.
    /// </summary>
    [CollectionDefinition(Name)]
    public class UtsSandboxCollection : ICollectionFixture<SandboxAppFixture>
    {
        public const string Name = "UTS Sandbox";
    }
}
