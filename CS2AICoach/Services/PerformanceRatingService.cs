using CS2AICoach.Models;

namespace CS2AICoach.Services
{
    public class PerformanceRatingService
    {
        private class FlashState
        {
            public string Flasher { get; set; } = "";
            public float Duration { get; set; }
            public float Tick { get; set; }
            public int Sequence { get; set; }
        }

        private class KillState
        {
            public string Killer { get; set; } = "";
            public string Victim { get; set; } = "";
            public int KillerTeam { get; set; }
            public int VictimTeam { get; set; }
            public float Tick { get; set; }
            public int Sequence { get; set; }
        }

        private const int TRADE_WINDOW_TICKS = 128; // 2 seconds at 64 tick
        private const int FLASH_ASSIST_WINDOW_TICKS = 96; // 1.5 seconds at 64 tick

        public float CalculatePerformanceScore(MatchData matchData, PlayerStats playerStats)
        {
            int roundCount = matchData.Events.Count(e => e.Type == "RoundStart");
            roundCount = Math.Max(1, roundCount);

            // Core combat metrics (40 points total)
            var combatMetrics = CalculateCombatMetrics(matchData, playerStats, roundCount);
            float combatScore = (
                combatMetrics.KdrScore * 15 +      // 15 points
                combatMetrics.KprScore * 15 +      // 15 points
                combatMetrics.HsScore * 10         // 10 points
            );

            // Impact metrics (25 points total)
            var impactMetrics = CalculateImpactMetrics(matchData, playerStats, roundCount);
            float impactScore = (
                impactMetrics.OpeningDuelScore * 10 +     // 10 points
                impactMetrics.ClutchScore * 10 +          // 10 points
                impactMetrics.TradeScore * 5              // 5 points
            );

            // Utility and support (20 points total)
            var utilityMetrics = CalculateUtilityMetrics(matchData, playerStats, roundCount);
            float utilityScore = (
                utilityMetrics.FlashAssists * 7 +     // 7 points
                utilityMetrics.UtilityDamage * 7 +   // 7 points
                utilityMetrics.Support * 6           // 6 points
            );

            // Economy management (15 points total)
            float economyScore = 7.5f; // Default score while economy tracking is being implemented

            return Math.Clamp(
                combatScore + impactScore + utilityScore + economyScore,
                0, 100
            );
        }

        private (float KdrScore, float KprScore, float HsScore) CalculateCombatMetrics(
            MatchData matchData, PlayerStats playerStats, int roundCount)
        {
            float kdr = playerStats.Deaths > 0 ? (float)playerStats.Kills / playerStats.Deaths : playerStats.Kills;
            float kpr = (float)playerStats.Kills / roundCount;
            float hsPercentage = (float)playerStats.HeadshotPercentage / 100;

            return (
                KdrScore: ScaleValue(kdr, 0.5f, 2.0f),
                KprScore: ScaleValue(kpr, 0.4f, 1.2f),
                HsScore: ScaleValue(hsPercentage, 0.2f, 0.7f)
            );
        }

        private (float OpeningDuelScore, float ClutchScore, float TradeScore) CalculateImpactMetrics(
            MatchData matchData, PlayerStats playerStats, int roundCount)
        {
            var openingDuels = AnalyzeOpeningDuels(matchData, playerStats);
            var clutchStats = AnalyzeClutchRounds(matchData, playerStats);
            float tradingEffectiveness = CalculateTradingEffectiveness(matchData, playerStats);

            return (
                OpeningDuelScore: ScaleValue(openingDuels.WinRate, 0.3f, 0.7f),
                ClutchScore: ScaleValue(clutchStats.SuccessRate, 0.2f, 0.6f),
                TradeScore: ScaleValue(tradingEffectiveness, 0.3f, 0.7f)
            );
        }

        private (float WinRate, int Total) AnalyzeOpeningDuels(MatchData matchData, PlayerStats playerStats)
        {
            var roundGroups = matchData.Events
                .Where(e => e.Type == "RoundStart" || e.Type == "PlayerDeath")
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.SequenceNumber)
                .GroupBy(e => GetRoundStartTick(e, matchData));

            int totalFirstDuels = 0;
            int wonFirstDuels = 0;

            foreach (var round in roundGroups)
            {
                var firstKill = round
                    .Where(e => e.Type == "PlayerDeath")
                    .OrderBy(e => e.Tick)
                    .ThenBy(e => e.SequenceNumber)
                    .FirstOrDefault();

                if (firstKill == null) continue;

                string killerName = firstKill.Data["KillerName"].ToString() ?? "";
                string victimName = firstKill.Data["VictimName"].ToString() ?? "";

                if (killerName == playerStats.Name || victimName == playerStats.Name)
                {
                    totalFirstDuels++;
                    if (killerName == playerStats.Name)
                        wonFirstDuels++;
                }
            }

