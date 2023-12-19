using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Linq;
using MySql.Data.MySqlClient;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Data.SqlClient;
using Serilog;

namespace BrokeAPI
{
    public class Backend
    {
        private readonly XElement _config;
        private System.Timers.Timer? _timer;
        private readonly int _updateTime;
        private static readonly char[] separatorArray = [';'];

        public Backend(XElement config, int updateTime)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _updateTime = updateTime;
            Log.Information("Backend initialized with update time: {UpdateTime}", updateTime);
        }

        public void Start()
        {
            Log.Information("Starting Backend process.");
            ScheduleDataRetrieval();
        }

        private void ScheduleDataRetrieval()
        {
            Log.Information("Scheduling data retrieval.");
            var currentTime = DateTime.Now;
            var nextMinute = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, currentTime.Hour, currentTime.Minute, 0).AddMinutes(1);
            var delay = (int)(nextMinute - currentTime).TotalMilliseconds;

            //var nextHour = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, currentTime.Hour, 0, 0).AddHours(1);
            //var delay = (int)(nextHour - currentTime).TotalMilliseconds;

            _timer = new System.Timers.Timer(delay);
            _timer.Elapsed += async (sender, e) => await OnTimedEvent();
            _timer.Start();
            Log.Information("Data retrieval scheduled to start in {Delay} milliseconds.", delay);
        }

        private async Task OnTimedEvent()
        {
            Log.Information("Timer event triggered.");
            if (_timer != null)
            {
                _timer.Interval = _updateTime;
                await RetrieveAndUpdateData();
            }
            else
            {
                Log.Warning("Timer is null, unable to proceed with data retrieval.");
            }
        }

        private async Task RetrieveAndUpdateData()
        {
            try
            {
                Log.Information("Retrieving and updating data.");
                var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "output");
                var sqlDirectory = Path.Combine(outputDirectory, "sql");

                if (!Directory.Exists(outputDirectory))
                {
                    Log.Information("Creating output directory.");
                    Directory.CreateDirectory(outputDirectory);
                }

                if (Directory.Exists(sqlDirectory))
                {
                    Log.Information("Clearing SQL directory.");
                    Directory.Delete(sqlDirectory, true);
                }
                Directory.CreateDirectory(sqlDirectory);

                var outputFilePath = Path.Combine(outputDirectory, "output.json");
                var authKey = _config.Element("DataHost")?.Element("AuthKey")?.Value;
                var serverLocation = _config.Element("DataHost")?.Element("ServerLocation")?.Value;

                using var client = new HttpClient();
                Log.Information("Sending request to {ServerLocation}", serverLocation);
                var response = await client.GetStringAsync($"{serverLocation}/{authKey}/.json");
                File.WriteAllText(outputFilePath, response);
                Log.Information("Response received and written to {OutputFilePath}", outputFilePath);

                var outputJsonPath = Path.Combine(outputDirectory, "output.json");
                ProcessJsonToSql(outputJsonPath, sqlDirectory);
                await ExecuteSqlFiles(sqlDirectory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Log.Error(ex, "Error during data retrieval and update.");
            }
        }


        private void ProcessJsonToSql(string jsonFilePath, string sqlDirectory)
        {
            var databaseEngine = _config.Element("Database")?.Element("Engine")?.Value ?? "MySQL";
            Log.Information("Processing JSON to SQL. Database Engine: {DatabaseEngine}", databaseEngine);

            try
            {
                var jsonData = File.ReadAllText(jsonFilePath);
                Log.Information("JSON data read from {JsonFilePath}", jsonFilePath);

                using var jsonDoc = JsonDocument.Parse(jsonData);
                Log.Information("JSON document parsed successfully.");

                StringBuilder sqlCommands = new();
                int fileCount = 0, lineCount = 0;

                void WriteToFileAndReset()
                {
                    if (sqlCommands.Length > 0)
                    {
                        var filePath = Path.Combine(sqlDirectory, $"output_{fileCount}.sql");
                        File.WriteAllText(filePath, sqlCommands.ToString());
                        Log.Information("SQL commands written to file: {FilePath}", filePath);

                        sqlCommands.Clear();
                        fileCount++;
                        lineCount = 0;
                    }
                    else
                    {
                        Console.WriteLine("No SQL commands to write.");
                        Log.Information("No SQL commands to write at this point.");
                    }
                }

                // Processing heroes
                var heroes = jsonDoc.RootElement.GetProperty("heroes").EnumerateObject();
                foreach (var hero in heroes)
                {
                    var heroName = hero.Name;
                    var mmr = hero.Value.GetProperty("mmr").GetInt32();
                    var plays = hero.Value.GetProperty("plays").GetInt32();
                    var wins = hero.Value.GetProperty("wins").GetInt32();
                    var scepterPlays = hero.Value.GetProperty("scepter").GetProperty("plays").GetInt32();
                    var scepterWins = hero.Value.GetProperty("scepter").GetProperty("wins").GetInt32();
                    var shardPlays = hero.Value.GetProperty("shard").GetProperty("plays").GetInt32();
                    var shardWins = hero.Value.GetProperty("shard").GetProperty("wins").GetInt32();

                    if (databaseEngine.Equals("MSSQL", StringComparison.OrdinalIgnoreCase))
                    {
                        // MSSQL specific SQL syntax - Make sure to add a semi-colon at the end of each MERGE statement
                        sqlCommands.AppendLine($"MERGE INTO heroes WITH (HOLDLOCK) AS target " +
                                               $"USING (SELECT '{heroName}' AS hero) AS source " +
                                               $"ON target.hero = source.hero " +
                                               $"WHEN MATCHED THEN " +
                                               $"UPDATE SET mmr = {mmr}, plays = {plays}, wins = {wins}, " +
                                               $"scepter_plays = {scepterPlays}, scepter_wins = {scepterWins}, " +
                                               $"shard_plays = {shardPlays}, shard_wins = {shardWins} " +
                                               $"WHEN NOT MATCHED THEN " +
                                               $"INSERT (hero, mmr, plays, wins, scepter_plays, scepter_wins, shard_plays, shard_wins) " +
                                               $"VALUES ('{heroName}', {mmr}, {plays}, {wins}, {scepterPlays}, {scepterWins}, {shardPlays}, {shardWins});");
                    }
                    else
                    {
                        // MySQL specific SQL syntax
                        sqlCommands.AppendLine($"INSERT INTO heroes (hero, mmr, plays, wins, scepter_plays, scepter_wins, shard_plays, shard_wins) " +
                                               $"VALUES ('{heroName}', {mmr}, {plays}, {wins}, {scepterPlays}, {scepterWins}, {shardPlays}, {shardWins}) " +
                                               $"ON DUPLICATE KEY UPDATE mmr = VALUES(mmr), plays = VALUES(plays), wins = VALUES(wins), " +
                                               $"scepter_plays = VALUES(scepter_plays), scepter_wins = VALUES(scepter_wins), " +
                                               $"shard_plays = VALUES(shard_plays), shard_wins = VALUES(shard_wins);");
                    }

                    lineCount++;
                    if (lineCount >= 10000) // Temporarily lower the threshold to ensure the file is written
                    {
                        WriteToFileAndReset();
                    }
                }
                var players = jsonDoc.RootElement.GetProperty("players").EnumerateObject();
                foreach (var player in players)
                {
                    var steamId = player.Name;
                    var mmr = player.Value.GetProperty("mmr").GetInt32();
                    var plays = player.Value.GetProperty("plays").GetInt32();
                    var wins = player.Value.GetProperty("wins").GetInt32();

                    if (databaseEngine.Equals("MSSQL", StringComparison.OrdinalIgnoreCase))
                    {
                        // MSSQL specific SQL syntax
                        sqlCommands.AppendLine($"MERGE INTO players WITH (HOLDLOCK) AS target " +
                                               $"USING (SELECT {steamId} AS steam_id) AS source " +
                                               $"ON target.steam_id = source.steam_id " +
                                               $"WHEN MATCHED THEN " +
                                               $"UPDATE SET mmr = {mmr}, plays = {plays}, wins = {wins} " +
                                               $"WHEN NOT MATCHED THEN " +
                                               $"INSERT (steam_id, mmr, plays, wins) " +
                                               $"VALUES ({steamId}, {mmr}, {plays}, {wins});");  // Add semi-colon here
                    }
                    else
                    {
                        // MySQL specific SQL syntax
                        sqlCommands.AppendLine($"INSERT INTO players (steam_id, mmr, plays, wins) " +
                                               $"VALUES ({steamId}, {mmr}, {plays}, {wins}) " +
                                               $"ON DUPLICATE KEY UPDATE mmr = VALUES(mmr), plays = VALUES(plays), wins = VALUES(wins);");
                    }

                    lineCount++;
                    if (lineCount >= 10000)
                    {
                        WriteToFileAndReset();
                    }
                }


                var rounds = jsonDoc.RootElement.GetProperty("rounds").GetProperty("round_won").EnumerateObject();
                foreach (var round in rounds)
                {
                    var name = round.Name;
                    var roundsWins = round.Value.GetInt64();
                    if (databaseEngine.Equals("MSSQL", StringComparison.OrdinalIgnoreCase))
                    {
                        // MSSQL specific SQL syntax
                        sqlCommands.AppendLine($"MERGE INTO rounds WITH (HOLDLOCK) AS target " +
                                               $"USING (SELECT '{name}' AS name) AS source " +
                                               $"ON target.name = source.name " +
                                               $"WHEN MATCHED THEN " +
                                               $"UPDATE SET wins = {roundsWins} " +
                                               $"WHEN NOT MATCHED THEN " +
                                               $"INSERT (name, wins) " +
                                               $"VALUES ('{name}', {roundsWins});");
                    }
                    else
                    {
                        // MySQL specific SQL syntax
                        sqlCommands.AppendLine($"INSERT INTO rounds (name, wins) " +
                                               $"VALUES ('{name}', {roundsWins}) " +
                                               $"ON DUPLICATE KEY UPDATE wins = VALUES(wins);");
                    }

                    lineCount++;
                    if (lineCount >= 1)
                    {
                        WriteToFileAndReset();
                    }
                }

                Console.WriteLine("SQL files created successfully.");
                Log.Information("SQL files created successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing JSON to SQL: {ex.Message}");
                Log.Error(ex, "Error processing JSON to SQL.");
            }
        }

        public async Task ExecuteSqlFiles(string sqlDirectory)
        {
            Log.Information("Executing SQL files in directory: {SqlDirectory}", sqlDirectory);
            var sqlFiles = Directory.GetFiles(sqlDirectory, "*.sql");
            foreach (var sqlFile in sqlFiles)
            {
                Log.Information("Reading SQL file: {SqlFile}", sqlFile);
                var sqlContent = File.ReadAllText(sqlFile);
                if (!string.IsNullOrWhiteSpace(sqlContent))
                {
                    Log.Information("Executing SQL file: {SqlFile}", sqlFile);
                    await ExecuteSqlFile(sqlFile);
                }
                else
                {
                    Console.WriteLine($"Skipping empty SQL file: {sqlFile}");
                    Log.Information("Skipping empty SQL file: {SqlFile}", sqlFile);
                }
            }
        }

        public async Task ExecuteSqlFile(string sqlFilePath)
        {
            Log.Information("Preparing to execute SQL file: {SqlFilePath}", sqlFilePath);
            var databaseConfig = _config.Element("Database");
            if (databaseConfig == null)
            {
                Console.WriteLine("Database configuration is missing. Cannot execute SQL file.");
                Log.Warning("Database configuration is missing. Cannot execute SQL file: {SqlFilePath}", sqlFilePath);
                return;
            }

            var databaseEngine = databaseConfig.Element("Engine")?.Value ?? "MySQL";
            try
            {
                if (databaseEngine.Equals("MSSQL", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("Executing MSSQL file: {SqlFilePath}", sqlFilePath);
                    await ExecuteSqlFileMSSQL(sqlFilePath, databaseConfig);
                }
                else
                {
                    Log.Information("Executing MySQL file: {SqlFilePath}", sqlFilePath);
                    await ExecuteSqlFileMySQL(sqlFilePath, databaseConfig);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing SQL file {sqlFilePath}: {ex.Message}");
                Log.Error(ex, "Error executing SQL file: {SqlFilePath}", sqlFilePath);
            }
        }

        private static async Task ExecuteSqlFileMySQL(string sqlFilePath, XElement databaseConfig)
        {
            var connectionString = $"Server={databaseConfig?.Element("DatabaseIP")?.Value};" +
                                   $"Port={databaseConfig?.Element("DatabasePort")?.Value};" +
                                   $"Database={databaseConfig?.Element("DatabaseName")?.Value};" +
                                   $"User={databaseConfig?.Element("DatabaseLogin")?.Value};" +
                                   $"Password={databaseConfig?.Element("DatabasePassword")?.Value};";

            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            Log.Information("Executing SQL commands in MySQL for file: {SqlFilePath}", sqlFilePath);
            var sqlCommands = File.ReadAllText(sqlFilePath).Split(separatorArray, StringSplitOptions.RemoveEmptyEntries);
            foreach (var sqlCommand in sqlCommands)
            {
                if (!string.IsNullOrWhiteSpace(sqlCommand))
                {
                    using var command = new MySqlCommand(sqlCommand, connection);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        public static async Task ExecuteSqlFileMSSQL(string sqlFilePath, XElement databaseConfig)
        {
            var connectionString = $"Server={databaseConfig?.Element("DatabaseIP")?.Value};" +
                                   $"Database={databaseConfig?.Element("DatabaseName")?.Value};" +
                                   $"User Id={databaseConfig?.Element("DatabaseLogin")?.Value};" +
                                   $"Password={databaseConfig?.Element("DatabasePassword")?.Value};";

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var sqlContent = File.ReadAllText(sqlFilePath);

            Log.Information("Executing SQL commands in MSSQL for file: {SqlFilePath}", sqlFilePath);

            var sqlCommands = sqlContent.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (var sqlCommand in sqlCommands)
            {
                if (!string.IsNullOrWhiteSpace(sqlCommand))
                {
                    // Add a semicolon at the end of the SQL command
                    var formattedSql = sqlCommand.Trim() + ";";

                    using var command = new SqlCommand(formattedSql, connection);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

    }
}
