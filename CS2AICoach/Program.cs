using DemoFile;
using CS2AICoach.Services;
using CS2AICoach.Models;
using DemoFile.Game.Cs;

class Program
{
    private static bool _verbose = false;

    static async Task Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            PrintHelp();
            return;
        }

        var command = args[0].ToLower();
        var options = args.Skip(1).Where(a => a.StartsWith("--")).ToArray();
        var nonOptions = args.Skip(1).Where(a => !a.StartsWith("--")).ToArray();

        // Set verbose mode if flag is present
        _verbose = options.Contains("--verbose");
        Logger.SetVerboseMode(_verbose);

        switch (command)
        {
            case "analyze":
            case "rate":
                if (nonOptions.Length < 2)
                {
                    Console.WriteLine($"Usage: {command} <demo-file|directory> <player-name|steam-id> [--recursive] [--use-steamid] [--verbose]");
                    return;
                }
                await ProcessDemos(nonOptions[0], nonOptions[1], command,
                    options.Contains("--recursive"),
                    options.Contains("--use-steamid"));
                break;

            case "train":
                await TrainModel();
                break;

            case "list":
                await ListTrainingData();
                break;

            case "download":
                if (nonOptions.Length < 1)
                {
                    Console.WriteLine("Usage: download <steam-id> [--limit <number>] [--verbose]");
                    return;
                }
                await DownloadDemos(nonOptions[0], GetLimitOption(options));
                break;

            default:
                Console.WriteLine($"Unknown command: {command}");
                PrintHelp();
                break;
        }
    }

    private static void Log(string message, bool alwaysShow = false)
    {
        if (_verbose || alwaysShow)
        {
            Console.WriteLine(message);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("CS2 AI Coach");
        Console.WriteLine("Commands:");
        Console.WriteLine("  analyze <demo-file|directory> <player-name|steam-id> [--recursive] [--use-steamid] [--verbose] - Analyze match(es)");
        Console.WriteLine("  rate <demo-file|directory> <player-name|steam-id> [--recursive] [--use-steamid] [--verbose]    - Rate match(es)");
        Console.WriteLine("  train                                                                                          - Train ML model");
        Console.WriteLine("  list                                                                                          - List training data");
        Console.WriteLine("  --help, -h                                                                                    - Show help");
        Console.WriteLine("\nOptions:");
        Console.WriteLine("  --verbose     Show detailed output during processing");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  By name:     dotnet run analyze demo.dem.gz \"PlayerName\"");
        Console.WriteLine("  By Steam ID: dotnet run analyze demo.dem.gz \"76561198386265483\" --use-steamid");
    }

    private static PlayerStats? FindPlayer(MatchData matchData, string identifier, bool useSteamId)
    {
        return useSteamId
            ? matchData.PlayerStats.Values.FirstOrDefault(p =>
                p.SteamId.Equals(identifier, StringComparison.OrdinalIgnoreCase))
            : matchData.PlayerStats.Values.FirstOrDefault(p =>
                p.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetDemoFiles(string path, bool recursive)
    {
        if (File.Exists(path))
        {
            return new[] { path };
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Path not found: {path}");
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.GetFiles(path, "*.*", searchOption)
                .Where(f => f.EndsWith(".dem", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".dem.gz", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".dem.bz2", StringComparison.OrdinalIgnoreCase));
    }

    private static void PrintAvailablePlayers(MatchData matchData)
    {
        Console.WriteLine("\nAvailable players in this match:");
        foreach (var p in matchData.PlayerStats.Values.OrderBy(p => p.Name))
        {
            Console.WriteLine($"- Name: {p.Name}");
            Console.WriteLine($"  Steam ID: {p.SteamId}");
            Console.WriteLine($"  Profile: https://steamcommunity.com/profiles/{p.SteamId}");
        }
    }

    private static async Task ProcessDemos(string path, string playerIdentifier, string mode, bool recursive, bool useSteamId)
    {
        try
        {
            var demoFiles = GetDemoFiles(path, recursive);

            if (!demoFiles.Any())
            {
                Console.WriteLine("No demo files found!");
                return;
            }

            Log($"Found {demoFiles.Count()} demo file(s)", true);
            Log($"Looking for player by {(useSteamId ? "Steam ID" : "name")}: {playerIdentifier}");

            int processed = 0;
            foreach (var demoFile in demoFiles)
            {
                try
                {
                    Log($"\nProcessing {Path.GetFileName(demoFile)} ({++processed}/{demoFiles.Count()})");
                    Console.Write($"Processing {Path.GetFileName(demoFile)}... ");

                    if (mode == "analyze")
                        await AnalyzeMatch(demoFile, playerIdentifier, useSteamId);
                    else
                        await RateMatch(demoFile, playerIdentifier, useSteamId);

                    Console.WriteLine("Done");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    Log($"Detailed error: {ex}");
                    Console.WriteLine("Continuing with next demo...");
                }
            }

            Log($"\nProcessed {processed} demo files.", true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Log($"Detailed error: {ex}");
        }
    }

    private static async Task AnalyzeMatch(string demoPath, string playerIdentifier, bool useSteamId)
    {
        try
        {
            var demo = new CsDemoParser();
            var parser = new DemoParser(demoPath, demo);

            Log("Parsing demo file...");
            var matchData = await parser.ParseDemo();

            var player = FindPlayer(matchData, playerIdentifier, useSteamId);
            if (player == null)
            {
                Console.WriteLine($"\nPlayer not found using {(useSteamId ? "Steam ID" : "name")}: {playerIdentifier}");
                PrintAvailablePlayers(matchData);
                return;
            }

            var ollamaService = new OllamaService(playerName: player.Name);

            Log("Analyzing match data...");
            var analysis = await ollamaService.AnalyzeMatchData(matchData);

            Console.WriteLine("\nMatch Analysis:");
            Console.WriteLine(analysis);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error analyzing match: {ex.Message}");
        }
    }

    private static async Task RateMatch(string demoPath, string playerIdentifier, bool useSteamId)
    {
        try
        {
            Log($"Parsing demo file: {Path.GetFileName(demoPath)}");
            var demo = new CsDemoParser();
            var parser = new DemoParser(demoPath, demo);
            MatchData matchData = await parser.ParseDemo();

            var player = FindPlayer(matchData, playerIdentifier, useSteamId);
            if (player == null)
            {
                Console.WriteLine($"\nPlayer not found using {(useSteamId ? "Steam ID" : "name")}: {playerIdentifier}");
                PrintAvailablePlayers(matchData);
                return;
            }

            var trainingService = new TrainingDataService();
            await trainingService.SaveMatchDataAsync(matchData, player.Name);

            var ratingService = new PerformanceRatingService();
            var metrics = ratingService.GetDetailedMetrics(matchData, player);
            var score = ratingService.CalculatePerformanceScore(matchData, player);

            // Print summary
            Console.WriteLine($"\n{Path.GetFileName(demoPath)} - Performance Summary");
            Console.WriteLine($"Player: {player.Name}");
            Console.WriteLine($"Score: {score:F1}/100");

            Console.WriteLine("\nDetailed Metrics:");
            foreach (var (metric, value) in metrics)
            {
                Console.WriteLine($"{metric}: {value:F2}");
            }

            ListPlayers(matchData);

            Console.WriteLine("Match data saved successfully!");

        }
        catch (Exception ex)
        {
            throw new Exception($"Error processing match: {ex.Message}");
        }
    }

    private static async Task TrainModel()
    {
        try
        {
            var trainingService = new TrainingDataService();
            var mlService = new MLService();

            Log("Loading training data...");
            var trainingData = await trainingService.PrepareTrainingDataAsync(mlService);
            var dataList = trainingData.ToList();

            if (dataList.Count == 0)
            {
                Console.WriteLine("No training data available. Rate some matches first!");
                return;
            }

            Console.WriteLine($"Found {dataList.Count} training matches.");
            Console.Write("Proceed with training? (y/n): ");
            if (Console.ReadLine()?.ToLower() != "y")
            {
                return;
            }

            Console.WriteLine("Training model...");
            mlService.TrainModel(dataList);
            Console.WriteLine("Model trained and saved successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error training model: {ex.Message}");
            Log($"Detailed error: {ex}");
        }
    }

    private static async Task ListTrainingData()
    {
        try
        {
            var trainingService = new TrainingDataService();
            var matches = await trainingService.LoadAllTrainingDataAsync();

            Console.WriteLine("\nTraining Data:");
            Console.WriteLine("-------------");
            foreach (var match in matches.OrderByDescending(m => m.Timestamp))
            {
                var player = match.MatchData.PlayerStats.Values
                    .FirstOrDefault(p => p.Name.Equals(match.PlayerName, StringComparison.OrdinalIgnoreCase));

                if (player != null)
                {
                    Console.WriteLine($"Date: {match.Timestamp:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"Player: {match.PlayerName}");
                    Log($"Steam ID: {player.SteamId}");
                    Log($"Profile: https://steamcommunity.com/profiles/{player.SteamId}");
                    Console.WriteLine($"Map: {match.MatchData.MapName}");
                    Console.WriteLine($"Rating: {match.PerformanceRating}/100");
                    Console.WriteLine($"K/D/A: {player.Kills}/{player.Deaths}/{player.Assists}");
                    Console.WriteLine("-------------");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error listing training data: {ex.Message}");
            Log($"Detailed error: {ex}");
        }
    }

    private static int GetLimitOption(string[] options)
    {
        var limitOption = options.FirstOrDefault(o => o.StartsWith("--limit="));
        if (limitOption == null) return 5;

        var limitStr = limitOption.Split('=')[1];
        return int.TryParse(limitStr, out int limit) ? limit : 5;
    }

    private static async Task DownloadDemos(string steamId, int limit)
    {
        var apiKey = Environment.GetEnvironmentVariable("FACEIT_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.WriteLine("Please set FACEIT_API_KEY environment variable");
            return;
        }

        var downloadPath = Path.Combine(Directory.GetCurrentDirectory(), "downloads");
        Directory.CreateDirectory(downloadPath);

        Console.WriteLine($"Downloading up to {limit} demos for Steam ID: {steamId}");
        var faceitService = new FaceitService(apiKey);

        var files = await faceitService.DownloadPlayerDemos(steamId, downloadPath, limit);
        if (files.Count == 0)
        {
            Console.WriteLine("No demos found or player not found on FACEIT");
            return;
        }

        Console.WriteLine($"\nDownloaded {files.Count} demos to {downloadPath}:");
        foreach (var file in files)
        {
            Console.WriteLine($"- {Path.GetFileName(file)}");
        }
    }

    private static void ListPlayers(MatchData matchData)
    {
        var playerTeams = matchData.Events
            .Where(e => e.Type == "PlayerSpawn" &&
                       e.Data.ContainsKey("PlayerName") &&
                       e.Data.ContainsKey("Team"))
            .GroupBy(e => e.Data["PlayerName"].ToString() ?? "Unknown")
            .ToDictionary(g => g.Key, g =>
            {
                var team = g.First().Data["Team"];
                // Handle both integer and string team cases
                if (team is int i)
                {
                    return i;
                }
                else if (team is string s)
                {
                    return s == "Terrorist" ? 2 :
                           s == "CounterTerrorist" ? 3 : 0;
                }
                else if (team is CCSTeam)
                {
                    CCSTeam curTeam = (CCSTeam)team;
                    return curTeam.TeamNum;
                }
                return 0;
            });

        if (!playerTeams.Any())
        {
            Console.WriteLine("\nNo team information found in demo");
            return;
        }

        var teamPlayers = playerTeams
            .GroupBy(pt => pt.Value)
            .ToDictionary(g => g.Key, g => g.Select(pt => pt.Key).ToList());

        Console.WriteLine("\nTeams and Players:");
        foreach (var team in teamPlayers.OrderBy(t => t.Key))
        {
            Console.WriteLine($"\nTeam {team.Key}:");
            foreach (var playerName in team.Value.OrderBy(p => p))
            {
                var stats = matchData.PlayerStats.Values.FirstOrDefault(p => p.Name == playerName);
                if (stats != null)
                {
                    Console.WriteLine($"  {playerName} (Steam ID: {stats.SteamId})");
                }
            }
        }
    }
}