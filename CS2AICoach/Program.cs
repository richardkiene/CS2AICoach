using DemoFile;
using CS2AICoach.Services;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            PrintHelp();
            return;
        }

        var command = args[0].ToLower();
        switch (command)
        {
            case "analyze":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: analyze <demo-file|directory> <player-name> [--recursive]");
                    return;
                }
                await ProcessDemos(args[1], args[2], "analyze", args.Contains("--recursive"));
                break;

            case "rate":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: rate <demo-file|directory> <player-name> [--recursive]");
                    return;
                }
                await ProcessDemos(args[1], args[2], "rate", args.Contains("--recursive"));
                break;

            case "train":
                await TrainModel();
                break;

            case "list":
                await ListTrainingData();
                break;

            default:
                Console.WriteLine($"Unknown command: {command}");
                PrintHelp();
                break;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("CS2 AI Coach");
        Console.WriteLine("Commands:");
        Console.WriteLine("  analyze <demo-file|directory> <player-name> [--recursive] - Analyze match(es) with AI coaching");
        Console.WriteLine("  rate <demo-file|directory> <player-name> [--recursive]    - Rate match(es) for training data");
        Console.WriteLine("  train                                                     - Train the ML model using collected data");
        Console.WriteLine("  list                                                      - List all training data");
        Console.WriteLine("  --help, -h                                               - Show this help message");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  Single demo:     dotnet run analyze demo.dem.gz \"PlayerName\"");
        Console.WriteLine("  Multiple demos:  dotnet run analyze demos_folder \"PlayerName\"");
        Console.WriteLine("  Recursive:       dotnet run analyze demos_folder \"PlayerName\" --recursive");
    }

    private static async Task ProcessDemos(string path, string playerName, string mode, bool recursive)
    {
        try
        {
            var demoFiles = GetDemoFiles(path, recursive);

            if (!demoFiles.Any())
            {
                Console.WriteLine("No demo files found!");
                return;
            }

            Console.WriteLine($"Found {demoFiles.Count()} demo file(s)");

            int processed = 0;
            foreach (var demoFile in demoFiles)
            {
                try
                {
                    Console.WriteLine($"\nProcessing {Path.GetFileName(demoFile)} ({++processed}/{demoFiles.Count()})");

                    if (mode == "analyze")
                        await AnalyzeMatch(demoFile, playerName);
                    else
                        await RateMatch(demoFile, playerName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing {demoFile}: {ex.Message}");
                    Console.WriteLine("Continuing with next demo...");
                }
            }

            Console.WriteLine($"\nProcessed {processed} demo files.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during bulk processing: {ex.Message}");
        }
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
                       f.EndsWith(".dem.gz", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task AnalyzeMatch(string demoPath, string playerName)
    {
        try
        {
            var demo = new CsDemoParser();
            var parser = new DemoParser(demoPath, demo);

            Console.WriteLine("Parsing demo file...");
            var matchData = await parser.ParseDemo();

            var ollamaService = new OllamaService(playerName: playerName);

            Console.WriteLine("Analyzing match data...");
            var analysis = await ollamaService.AnalyzeMatchData(matchData);

            Console.WriteLine("\nMatch Analysis:");
            Console.WriteLine(analysis);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error analyzing match: {ex.Message}");
        }
    }

    private static async Task RateMatch(string demoPath, string playerName)
    {
        try
        {
            // Parse demo
            Console.WriteLine("Parsing demo file...");
            var demo = new CsDemoParser();
            var parser = new DemoParser(demoPath, demo);
            var matchData = await parser.ParseDemo();

            // Verify player exists in match
            var player = matchData.PlayerStats.Values
                .FirstOrDefault(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

            if (player == null)
            {
                Console.WriteLine($"Player '{playerName}' not found in match. Available players:");
                foreach (var p in matchData.PlayerStats.Values)
                {
                    Console.WriteLine($"- {p.Name}");
                }
                return;
            }

            // Save training data with automated rating
            var trainingService = new TrainingDataService();
            await trainingService.SaveMatchDataAsync(matchData, playerName);

            // Display performance metrics for verification
            var ratingService = new PerformanceRatingService();
            var metrics = ratingService.GetDetailedMetrics(matchData, player);
            var score = ratingService.CalculatePerformanceScore(matchData, player);

            Console.WriteLine("\nCalculated Performance Metrics:");
            Console.WriteLine($"Overall Score: {score:F1}/100");
            foreach (var (metric, value) in metrics)
            {
                Console.WriteLine($"{metric}: {value:F2}");
            }

            Console.WriteLine("\nMatch data saved successfully!");
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

            Console.WriteLine("Loading training data...");
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
        }
    }
}