using Npgsql;

namespace FLB_API.Statistics.Modules
{
    public class RawDataModule : IStatisticModule
    {
        public string Id => "raw_data";

        private StatisticsManager? Instance { get; set; }

        private FusionAPI.Interfaces.ILogger? Logger { get; set; }

        public async Task<bool> Init(StatisticsManager statistics, FusionAPI.Interfaces.ILogger logger)
        {
            Instance = statistics;
            Logger = logger;

            if (Instance.DataSource == null)
                return false;

            const string sql = "CREATE TABLE IF NOT EXISTS modules.raw_data (fetchId INT PRIMARY KEY, date TIMESTAMPTZ, json JSONB)";
            await using var connection = await Instance.DataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(sql, connection);

            try
            {
                await cmd.ExecuteNonQueryAsync();
                return true;
            }
            catch (Exception ex)
            {
                Logger?.Error(Format("Failed to create schema"), ex);
                return false;
            }
            finally
            {
                await connection.CloseAsync();
            }
        }

        public async Task Analyze(LobbyListResponse response, NpgsqlConnection? connection = null)
        {
            if (Instance?.DataSource == null)
                return;

            var lastFetch = await GetLastFetchId();
            if (lastFetch == -1)
                return;
            const string sql = "INSERT INTO modules.raw_data (fetchId, date, json) VALUES (@fetchId, @date, @json)";

            if (connection != null)
            {
                await AnalyzeLobby(connection);
            }
            else
            {
                await using var conn = await Instance.DataSource.OpenConnectionAsync();
                await AnalyzeLobby(conn);
            }

            return;
            async Task AnalyzeLobby(NpgsqlConnection _connection)
            {
                await using var cmd = new NpgsqlCommand(sql, _connection);
                cmd.Parameters.Add(new NpgsqlParameter("fetchId", NpgsqlTypes.NpgsqlDbType.Integer) { Value = lastFetch + 1 });
                cmd.Parameters.Add(new NpgsqlParameter("date", NpgsqlTypes.NpgsqlDbType.TimestampTz)
                { Value = DateTimeOffset.FromUnixTimeSeconds(response.Date).UtcDateTime });
                cmd.Parameters.Add(new NpgsqlParameter("json", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = response.Json });

                try
                {
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    Logger?.Error(Format("Failed to insert raw data"), ex);
                }
                finally
                {
                    if (connection == null)
                        await _connection.CloseAsync();
                }
            }
        }

        public async Task<int> GetLastFetchId()
        {
            if (Instance?.DataSource == null)
                return -1;

            const string sql = "SELECT fetchId FROM modules.raw_data ORDER BY fetchId DESC LIMIT 1";
            await using var connection = await Instance.DataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(sql, connection);

            try
            {
                await using var data = await cmd.ExecuteReaderAsync();
                if (await data.ReadAsync())
                    return data.GetInt32(0);
            }
            catch (Exception ex)
            {
                Logger?.Error(Format("Failed to get last fetch id"), ex);
            }
            finally
            {
                await connection.CloseAsync();
            }

            return 0;
        }

        private static string Format(string message)
            => $"[RAW] {message}";
    }
}