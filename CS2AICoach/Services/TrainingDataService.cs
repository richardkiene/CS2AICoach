using System.Text.Json;
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
                WriteIndented = true
            };
            _ratingService = new PerformanceRatingService();

            if (!Directory.Exists(_trainingDataPath))
            {
                Directory.CreateDirectory(_trainingDataPath);
            }
        }

        public async Task SaveMatchDataAsync(MatchData matchData, string playerName)
        {
            var player = matchData.PlayerStats.Values
                .FirstOrDefault(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

            if (player == null)
            {
                throw new ArgumentException($"Player {playerName} not found in match data");
            }

            var performanceScore = _ratingService.CalculatePerformanceScore(matchData, player);
            var metrics = _ratingService.GetDetailedMetrics(matchData, player);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var filename = Path.Combine(_trainingDataPath, $"match_{timestamp}.json");

            var trainingMatch = new TrainingMatch
            {
                MatchData = matchData,
                PlayerName = playerName,
                PerformanceRating = performanceScore,
                DetailedMetrics = metrics,
                Timestamp = DateTime.UtcNow
            };

            await File.WriteAllTextAsync(filename, JsonSerializer.Serialize(trainingMatch, _jsonOptions));
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