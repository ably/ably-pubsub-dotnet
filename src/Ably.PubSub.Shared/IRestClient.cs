using System;
using System.Threading.Tasks;
using IO.Ably.Push;
using IO.Ably.Rest;

namespace IO.Ably
{
    /// <summary>
    /// Interface for a rest client.
    /// </summary>
    public interface IRestClient : IStatsCommands
    {
        /// <summary>Authentication methods.</summary>
        IAblyAuth Auth { get; }

        // /// <summary>
        // /// Methods for managing Push notifications subscriptions and device registrations.
        // /// </summary>
        // PushRest Push { get; }

        /// <summary>Channel methods.</summary>
        RestChannels Channels { get; }

        /// <summary>Retrieves the ably service time.</summary>
        /// <returns>DateTimeOffset of the server time.</returns>
        Task<DateTimeOffset> TimeAsync();

        /// <summary>
        /// Sync method for getting server time.
        /// </summary>
        /// <returns>DateTimeOffset of the server time.</returns>
        DateTimeOffset Time();

        /// <summary>
        /// The local device instance represents the current state of the device in respect of it being a target for push notifications.
        /// </summary>
        LocalDevice Device { get; }
    }

    /// <summary>
    /// Defines an interface for Stats commands.
    /// </summary>
    public interface IStatsCommands
    {
        /// <summary>Retrieves the stats for the application. Passed default <see cref="StatsRequestParams"/> for the request.</summary>
        /// <returns><see cref="PaginatedResult{T}"/> of <see cref="IO.Ably.Stats"/>.</returns>
        Task<PaginatedResult<Stats>> StatsAsync();

        /// <summary>Retrieves the stats for the application using a more specific stats query. Check <see cref="StatsRequestParams"/> for more information.</summary>
        /// <param name="query">stats query.</param>
        /// <returns><see cref="PaginatedResult{T}"/> of <see cref="IO.Ably.Stats"/>.</returns>
        Task<PaginatedResult<Stats>> StatsAsync(StatsRequestParams query);

        /// <summary>Retrieves the stats for the application. Passed default <see cref="StatsRequestParams"/> for the request.</summary>
        /// <returns><see cref="PaginatedResult{T}"/> of <see cref="IO.Ably.Stats"/>.</returns>
        PaginatedResult<Stats> Stats();

        /// <summary>Retrieves the stats for the application. Passed default <see cref="StatsRequestParams"/> for the request.</summary>
        /// <param name="query">stats query.</param>
        /// <returns><see cref="PaginatedResult{T}"/> of <see cref="IO.Ably.Stats"/>.</returns>
        PaginatedResult<Stats> Stats(StatsRequestParams query);
    }
}
