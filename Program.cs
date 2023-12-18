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
            ArgumentNullException.ThrowIfNull(args);

            LoadConfiguration();
            Console.WriteLine("Loggin system started.");
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(Path.Combine(Directory.GetCurrentDirectory(), "logs", "log-.txt"), rollingInterval: RollingInterval.Day)
                .CreateLogger();

            if (_config != null)
            {
                Console.WriteLine("Backend system starting.");
                var updateTimeStr = _config.Element("Backend")?.Element("UpdateTime")?.Value;
                if (int.TryParse(updateTimeStr, out int updateTime))
                {
                    var backend = new Backend(_config, updateTime);
                    backend.Start();
                }
                else
                {
                    Console.WriteLine("Invalid UpdateTime in configuration.");
                    // Handle error
                }
            }
            else
            {
                Console.WriteLine("Configuration not loaded. Exiting application.");
                return;
            }

            try
            {
                Console.WriteLine("Starting Webserver.");
                Log.Information("Starting web host");
                await StartWebServer();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }


        }

        private static void LoadConfiguration()
        {
            Console.WriteLine("Attempting to read Configuration.");
            var configDirectory = Path.Combine(Directory.GetCurrentDirectory(), "config");
            var configPath = Path.Combine(configDirectory, "config.xml");

            if (!File.Exists(configPath))
            {
                Console.WriteLine("Configuration file not found. Creating with default values.");
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
                        new XElement("DatabaseIP", "127.0.0.1"),
                        new XElement("DatabasePort", "3306"),
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

                    // Parsing WebServer Configuration
                    var webServerConfig = _config.Element("WebServer");
                    var ip = webServerConfig?.Element("IP")?.Value;
                    var port = webServerConfig?.Element("Port")?.Value;

                    // Parsing Database Configuration
                    var databaseConfig = _config.Element("Database");
                    var databaseIP = databaseConfig?.Element("DatabaseIP")?.Value;
                    var databasePort = databaseConfig?.Element("DatabasePort")?.Value;
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
                    Console.WriteLine("System halted.");
                    Console.ReadKey(); // Wait for the user to press a key
                    Environment.Exit(1); // Exit code 1 to indicate an error
                }
            }
        }

        private static async Task StartWebServer()
        {
            // Retrieve IP and Port from the configuration
            var ip = _config?.Element("IP")?.Value ?? "*";
            var port = _config?.Element("Port")?.Value ?? "5000";
            var url = $"http://{ip}:{port}";

            Console.Write("Webserver started at:  " + url);

            var host = Host.CreateDefaultBuilder()
              .UseSerilog() // Use Serilog for logging
              .ConfigureWebHostDefaults(webBuilder =>
              {
                  webBuilder.Configure(app =>
                  {
                      // Static file serving setup remains the same
                      // ...

                      app.UseRouting();

                      app.UseEndpoints(endpoints =>
                      {
                          endpoints.MapGet("/", async context =>
                          {
                              await context.Response.WriteAsync("");
                          });
                          // Additional endpoints for API
                      });
                  })
                    .UseUrls(url);
              }).Build();

            await host.RunAsync();
        }
    }
}
