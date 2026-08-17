using System.Reflection;
using System.Text.Json;

using Npgsql;

using NpgsqlTypes;

namespace FLB_API.Statistics
{
    public class StatisticsManager(FusionAPI.Interfaces.ILogger logger)
    {
        public NpgsqlDataSource? DataSource { get; private set; }

        public FusionAPI.Interfaces.ILogger Logger { get; } = logger;

        private readonly List<IStatisticModule> _modules = [];

        public IReadOnlyList<IStatisticModule> Modules => _modules.AsReadOnly();

        public bool IsMigrating { get; set; } = false;

        public List<LobbyListResponse> AdditionalToMigrate { get; set; } = [];

        public List<string> MigrateList { get; } = [];

        public async Task Init(string connectionString)
        {
            Logger?.Info("Creating data source...");
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder
                .ConfigureJsonOptions(JsonSerializerOptions.Web);
            DataSource = builder.Build();

            Logger?.Info("Creating migration schema");

            const string sqlSchema = "CREATE SCHEMA IF NOT EXISTS modules";
            await using var connection = await DataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(sqlSchema, connection);

            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Logger?.Error("Failed to create migration schema", ex);
            }

            Logger?.Info("Creating migration table");

            const string sqlTable = "CREATE TABLE IF NOT EXISTS modules.migration (id TEXT PRIMARY KEY, fetchId INT)";
            await using var cmdTable = new NpgsqlCommand(sqlTable, connection);

            try
            {
                await cmdTable.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Logger?.Error("Failed to create migration table", ex);
            }
            finally
            {
                await connection.CloseAsync();
            }
        }

        public async Task Migrate()
        {
            Logger.Info("Checking if requires migration");
            var lastFetchId = await GetLastFetchId();
            switch (lastFetchId)
            {
                case 0:
                    Logger.Info("No migration required");
                    return;

                case -1:
                    Logger.Error("Data source is null, cannot migrate");
                    return;
            }

            Dictionary<string, int> data = [];

            if (DataSource == null)
                return;

            const string sql = "SELECT * FROM modules.migration";
            await using var connection = await DataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(sql, connection);

            try
            {
                try
                {
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        data.TryAdd(reader.GetString(0), reader.GetInt32(1));
                }
                catch (Exception ex)
                {
                    Logger?.Error("Failed to create migration table", ex);
                }

                Dictionary<int, LobbyListResponse> old = [];

                foreach (var module in Modules)
                {
                    if (data.TryGetValue(module.Id, out var fetchId) && fetchId >= lastFetchId)
                        continue;

                    Logger?.Warning(
                        $"Module {module.Id} requires migration ({lastFetchId - fetchId} lobbies to migrate)");
                    MigrateList.Add(module.Id);

                    const string sql2 = "SELECT * FROM modules.raw_data WHERE fetchId > @fetchId";
                    await using var cmd2 = new NpgsqlCommand(sql2, connection);
                    try
                    {
                        await using var rawReader = await cmd2.ExecuteReaderAsync();
                        while (await rawReader.ReadAsync())
                        {
                            if (old.ContainsKey(rawReader.GetInt32(0)))
                                continue;

                            var jsonString = rawReader.GetString(2);
                            var res = JsonSerializer.Deserialize<LobbyListResponse>(jsonString);
                            if (res != null)
                                old.Add(rawReader.GetInt32(0), res);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error("Failed to fetch old lobbies", ex);
                        return;
                    }

                    foreach (var toProcess in old.Where(x => x.Key > fetchId).OrderBy(x => x.Key))
                        await module.Analyze(toProcess.Value, connection);
                }

                if (AdditionalToMigrate.Count > 0)
                {
                    var modules = MigrateList.Select(x => Modules.FirstOrDefault(y => y.Id == x)) ?? [];

                    foreach (var response in AdditionalToMigrate)
                    {
                        foreach (var module in modules)
                            await module?.Analyze(response, connection);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Error("Failed to migrate", ex);
            }
            finally
            {
                MigrateList.Clear();
                AdditionalToMigrate.Clear();
                IsMigrating = false;
                await connection.CloseAsync();
            }
        }

        public async Task Analyze(LobbyListResponse response, bool shouldAwait = false)
        {
            if (IsMigrating)
                AdditionalToMigrate.Add(response);

            if (shouldAwait)
                await Process();
            else
                _ = Task.Run(async () => await Process());
            return;

            async Task Process()
            {
                var fetchId = -1;
                foreach (var module in Modules)
                {
                    if (MigrateList.Contains(module.Id))
                        continue;

                    fetchId = await AnalyzeLobby(response, module, fetchId);
                }
            }
        }

        private async Task<int> AnalyzeLobby(LobbyListResponse res, IStatisticModule module, int fetchId = -1)
        {
            var failed = false;
            try
            {
                await module.Analyze(res);
            }
            catch (Exception ex)
            {
                Logger.Error($"An error occurred while analyzing the lobby by module {module.Id}", ex);
                failed = true;
            }

            if (failed || DataSource == null)
                return fetchId;

            if (fetchId == -1)
                fetchId = await GetLastFetchId();
            const string sql = "INSERT INTO modules.migration (id, fetchId) VALUES (@id, @fetchId) ON CONFLICT (id) DO UPDATE SET fetchId = @fetchId";
            await using var connection = await DataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(sql, connection);
            cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Text) { Value = module.Id });
            cmd.Parameters.Add(new NpgsqlParameter(nameof(fetchId), NpgsqlDbType.Integer) { Value = fetchId != -1 ? fetchId : GetLastFetchId() });

            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Logger?.Error($"Failed to update migration table for module {module.Id}", ex);
            }
            finally
            {
                await connection.CloseAsync();
            }

            return fetchId;
        }