            return (
                WinRate: totalFirstDuels > 0 ? (float)wonFirstDuels / totalFirstDuels : 0,
                Total: totalFirstDuels
            );
        }

        private float GetRoundStartTick(GameEvent gameEvent, MatchData matchData)
        {
            if (gameEvent.Type == "RoundStart")
                return gameEvent.Tick;

            // Find the most recent round start before this event
            var roundStart = matchData.Events
                .Where(e => e.Type == "RoundStart" && e.Tick <= gameEvent.Tick)
                .OrderByDescending(e => e.Tick)
                .ThenByDescending(e => e.SequenceNumber)
                .FirstOrDefault();

            return roundStart?.Tick ?? 0;
        }

        private (float SuccessRate, int Total) AnalyzeClutchRounds(MatchData matchData, PlayerStats playerStats)
        {
            // Group events by round, using tick to determine round boundaries
            var rounds = new List<(List<GameEvent> Deaths, GameEvent? RoundEnd)>();
            var currentRoundDeaths = new List<GameEvent>();
            GameEvent? lastRoundStart = null;

            // Sort all events by tick and sequence
            var orderedEvents = matchData.Events
                .Where(e => e.Type is "PlayerDeath" or "RoundStart" or "RoundEnd")
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.SequenceNumber)
                .ToList();

            foreach (var evt in orderedEvents)
            {
                switch (evt.Type)
                {
                    case "RoundStart":
                        if (lastRoundStart != null)
                        {
                            // Save previous round
                            var roundEnd = matchData.Events
                                .Where(e => e.Type == "RoundEnd" &&
                                       e.Tick > lastRoundStart.Tick &&
                                       (evt == null || e.Tick < evt.Tick))
                                .OrderBy(e => e.Tick)
                                .FirstOrDefault();

                            rounds.Add((new List<GameEvent>(currentRoundDeaths), roundEnd));
                            currentRoundDeaths.Clear();
                        }
                        lastRoundStart = evt;
                        break;

                    case "PlayerDeath" when lastRoundStart != null:
                        currentRoundDeaths.Add(evt);
                        break;
                }
            }

            // Add the last round if it exists
            if (lastRoundStart != null && currentRoundDeaths.Any())
            {
                var finalRoundEnd = matchData.Events
                    .Where(e => e.Type == "RoundEnd" && e.Tick > lastRoundStart.Tick)
                    .OrderBy(e => e.Tick)
                    .FirstOrDefault();

                rounds.Add((new List<GameEvent>(currentRoundDeaths), finalRoundEnd));
            }

            int totalClutches = 0;
            int clutchesWon = 0;

            foreach (var (deaths, roundEnd) in rounds)
            {
                if (roundEnd == null) continue;

                bool wasClutch = false;
                bool clutchWon = false;

                // Create a snapshot of player states at each death
                for (int i = 0; i < deaths.Count; i++)
                {
                    var relevantDeaths = deaths.Take(i + 1).ToList();
                    if (IsClutchSituation(relevantDeaths, playerStats.Name))
                    {
                        wasClutch = true;
                        clutchWon = DidWinClutch(deaths.Skip(i), roundEnd, playerStats.Name);
                        break;
                    }
                }

                if (wasClutch)
                {
                    totalClutches++;
                    if (clutchWon) clutchesWon++;
                }
            }

