using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;


//Next, i will create 2 additional files: 
//- Backend.cs
//- Frontend.cs 
//
//Backend.cs: 
//- Each hour (exact time will be in the config.xml) it will contact Google Firebase, download .json database file,
//parse the file and upload it's contents to a MySQL or MSSQL /database (dbo engine, credentials will also be stored in config.xml) 
//
//Frontend.cs: 
//- Will repond to outside GET? Post? requests? i'm not quite sure.
//But it will make a query to the MySQL/MSSQL database, get the data, and reply, like an API would.
//I assume it has to use endpoints 



namespace BrokeAPI
{
    public class Program
    {
        private static XElement? _config;

        public static async Task Main(string[] args)
        {

            Log.Logger = new LoggerConfiguration().WriteTo.File(Path.Combine(Directory.GetCurrentDirectory(), "logs", "log-.txt"), rollingInterval: RollingInterval.Day).CreateLogger();
            ArgumentNullException.ThrowIfNull(args);
            Log.Information("Application starting up");

            LoadConfiguration();

            if (_config != null)
            {
                Log.Information("Backend system starting.");
                Console.WriteLine("Backend system starting.");
                var updateTimeStr = _config.Element("Backend")?.Element("UpdateTime")?.Value;
                if (int.TryParse(updateTimeStr, out int updateTime))
                {
                    var backend = new Backend(_config, updateTime);
                    backend.Start();
                    Log.Information("Backend started with update time: {UpdateTime}", updateTime);
                }
                else
                {
                    Console.WriteLine("Invalid UpdateTime in configuration. Exiting.");
                    Log.Fatal("Invalid UpdateTime in configuration. Exiting application.");
                    return;
                }
            }
            else
            {
                Log.Fatal("Configuration not loaded. Exiting application.");
                Console.WriteLine("Configuration not loaded. Exiting application.");
                return;
            }

            try
            {
                Console.WriteLine("Starting Webserver.");
                Log.Information("Starting Webserver at {Url}", $"http://{_config?.Element("WebServer")?.Element("IP")?.Value ?? "*"}:{_config?.Element("WebServer")?.Element("Port")?.Value ?? "5000"}");
                await StartWebServer();
                Log.Information("Webserver started successfully.");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
                Console.WriteLine($"Host terminated unexpectedly: {ex.Message}");
            }
            finally
            {
                Log.Information("Shutting down");
                Log.CloseAndFlush();
            }
        }

        private static void LoadConfiguration()
        {
            Console.WriteLine("Attempting to read Configuration.");
            Log.Information("Attempting to read Configuration.");
            var configDirectory = Path.Combine(Directory.GetCurrentDirectory(), "config");
            var configPath = Path.Combine(configDirectory, "config.xml");

            if (!File.Exists(configPath))
            {
                Console.WriteLine("Configuration file not found. Creating with default values.");
                Log.Warning("Configuration file not found. Creating with default values.");
                if (!Directory.Exists(configDirectory))
                {
                    Directory.CreateDirectory(configDirectory);
                }

                // Create a default configuration XML with default update time in milliseconds
                _config = new XElement("Configuration",
                    new XElement("WebServer",
                        new XElement("IP", "127.0.0.1"),
                        new XElement("Port", "5000")),
                    new XElement("Database",
                        new XElement("Engine", "MSSQL"),
                        new XElement("DatabaseIP", "127.0.0.1"),
                        new XElement("DatabasePort", "3306"),
                        new XElement("DatabaseName", "default"),
                        new XElement("DatabaseLogin", "root"),
                        new XElement("DatabasePassword", "password")),
                    new XElement("Backend",
                        new XElement("UpdateTime", "3600000")), // Default update time (e.g., 3600000 ms = 1 hour)
                    new XElement("DataHost",
                        new XElement("AuthKey", "00000000000000000000000000000000000000000"),
                        new XElement("ServerLocation", "data.host.com"))
                );

                _config.Save(configPath);
            }
            else
            {
                try
                {
                    _config = XElement.Load(configPath);
                    Log.Information("Configuration file loaded successfully.");
                    // Parsing WebServer Configuration
                    var webServerConfig = _config.Element("WebServer");
                    var ip = webServerConfig?.Element("IP")?.Value;
                    var port = webServerConfig?.Element("Port")?.Value;

                    // Parsing Database Configuration
                    var databaseConfig = _config.Element("Database");
                    var databaseEngine = databaseConfig?.Element("Engine")?.Value;
                    var databaseIP = databaseConfig?.Element("DatabaseIP")?.Value;
                    var databasePort = databaseConfig?.Element("DatabasePort")?.Value;
                    var databaseName = databaseConfig?.Element("DatabaseName")?.Value;
                    var databaseLogin = databaseConfig?.Element("DatabaseLogin")?.Value;
                    var databasePassword = databaseConfig?.Element("DatabasePassword")?.Value;

                    // Parsing Backend Configuration
                    var backendConfig = _config.Element("Backend");
                    var updateTimeStr = backendConfig?.Element("UpdateTime")?.Value;
                    if (!int.TryParse(updateTimeStr, out int updateTime))
                    {
                        Console.WriteLine("Invalid or missing UpdateTime value in configuration. Using default value.");
                        updateTime = 3600000; // Default value in milliseconds (e.g., 1 hour)
                    }

                    var datahostconfig = _config.Element("DataHost");
                    var authhost = databaseConfig?.Element("AuthKey")?.Value;
                    var serverlocation = databaseConfig?.Element("ServerLocation")?.Value;

                    Console.WriteLine("Configuration loaded.");
                }
                catch (XmlException ex)
                {
                    Console.WriteLine($"Error reading configuration file: {ex.Message}");
                    Log.Error(ex, "Error reading configuration file.");
                    Console.WriteLine("System halted.");
                    Log.Information("System halted. Exiting application.");
                    Console.ReadKey();
                    Environment.Exit(1);
                }
            }
        }

        private static async Task StartWebServer()
        {
            // Retrieve IP and Port from the configuration
            var webServerConfig = _config?.Element("WebServer");
            var ip = webServerConfig?.Element("IP")?.Value ?? "*";
            var port = webServerConfig?.Element("Port")?.Value ?? "5000";
            var url = $"http://{ip}:{port}";
            Console.WriteLine("Webserver started at: " + url);

            if (_config == null)
            {
                Console.WriteLine("Configuration is null, cannot start webserver.");
                return;
            }

            var host = Host.CreateDefaultBuilder()
                .UseSerilog() // Use Serilog for logging
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.Configure(app =>
                    {
                        var frontend = new Frontend(_config);
                        frontend.ConfigureRoutes(app);
                    })
                    .UseUrls(url);
                }).Build();
            await host.RunAsync();
        }

    }
}
