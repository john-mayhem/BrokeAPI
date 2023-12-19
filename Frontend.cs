using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MySql.Data.MySqlClient;
using System.Data.SqlClient;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;



//2. players 
//- top 200 (mmr)
//- top 200 (winrate)
//- top 100 (mmr)
//- top 100 (winrate)
//- top 50 (mmr)
//- top 50 (winrate)
//- top 10 (mmr)
//- top 10 (winrate)

//Top MMR players should have a minumum of 5240 MMR and steam_id of not 0 
//Top Winrate players should have at least 50 wins and steam_id of not 0

//3. difficulty 
//- challenger wins 
//- normal wins
//- hard wins
//- nightmare wins
//4. profile 
//- if a steam_id is provided as an argument - display the stats for that steam_id user



namespace BrokeAPI
{
    public class Frontend(XElement config)
    {
        private readonly XElement _config = config;

        public void ConfigureRoutes(IApplicationBuilder app)
        {
            app.UseRouting();

            _ = app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/heroes", async context =>
                {
                    var databaseEngine = GetDatabaseEngine();
                    var query = "SELECT hero, mmr, plays, wins, scepter_plays, scepter_wins, shard_plays, shard_wins FROM heroes ORDER BY hero ASC"; // Your SQL query here

                    var data = databaseEngine switch
                    {
                        "MSSQL" => await ExecuteQueryMSSQL(query),
                        "MySQL" => await ExecuteQueryMySQL(query),
                        _ => "Unsupported database engine"
                    };
                    await context.Response.WriteAsync(data);
                });

                // Endpoint for most played heroes
                endpoints.MapGet("/heroes/mostplayed", async context =>
                {
                    var databaseEngine = GetDatabaseEngine();
                    var query = "SELECT hero, plays FROM heroes ORDER BY plays DESC"; // Your SQL query here

                    var data = databaseEngine switch
                    {
                        "MSSQL" => await ExecuteQueryMSSQL(query),
                        "MySQL" => await ExecuteQueryMySQL(query),
                        _ => "Unsupported database engine"
                    };
                    await context.Response.WriteAsync(data);
                });

                endpoints.MapGet("/heroes/mostwins", async context =>
                {
                    var databaseEngine = GetDatabaseEngine();
                    var query = "SELECT hero, wins, plays, CASE WHEN plays = 0 THEN 0 ELSE (CAST(wins AS float) / plays) * 100 END AS winrate FROM heroes ORDER BY winrate DESC";

                    var data = databaseEngine switch
                    {
                        "MSSQL" => await ExecuteQueryMSSQL(query),
                        "MySQL" => await ExecuteQueryMySQL(query),
                        _ => "Unsupported database engine"
                    };
                    await context.Response.WriteAsync(data);
                });

                endpoints.MapGet("/heroes/mostmmr", async context =>
                {
                    var databaseEngine = GetDatabaseEngine();
                    var query = "SELECT hero, mmr FROM heroes ORDER BY mmr DESC"; // Your SQL query here

                    var data = databaseEngine switch
                    {
                        "MSSQL" => await ExecuteQueryMSSQL(query),
                        "MySQL" => await ExecuteQueryMySQL(query),
                        _ => "Unsupported database engine"
                    };
                    await context.Response.WriteAsync(data);
                });

                // Endpoint for top 200 players by MMR
                endpoints.MapGet("/players/top200mmr", async context =>
                {
                    var databaseEngine = GetDatabaseEngine();
                    var query = "SELECT TOP 200 steam_id, mmr, plays, wins FROM players WHERE mmr >= 5240 AND steam_id != 0 ORDER BY mmr DESC";

                    var data = databaseEngine switch
                    {
                        "MSSQL" => await ExecuteQueryMSSQL(query),
                        "MySQL" => await ExecuteQueryMySQL(query),
                        _ => "Unsupported database engine"
                    };
                    await context.Response.WriteAsync(data);
                });

                // Add more sub-endpoints for players...

                // Endpoint for difficulty stats
                endpoints.MapGet("/difficulty/{difficultyName}", async context =>
                {
                    var difficultyName = context.Request.RouteValues["difficultyName"].ToString();
                    // Implement the logic to fetch difficulty stats based on difficultyName
                });

                // Endpoint for player profile
                endpoints.MapGet("/profile/{steamId}", async context =>
                {
                    var steamId = context.Request.RouteValues["steamId"].ToString();
                    // Implement the logic to fetch player profile based on steamId
                });

                // Add more endpoints as needed
            });

            app.Run(async context =>
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Not Found");
                });
        }

        private async Task<string> GetHeroesData()
        {
            // Implement the SQL query to fetch heroes data
            // Example: "SELECT * FROM heroes"
            return "Heroes data here";
        }

        private string GetDatabaseEngine()
        {
            return _config.Element("Database")?.Element("Engine")?.Value ?? "MySQL";
        }

        private async Task<string> ExecuteQueryMySQL(string query)
        {
            var connectionString = $"Server={_config.Element("Database")?.Element("DatabaseIP")?.Value};" +
                                   $"Port={_config.Element("Database")?.Element("DatabasePort")?.Value};" +
                                   $"Database={_config.Element("Database")?.Element("DatabaseName")?.Value};" +
                                   $"User={_config.Element("Database")?.Element("DatabaseLogin")?.Value};" +
                                   $"Password={_config.Element("Database")?.Element("DatabasePassword")?.Value};";

            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            using var command = new MySqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            // Process the data reader and return results
            return "Results from MySQL query";
        }

        private async Task<string> ExecuteQueryMSSQL(string query)
        {
            var connectionString = $"Server={_config.Element("Database")?.Element("DatabaseIP")?.Value};" +
                                   $"Database={_config.Element("Database")?.Element("DatabaseName")?.Value};" +
                                   $"User Id={_config.Element("Database")?.Element("DatabaseLogin")?.Value};" +
                                   $"Password={_config.Element("Database")?.Element("DatabasePassword")?.Value};";

            StringBuilder result = new StringBuilder();
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand(query, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            // Dynamically building a string for each row
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var columnName = reader.GetName(i);
                                var columnValue = reader.IsDBNull(i) ? "null" : reader.GetValue(i).ToString();
                                result.Append($"{columnName}: {columnValue}, ");
                            }
                            result.AppendLine(); // End of row
                        }
                    }
                }
            }
            return result.ToString();
        }
    }
}