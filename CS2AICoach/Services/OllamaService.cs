using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CS2AICoach.Models;

namespace CS2AICoach.Services
{
    public class OllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _model;
        private readonly string _playerName;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly MLService _mlService;

        public OllamaService(string baseUrl = "http://localhost:11434", string model = "llama3.2", string playerName = "")
        {
            _baseUrl = baseUrl;
            _model = model;
            _playerName = playerName;
            _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            _mlService = new MLService();
        }

        public async Task<string> AnalyzeMatchData(MatchData matchData)
        {
            var mainPlayer = FindMainPlayer(matchData);
            if (mainPlayer == null)
            {
                return $"Error: Player '{_playerName}' not found in match data. Available players: {string.Join(", ", matchData.PlayerStats.Values.Select(p => p.Name))}";
            }

            var mlData = _mlService.ConvertToMLData(matchData, mainPlayer);
            var analysis = _mlService.AnalyzePerformance(mlData);

            var prompt = GenerateEnhancedAnalysisPrompt(matchData, analysis, mainPlayer);
            var response = await GetOllamaResponse(prompt);
            return response;
        }

        private PlayerStats? FindMainPlayer(MatchData matchData)
        {
            if (string.IsNullOrEmpty(_playerName))
            {
                return matchData.PlayerStats.Values.FirstOrDefault();
            }

            return matchData.PlayerStats.Values.FirstOrDefault(p =>
                p.Name.Equals(_playerName, StringComparison.OrdinalIgnoreCase));
        }

        private string GenerateEnhancedAnalysisPrompt(MatchData matchData, PerformanceAnalysis mlAnalysis, PlayerStats mainPlayer)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You are an expert CS2 coach analyzing a match for player: {_playerName}");
            sb.AppendLine($"Map: {matchData.MapName}");

            // Add ML analysis
            sb.AppendLine("\nML Model Analysis:");
            sb.AppendLine($"Overall Performance Score: {mlAnalysis.PredictedScore:F1}/100");

            sb.AppendLine("\nKey Metrics:");
            foreach (var metric in mlAnalysis.Metrics)
            {
                sb.AppendLine($"- {metric.Key}: {metric.Value:F2}");
            }

            sb.AppendLine("\nML-Generated Insights:");
            foreach (var insight in mlAnalysis.Insights)
            {
                sb.AppendLine($"- {insight}");
            }

            // Add main player's detailed stats
            sb.AppendLine($"\nDetailed Statistics for {_playerName}:");
            sb.AppendLine($"K/D/A: {mainPlayer.Kills}/{mainPlayer.Deaths}/{mainPlayer.Assists}");
            sb.AppendLine($"Headshot %: {mainPlayer.HeadshotPercentage:F2}%");

            sb.AppendLine("\nWeapon Usage:");
            foreach (var weapon in mainPlayer.WeaponUsage)
            {
                var accuracy = weapon.TotalShots > 0
                    ? (double)weapon.Hits / weapon.TotalShots * 100
                    : 0;
                sb.AppendLine($"- {weapon.WeaponName}: {weapon.Kills} kills, {accuracy:F2}% accuracy");
            }

            // Add match context (other players' performance)
            sb.AppendLine("\nMatch Context (Team Performance):");
            foreach (var (_, stats) in matchData.PlayerStats.Where(p => p.Value.Name != _playerName))
            {
                sb.AppendLine($"- {stats.Name}: {stats.Kills}/{stats.Deaths}/{stats.Assists}");
            }

            sb.AppendLine("\nBased on this comprehensive analysis, please provide:");
            sb.AppendLine($"1. Specific strengths demonstrated by {_playerName} in this match");
            sb.AppendLine("2. Priority areas for improvement");
            sb.AppendLine("3. Concrete practice routines or workshop maps to address weaknesses");
            sb.AppendLine("4. Strategic adjustments recommended for the next match");

            return sb.ToString();
        }

        private async Task<string> GetOllamaResponse(string prompt)
        {
            var request = new OllamaRequest
            {
                Model = _model,
                Prompt = prompt,
                Stream = false
            };

            var requestJson = JsonSerializer.Serialize(request, _jsonOptions);
            var response = await _httpClient.PostAsync(
                "api/generate",
                new StringContent(requestJson, Encoding.UTF8, "application/json")
            );

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Ollama API request failed: {response.StatusCode}");
            }

            var result = await response.Content.ReadAsStringAsync();
            var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(result, _jsonOptions);
            return ollamaResponse?.Response ?? "No response from Ollama";
        }

        private class OllamaRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; } = "";

            [JsonPropertyName("prompt")]
            public string Prompt { get; set; } = "";

            [JsonPropertyName("stream")]
            public bool Stream { get; set; }
        }

        private class OllamaResponse
        {
            [JsonPropertyName("response")]
            public string? Response { get; set; }
        }
    }
}