            return (
                SuccessRate: totalClutches > 0 ? (float)clutchesWon / totalClutches : 0,
                Total: totalClutches
            );
        }

        private bool IsClutchSituation(List<GameEvent> deathEvents, string playerName)
        {
            Console.WriteLine($"Checking clutch situation for {playerName}");

            // Track player states
            var playerStates = new Dictionary<string, (bool IsAlive, int Team)>();
            int? targetPlayerTeam = null;

            // Process all deaths to build current state
            foreach (var death in deathEvents)
            {
                string killer = death.Data["KillerName"].ToString() ?? "";
                string victim = death.Data["VictimName"].ToString() ?? "";
                int killerTeam = Convert.ToInt32(death.Data["KillerTeam"]);
                int victimTeam = Convert.ToInt32(death.Data["VictimTeam"]);

                // Initialize or update killer state
                if (!playerStates.ContainsKey(killer))
                {
                    playerStates[killer] = (true, killerTeam);
                }

                // Initialize or update victim state
                if (!playerStates.ContainsKey(victim))
                {
                    playerStates[victim] = (false, victimTeam);
                }
                else
                {
                    playerStates[victim] = (false, playerStates[victim].Team);
                }

                // Track our target player's team
                if (killer == playerName || victim == playerName)
                {
                    targetPlayerTeam = killer == playerName ? killerTeam : victimTeam;
                }
            }

            if (!targetPlayerTeam.HasValue || !playerStates.ContainsKey(playerName))
            {
                Console.WriteLine($"Could not determine team for player {playerName}");
                return false;
            }

            // Count alive teammates and enemies
            int aliveTeammates = 0;
            int aliveEnemies = 0;

            foreach (var (player, (isAlive, team)) in playerStates)
            {
                if (!isAlive || player == playerName) continue;

                if (team == targetPlayerTeam)
                    aliveTeammates++;
                else
                    aliveEnemies++;
            }

            // Player must be alive for it to be a clutch
            bool playerIsAlive = playerStates[playerName].IsAlive;

            Console.WriteLine($"Clutch check result - Alive teammates: {aliveTeammates}, Enemies: {aliveEnemies}");
            return playerIsAlive && aliveTeammates == 0 && aliveEnemies > 0;
        }

        private bool DidWinClutch(IEnumerable<GameEvent> remainingDeaths, GameEvent roundEnd, string playerName)
        {
            // Check if player died in remaining deaths
            bool playerDied = remainingDeaths.Any(e =>
                e.Data["VictimName"].ToString() == playerName);

            // Get player's team
            var playerTeamEvent = remainingDeaths.FirstOrDefault(e =>
                e.Data["KillerName"].ToString() == playerName ||
                e.Data["VictimName"].ToString() == playerName);

            if (playerTeamEvent == null) return false;

            int playerTeam;
            if (playerTeamEvent.Data["KillerName"].ToString() == playerName)
                playerTeam = Convert.ToInt32(playerTeamEvent.Data["KillerTeam"]);
            else
                playerTeam = Convert.ToInt32(playerTeamEvent.Data["VictimTeam"]);

            int winningTeam = Convert.ToInt32(roundEnd.Data["Winner"]);

            return !playerDied && winningTeam == playerTeam;
        }


        private float CalculateFlashAssists(MatchData matchData, PlayerStats playerStats)
        {
            // Get all flash events ordered by tick and sequence
            var flashEvents = matchData.Events
                .Where(e => e.Type == "PlayerFlashed")
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.SequenceNumber)
                .Select(e => new
                {
                    FlashedPlayer = e.Data["VictimName"].ToString(),
                    Flasher = e.Data["AttackerName"].ToString(),
                    Duration = Convert.ToSingle(e.Data["FlashDuration"]),
                    Tick = e.Tick,
                    Sequence = e.SequenceNumber
                })
                .ToList();

            int flashAssists = 0;

            // Get all kill events
            var killEvents = matchData.Events
                .Where(e => e.Type == "PlayerDeath" &&
                           e.Data["KillerName"].ToString() == playerStats.Name)
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.SequenceNumber);

            foreach (var kill in killEvents)
            {
                string victim = kill.Data["VictimName"].ToString() ?? "";
                float killTick = kill.Tick;

                // Find relevant flash within the assist window
                var relevantFlash = flashEvents
                    .Where(f => f.Tick <= killTick &&
                               f.Tick >= killTick - FLASH_ASSIST_WINDOW_TICKS &&
                               f.FlashedPlayer == victim &&
                               f.Flasher == playerStats.Name &&
                               f.Duration >= 0.7f)
                    .OrderByDescending(f => f.Tick)
                    .ThenByDescending(f => f.Sequence)
                    .FirstOrDefault();

                if (relevantFlash != null)
                {
                    flashAssists++;
                    Console.WriteLine($"Flash assist found: {relevantFlash.Flasher} flashed {relevantFlash.FlashedPlayer} " +
                                    $"(Duration: {relevantFlash.Duration:F2}s) at tick {relevantFlash.Tick}, " +
                                    $"kill occurred at tick {killTick}");
                }
            }

            return flashAssists;
        }

        private float CalculateUtilityDamage(MatchData matchData, PlayerStats playerStats)
        {
            float totalUtilityDamage = 0;

            var utilityDamageEvents = matchData.Events
                .Where(e => e.Type == "PlayerHurt" &&
                           e.Data["AttackerName"].ToString() == playerStats.Name &&
                           IsUtilityWeapon(e.Data["Weapon"].ToString()));

            foreach (var damageEvent in utilityDamageEvents)
            {
                float damage = Convert.ToSingle(damageEvent.Data["Damage"]);
                totalUtilityDamage += damage;
            }

            return totalUtilityDamage;
        }

        private bool IsUtilityWeapon(string? weapon)
        {
            if (string.IsNullOrEmpty(weapon)) return false;
            var weaponLower = weapon.ToLower();
            return weaponLower is "hegrenade" or "molotov" or "incgrenade";
        }

        private float ScaleValue(float value, float min, float max)
        {
            return Math.Clamp((value - min) / (max - min), 0, 1);
        }

        private float CalculateAverageAccuracy(PlayerStats playerStats)
        {
            var totalHits = playerStats.WeaponUsage.Sum(w => w.Hits);
            var totalShots = playerStats.WeaponUsage.Sum(w => w.TotalShots);
            return totalShots > 0 ? (float)totalHits / totalShots : 0;
        }

        private float CalculateTradingEffectiveness(MatchData matchData, PlayerStats playerStats)
        {
            float successfulTrades = 0;
            float tradeOpportunities = 0;

            var deathEvents = matchData.Events
                .Where(e => e.Type == "PlayerDeath")
                .OrderBy(e => e.Tick)
                .ToList();

            foreach (var deathEvent in deathEvents)
            {
                string killerName = deathEvent.Data["KillerName"].ToString() ?? "";
                string victimName = deathEvent.Data["VictimName"].ToString() ?? "";
                float deathTick = deathEvent.Tick;

                if (victimName == playerStats.Name)
                {
                    var tradingKill = deathEvents
                        .Where(e => e.Tick > deathTick &&
                                  e.Tick <= deathTick + TRADE_WINDOW_TICKS &&
                                  e.Data["VictimName"].ToString() == killerName)
                        .FirstOrDefault();

                    if (tradingKill != null)
                        successfulTrades++;
                    tradeOpportunities++;
                }
                else if (killerName == playerStats.Name)
                {
                    var previousTeammateDeath = deathEvents
                        .Where(e => e.Tick < deathTick &&
                                  e.Tick >= deathTick - TRADE_WINDOW_TICKS &&
                                  e.Data["VictimName"].ToString() != playerStats.Name &&
                                  e.Data["KillerName"].ToString() == victimName)
                        .FirstOrDefault();

                    if (previousTeammateDeath != null)
                        successfulTrades++;
                }
            }

            return tradeOpportunities > 0 ? successfulTrades / tradeOpportunities : 0;
        }

        private (float FlashAssists, float UtilityDamage, float Support) CalculateUtilityMetrics(
            MatchData matchData, PlayerStats playerStats, int roundCount)
        {
            float flashAssists = CalculateFlashAssists(matchData, playerStats);
            float utilityDamage = CalculateUtilityDamage(matchData, playerStats);
            float supportScore = CalculateSupportScore(matchData, playerStats);

            return (
                FlashAssists: ScaleValue(flashAssists / roundCount, 0.1f, 0.5f),
                UtilityDamage: ScaleValue(utilityDamage / roundCount, 10, 30),
                Support: ScaleValue(supportScore, 0.2f, 0.6f)
            );
        }

        private float CalculateSupportScore(MatchData matchData, PlayerStats playerStats)
        {
            // Calculate support score based on assists and team flash avoidance
            return (float)playerStats.Assists / Math.Max(1, playerStats.Kills);
        }

        public Dictionary<string, float> GetDetailedMetrics(MatchData matchData, PlayerStats playerStats)
        {
            int roundCount = matchData.Events.Count(e => e.Type == "RoundStart");
            roundCount = Math.Max(1, roundCount);

            var metrics = new Dictionary<string, float>();

            // Combat Metrics
            metrics["KDR"] = playerStats.Deaths > 0 ? (float)playerStats.Kills / playerStats.Deaths : playerStats.Kills;
            metrics["KillsPerRound"] = (float)playerStats.Kills / roundCount;
            metrics["HeadshotPercentage"] = (float)playerStats.HeadshotPercentage / 100;

            // Impact Metrics
            var openingDuels = AnalyzeOpeningDuels(matchData, playerStats);
            var clutchStats = AnalyzeClutchRounds(matchData, playerStats);
            metrics["OpeningDuelWinRate"] = openingDuels.WinRate;
            metrics["ClutchSuccessRate"] = clutchStats.SuccessRate;
            metrics["TradingEffectiveness"] = CalculateTradingEffectiveness(matchData, playerStats);

            // Utility Metrics
            var utilityMetrics = CalculateUtilityMetrics(matchData, playerStats, roundCount);
            metrics["FlashAssists"] = utilityMetrics.FlashAssists;
            metrics["UtilityDamage"] = utilityMetrics.UtilityDamage;
            metrics["SupportScore"] = utilityMetrics.Support;

            // Weapon Metrics
            metrics["AverageAccuracy"] = CalculateAverageAccuracy(playerStats);

            return metrics;
        }
    }
}