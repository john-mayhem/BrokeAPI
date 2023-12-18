using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Linq;

namespace BrokeAPI
{
    public class Backend(XElement config, int updateTime)
    {
        private readonly XElement _config = config;
        private System.Timers.Timer? _timer; // Make _timer nullable
        private readonly int _updateTime = updateTime;

        public void Start()
        {
            ScheduleDataRetrieval();
        }

        private void ScheduleDataRetrieval()
        {
            Console.WriteLine("Attempting scheduled data retrieval.");
            var currentTime = DateTime.Now;
            var nextHour = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, currentTime.Hour, 0, 0).AddHours(1);
            var delay = (int)(nextHour - currentTime).TotalMilliseconds;

            _timer = new System.Timers.Timer(delay);
            _timer.Elapsed += async (sender, e) => await OnTimedEvent();
            Console.WriteLine("3");
            _timer.Start();
        }

        private async Task OnTimedEvent()
        {
            Console.WriteLine("2");
            if (_timer != null)
            {
                _timer.Interval = _updateTime; // Use the UpdateTime from config
                Console.WriteLine("1");
                await RetrieveAndUpdateData();
            }
        }

        private async Task RetrieveAndUpdateData()
        {
            try
            {
                var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "output");
                if (Directory.Exists(outputDirectory))
                {
                    Directory.Delete(outputDirectory, true);
                }
                Directory.CreateDirectory(outputDirectory);
                var outputFilePath = Path.Combine(outputDirectory, "output.json");

                var authKey = _config.Element("DataHost")?.Element("AuthKey")?.Value;
                var serverLocation = _config.Element("DataHost")?.Element("ServerLocation")?.Value;

                using var client = new HttpClient();
                Console.WriteLine("Request sent.");
                var response = await client.GetStringAsync($"{serverLocation}/{authKey}/players.json");
                File.WriteAllText(outputFilePath, response);
                Console.WriteLine(response);

                // Further processing: Parse JSON, generate SQL, update database
            }
            catch (Exception ex)
            {
                // Log exception
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
