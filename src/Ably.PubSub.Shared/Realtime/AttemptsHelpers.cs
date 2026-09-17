using System;
using System.Linq;
using System.Threading.Tasks;
using IO.Ably.Realtime.Workflow;

namespace IO.Ably.Realtime
{
    internal static class AttemptsHelpers
    {
        public static async Task<bool> CanFallback(this AblyRest restClient, ErrorInfo error)
        {
            return error != null && error.IsRetryableStatusCode() && await restClient.CanConnectToAbly();
        }

        public static bool ShouldSuspend(this RealtimeState state, Func<DateTimeOffset> now = null)
        {
            var firstAttempt = state.AttemptsInfo.FirstAttempt;
            if (firstAttempt == null)
            {
                return false;
            }

            if (state.AttemptsInfo.SuspendedCount() > 0)
            {
                return true;
            }

            now = now ?? Defaults.NowFunc();
            return (now() - firstAttempt.Value) >= state.Connection.ConnectionStateTtl;
        }

        public static string GetHost(RealtimeState state, string realtimeHost)
        {
            var lastFailedState = state.AttemptsInfo.Attempts.SelectMany(x => x.FailedStates).LastOrDefault(x => x.ShouldUseFallback());
            string customHost = string.Empty;
            var disconnectedCount = state.AttemptsInfo.DisconnectedCount();
            var suspendedCount = state.AttemptsInfo.SuspendedCount();

            if (lastFailedState != null)
            {
                // RTN17a if we were previously connected to a fallback host
                // we need to first try again on the default host before starting to check fallback hosts
                if (state.Connection.Host.IsNotEmpty() && state.Connection.IsFallbackHost)
                {
                    return realtimeHost;
                }

                if (lastFailedState.State == ConnectionState.Disconnected)
                {
                    // DisconnectedCount will always be > 0 and we want to start with the first host.
                    customHost = GetFallbackHost(disconnectedCount);
                }

                if (lastFailedState.State == ConnectionState.Suspended && suspendedCount > 1)
                {
                    customHost = GetFallbackHost(disconnectedCount + suspendedCount);
                }

                if (customHost.IsNotEmpty())
                {
                    return customHost;
                }
            }

            return realtimeHost;

            string GetFallbackHost(int failedRequestCount)
            {
                if (state.Connection.FallbackHosts.Count == 0)
                {
                    return string.Empty;
                }

                // We -1 because the index is 0 based where the failedRequestCount starts from 1
                var index = (failedRequestCount - 1) % state.Connection.FallbackHosts.Count;
                return state.Connection.FallbackHosts[index];
            }
        }
    }
}