        public async Task<int> GetLastFetchId()
        {
            if (DataSource == null)
                return -1;

            const string sql = "SELECT fetchId FROM modules.raw_data ORDER BY fetchId DESC LIMIT 1";
            await using var connection = await DataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(sql, connection);

            try
            {
                await using var data = await cmd.ExecuteReaderAsync();
                if (await data.ReadAsync())
                    return data.GetInt32(0);
            }
            catch (Exception ex)
            {
                Logger?.Error("Failed to get last fetch id", ex);
            }
            finally
            {
                await connection.CloseAsync();
            }

            return 0;
        }

        public async Task RegisterModule<TModule>() where TModule : IStatisticModule
            => await RegisterModule(typeof(TModule));

        public async Task RegisterModule(Type type)
        {
            if (!typeof(IStatisticModule).IsAssignableFrom(type))
                throw new ArgumentException("Type is not a statistics module.");

            if (Modules.Any(m => m.GetType() == type))
                throw new ArgumentException("Module of this type is already registered.");

            var obj = Activator.CreateInstance(type);
            if (obj != null)
            {
                var module = (IStatisticModule)obj;
                if (await module.Init(this, Logger))
                    _modules.Add(module);
            }
            else
            {
                throw new ArgumentException("The created object from type is null"); // this shouldn't happen
            }
        }

        public async Task RegisterFromAssembly(Assembly assembly)
        {
            var all = assembly.GetTypes().Where(t => typeof(IStatisticModule).IsAssignableFrom(t) && t.IsClass);
            foreach (var type in all)
                await RegisterModule(type);
        }

        public void UnregisterModule<TModule>() where TModule : IStatisticModule
            => UnregisterModule(typeof(TModule));

        public void UnregisterModule(Type type)
        {
            if (!type.IsSubclassOf(typeof(IStatisticModule)))
                throw new ArgumentException("Type does not inherit from IStatisticModule");

            var module = Modules.FirstOrDefault(m => m.GetType() == type);
            if (module != null)
                _modules.Remove(module);
            else
                throw new ArgumentException("Module of this type is not registered.");
        }

        public void UnregisterFromAssembly(Assembly assembly)
        {
            var all = assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(IStatisticModule)));
            foreach (var type in all)
                UnregisterModule(type);
        }
    }
}