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

namespace BrokeAPI
{
    public class Backend(XElement config, int updateTime)
    {
        private readonly XElement _config = config;
        private System.Timers.Timer? _timer; // Make _timer nullable
        private readonly int _updateTime = updateTime;
        private static readonly char[] separator = [';'];

        public void Start()
        {
            ScheduleDataRetrieval();
        }

        private void ScheduleDataRetrieval()
        {
            Console.WriteLine("Attempting scheduled data retrieval.");
            var currentTime = DateTime.Now;
            var nextMinute = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, currentTime.Hour, currentTime.Minute, 0).AddMinutes(1);
            var delay = (int)(nextMinute - currentTime).TotalMilliseconds;

         //var nextHour = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, currentTime.Hour, 0, 0).AddHours(1);
         //var delay = (int)(nextHour - currentTime).TotalMilliseconds;


            _timer = new System.Timers.Timer(delay);
            _timer.Elapsed += async (sender, e) => await OnTimedEvent();
            _timer.Start();
        }

        private async Task OnTimedEvent()
        {
            if (_timer != null)
            {
                _timer.Interval = _updateTime; // Use the UpdateTime from config
                await RetrieveAndUpdateData();
            }
        }

        private async Task RetrieveAndUpdateData()
        {
            try
            {
                var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "output");
                var sqlDirectory = Path.Combine(outputDirectory, "sql");

                // Create output directory if not exists
                if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);

                // Clear sql directory
                if (Directory.Exists(sqlDirectory))
                {
                    Directory.Delete(sqlDirectory, true);
                }
                Directory.CreateDirectory(sqlDirectory);

                var outputFilePath = Path.Combine(outputDirectory, "output.json");

                var authKey = _config.Element("DataHost")?.Element("AuthKey")?.Value;
                var serverLocation = _config.Element("DataHost")?.Element("ServerLocation")?.Value;

                using var client = new HttpClient();
                Console.WriteLine("Request sent.");
                var response = await client.GetStringAsync($"{serverLocation}/{authKey}/.json");
                File.WriteAllText(outputFilePath, response);

                Console.WriteLine("Response received");

                var outputJsonPath = Path.Combine(outputDirectory, "output.json");
                ProcessJsonToSql(outputJsonPath, sqlDirectory); // Updated to pass directory
                await ExecuteSqlFiles(sqlDirectory); // New method to execute all SQL files
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }


        private static void ProcessJsonToSql(string jsonFilePath, string sqlDirectory)
        {
            try
            {
                var jsonData = File.ReadAllText(jsonFilePath);
                using var jsonDoc = JsonDocument.Parse(jsonData);

                StringBuilder sqlCommands = new();
                int fileCount = 0, lineCount = 0;

                void WriteToFileAndReset()
                {
                    File.WriteAllText(Path.Combine(sqlDirectory, $"output_{fileCount}.sql"), sqlCommands.ToString());
                    sqlCommands.Clear();
                    fileCount++;
                    lineCount = 0;
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

                    sqlCommands.AppendLine($"INSERT INTO heroes (hero, mmr, plays, wins, scepter_plays, scepter_wins, shard_plays, shard_wins) " +
                                           $"VALUES ('{heroName}', {mmr}, {plays}, {wins}, {scepterPlays}, {scepterWins}, {shardPlays}, {shardWins}) " +
                                           $"ON DUPLICATE KEY UPDATE mmr = VALUES(mmr), plays = VALUES(plays), wins = VALUES(wins), " +
                                           $"scepter_plays = VALUES(scepter_plays), scepter_wins = VALUES(scepter_wins), " +
                                           $"shard_plays = VALUES(shard_plays), shard_wins = VALUES(shard_wins);");

                    lineCount++;
                    if (lineCount >= 10000)
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

                    sqlCommands.AppendLine($"INSERT INTO players (steam_id, mmr, plays, wins) " +
                                           $"VALUES ({steamId}, {mmr}, {plays}, {wins}) " +
                                           $"ON DUPLICATE KEY UPDATE mmr = VALUES(mmr), plays = VALUES(plays), wins = VALUES(wins);");

                    lineCount++;
                    if (lineCount >= 10000)
                    {
                        WriteToFileAndReset();
                    }
                }

                // Processing rounds
                var rounds = jsonDoc.RootElement.GetProperty("rounds").GetProperty("round_won");
                var challenger = rounds.GetProperty("challenger").GetInt32();
                var hard = rounds.GetProperty("hard").GetInt32();
                var nightmare = rounds.GetProperty("nightmare").GetInt32();
                var normal = rounds.GetProperty("normal").GetInt32();

                sqlCommands.AppendLine($"INSERT INTO rounds (id, challenger, hard, nightmare, normal) " +
                                       $"VALUES (1, {challenger}, {hard}, {nightmare}, {normal}) " +
                                       $"ON DUPLICATE KEY UPDATE challenger = VALUES(challenger), hard = VALUES(hard), " +
                                       $"nightmare = VALUES(nightmare), normal = VALUES(normal);");

                lineCount++;
                if (lineCount >= 10000)
                {
                    WriteToFileAndReset();
                }

                // Write remaining commands if any
                if (lineCount > 0)
                {
                    WriteToFileAndReset();
                }

                Console.WriteLine("SQL files created successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing JSON to SQL: {ex.Message}");
            }
        }

        public async Task ExecuteSqlFiles(string sqlDirectory)
        {
            var sqlFiles = Directory.GetFiles(sqlDirectory, "*.sql");
            foreach (var sqlFile in sqlFiles)
            {
                var sqlContent = File.ReadAllText(sqlFile);
                if (!string.IsNullOrWhiteSpace(sqlContent))
                {
                    await ExecuteSqlFile(sqlFile);
                    await Task.Delay(3000); // Delay between files
                }
                else
                {
                    Console.WriteLine($"Skipping empty SQL file: {sqlFile}");
                }
            }
        }


        public async Task ExecuteSqlFile(string sqlFilePath)
        {
            try
            {
                var databaseConfig = _config.Element("Database");
                var connectionString = $"Server={databaseConfig?.Element("DatabaseIP")?.Value};" +
                                       $"Port={databaseConfig?.Element("DatabasePort")?.Value};" +
                                       $"Database={databaseConfig?.Element("DatabaseName")?.Value};" +
                                       $"User={databaseConfig?.Element("DatabaseLogin")?.Value};" +
                                       $"Password={databaseConfig?.Element("DatabasePassword")?.Value};";

                using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                var sqlCommands = File.ReadAllText(sqlFilePath).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var sqlCommand in sqlCommands)
                {
                    if (!string.IsNullOrWhiteSpace(sqlCommand))
                    {
                        using var command = new MySqlCommand(sqlCommand, connection);
                        await command.ExecuteNonQueryAsync();
                    }
                }

                Console.WriteLine($"SQL file executed successfully: {sqlFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing SQL file {sqlFilePath}: {ex.Message}");
            }
        }
    }
}
