using System.Text.Json;
using System.Text.Json.Serialization;
using CS2AICoach.Models;

namespace CS2AICoach.Services
{
    public class TrainingDataService
    {
        private readonly string _trainingDataPath;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly PerformanceRatingService _ratingService;

        public TrainingDataService(string trainingDataPath = "training_data")
        {
            _trainingDataPath = trainingDataPath;
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                ReferenceHandler = ReferenceHandler.Preserve,  // Handle circular references
                MaxDepth = 128,  // Increase max depth
                Converters = { new GameEventConverter() }
            };
            _ratingService = new PerformanceRatingService();

            if (!Directory.Exists(_trainingDataPath))
            {
                Directory.CreateDirectory(_trainingDataPath);
            }
        }

        public async Task SaveMatchDataAsync(MatchData matchData, string playerName)
        {
            try
            {
                var player = matchData.PlayerStats.Values
                    .FirstOrDefault(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

                if (player == null)
                {
                    Console.WriteLine($"Warning: Player {playerName} not found in match data");
                    Console.WriteLine("Available players:");
                    foreach (var p in matchData.PlayerStats.Values)
                    {
                        Console.WriteLine($"- {p.Name}");
                    }
                    throw new ArgumentException($"Player {playerName} not found in match data");
                }

                Console.WriteLine($"Found player {player.Name} with {player.Kills} kills and {player.Deaths} deaths");

                // Create clean versions of all data structures without circular references
                var cleanMatchData = new MatchData
                {
                    MapName = matchData.MapName,
                    TickRate = matchData.TickRate,
                    Events = matchData.Events.Select(e => CleanGameEvent(e)).ToList(),
                    PlayerStats = new Dictionary<string, PlayerStats>()
                };

                // Deep copy player stats
                foreach (var kvp in matchData.PlayerStats)
                {
                    cleanMatchData.PlayerStats[kvp.Key] = CleanPlayerStats(kvp.Value);
                }

                var performanceScore = _ratingService.CalculatePerformanceScore(matchData, player);
                var metrics = _ratingService.GetDetailedMetrics(matchData, player);
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var filename = Path.Combine(_trainingDataPath, $"match_{timestamp}.json");

                var trainingMatch = new TrainingMatch
                {
                    MatchData = cleanMatchData,
                    PlayerName = playerName,
                    PerformanceRating = performanceScore,
                    DetailedMetrics = metrics,
                    Timestamp = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(trainingMatch, _jsonOptions);
                await File.WriteAllTextAsync(filename, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving match data: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }
        }

        private GameEvent CleanGameEvent(GameEvent original)
        {
            // Create a clean copy of the data dictionary
            var cleanData = new Dictionary<string, object>();
            foreach (var kvp in original.Data)
            {
                // Only copy primitive types and strings
                if (IsPrimitiveOrString(kvp.Value))
                {
                    cleanData[kvp.Key] = kvp.Value;
                }
                else
                {
                    // For complex objects, convert to string representation
                    cleanData[kvp.Key] = kvp.Value?.ToString() ?? "";
                }
            }

            // Use the Create factory method to make a new event
            return GameEvent.Create(
                original.Type,
                original.Tick,
                cleanData
            );
        }

        private PlayerStats CleanPlayerStats(PlayerStats original)
        {
            return new PlayerStats
            {
                Name = original.Name,
                SteamId = original.SteamId,
                Kills = original.Kills,
                Deaths = original.Deaths,
                Assists = original.Assists,
                HeadshotPercentage = original.HeadshotPercentage,
                WeaponUsage = original.WeaponUsage.Select(w => new WeaponStats
                {
                    WeaponName = w.WeaponName,
                    Kills = w.Kills,
                    TotalShots = w.TotalShots,
                    Hits = w.Hits
                }).ToList(),
                Data = original.Data.Where(kvp => IsPrimitiveOrString(kvp.Value))
                                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };
        }

        private bool IsPrimitiveOrString(object? value)
        {
            if (value == null) return true;
            var type = value.GetType();
            return type.IsPrimitive || type == typeof(string) || type == typeof(decimal);
        }

        public async Task<List<TrainingMatch>> LoadAllTrainingDataAsync()
        {
            var trainingMatches = new List<TrainingMatch>();
            var files = Directory.GetFiles(_trainingDataPath, "match_*.json");

            foreach (var file in files)
            {
                var json = await File.ReadAllTextAsync(file);
                var match = JsonSerializer.Deserialize<TrainingMatch>(json, _jsonOptions);
                if (match != null)
                {
                    trainingMatches.Add(match);
                }
            }

            return trainingMatches;
        }

        public async Task<IEnumerable<MatchMLData>> PrepareTrainingDataAsync(MLService mlService)
        {
            var trainingMatches = await LoadAllTrainingDataAsync();
            var trainingData = new List<MatchMLData>();

            foreach (var match in trainingMatches)
            {
                var playerStats = match.MatchData.PlayerStats.Values
                    .FirstOrDefault(p => p.Name.Equals(match.PlayerName, StringComparison.OrdinalIgnoreCase));

                if (playerStats != null)
                {
                    var mlData = mlService.ConvertToMLData(match.MatchData, playerStats);
                    // Override the calculated performance score with the rated one
                    mlData.PerformanceScore = match.PerformanceRating;
                    trainingData.Add(mlData);
                }
            }

            return trainingData;
        }
    }
}