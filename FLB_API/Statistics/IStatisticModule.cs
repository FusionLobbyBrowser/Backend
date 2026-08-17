using Npgsql;

namespace FLB_API.Statistics
{
    public interface IStatisticModule
    {
        public string Id { get; }

        public Task<bool> Init(StatisticsManager statistics, FusionAPI.Interfaces.ILogger logger);

        /// <summary>
        /// Provides a <seealso cref="response"/> for the module to analyze and update the statistics accordingly. In
        /// case of an exception, it is treated as if the module didn't handle the response properly and on the next app
        /// startup will migrate (the module will be provided the response that it failed on). Limit the exceptions as
        /// much as possible to avoid conflicts.
        /// </summary>
        /// <param name="response"></param>
        /// <returns></returns>
        public Task Analyze(LobbyListResponse response, NpgsqlConnection? connection = null);
    }
}