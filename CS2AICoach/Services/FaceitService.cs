using System.Text.Json;

namespace CS2AICoach.Services
{
    public class FaceitService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BaseUrl = "https://open.faceit.com/data/v4";

        public FaceitService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        public async Task<string?> GetFaceitPlayerIdBySteamId(string steamId)
        {
            // Convert SteamID64 to format FACEIT expects
            var steam32 = (ulong.Parse(steamId) - 76561197960265728).ToString();

            var response = await _httpClient.GetAsync($"{BaseUrl}/players?game=cs2&game_player_id={steam32}");
            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.GetProperty("player_id").GetString();
        }

        public async Task<List<string>> GetPlayerMatchIds(string playerId, int limit = 20)
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/players/{playerId}/history?game=cs2&offset=0&limit={limit}");
            if (!response.IsSuccessStatusCode) return new List<string>();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var matches = doc.RootElement.GetProperty("items").EnumerateArray();

            return matches.Select(m => m.GetProperty("match_id").GetString())
                         .Where(id => id != null)
                         .Select(id => id!)
                         .ToList();
        }

        public async Task<string?> GetMatchDemoUrl(string matchId)
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/matches/{matchId}");
            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            try
            {
                var demoUrls = doc.RootElement.GetProperty("demo_urls").EnumerateArray();
                return demoUrls.FirstOrDefault().GetString();
            }
            catch
            {
                return null;
            }
        }

        public async Task<List<string>> DownloadPlayerDemos(string steamId, string downloadPath, int limit = 5)
        {
            var playerId = await GetFaceitPlayerIdBySteamId(steamId);
            if (playerId == null) return new List<string>();

            var matchIds = await GetPlayerMatchIds(playerId, limit);
            var downloadedFiles = new List<string>();

            foreach (var matchId in matchIds)
            {
                var demoUrl = await GetMatchDemoUrl(matchId);
                if (string.IsNullOrEmpty(demoUrl)) continue;

                var fileName = Path.Combine(downloadPath, $"{matchId}.dem.gz");
                try
                {
                    using var response = await _httpClient.GetAsync(demoUrl);
                    using var fs = new FileStream(fileName, FileMode.Create);
                    await response.Content.CopyToAsync(fs);
                    downloadedFiles.Add(fileName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to download demo {matchId}: {ex.Message}");
                }
            }

            return downloadedFiles;
        }
    }
}