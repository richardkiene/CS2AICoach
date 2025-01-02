using CS2AICoach.Models;

namespace CS2AICoach.Services
{
    public class PerformanceRatingService
    {
        private const int TRADE_WINDOW_TICKS = 128; // 2 seconds at 64 tick
        private const int FLASH_ASSIST_WINDOW_TICKS = 96; // 1.5 seconds at 64 tick
        private const float SIGNIFICANT_FLASH_DURATION = 0.7f;

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
                utilityMetrics.UtilityDamage * 7 +    // 7 points
                utilityMetrics.Support * 6            // 6 points
            );

            // Economy management (15 points total)
            float economyScore = CalculateEconomyScore(matchData, playerStats, roundCount);

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
            var firstKillEvents = matchData.Events
                .Where(e => e.Type == "PlayerDeath")
                .GroupBy(e => matchData.Events
                    .Where(re => re.Type == "RoundStart" && re.Tick <= e.Tick)
                    .OrderByDescending(re => re.Tick)
                    .First().Tick)
                .Select(g => g.OrderBy(e => e.Tick).First());

            int totalFirstDuels = 0;
            int wonFirstDuels = 0;

            foreach (var firstKill in firstKillEvents)
            {
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

        private (float SuccessRate, int Total) AnalyzeClutchRounds(MatchData matchData, PlayerStats playerStats)
        {
            var roundEvents = matchData.Events
                .Where(e => e.Type == "PlayerDeath" || e.Type == "RoundStart" || e.Type == "RoundEnd")
                .GroupBy(e => matchData.Events
                    .Where(re => re.Type == "RoundStart" && re.Tick <= e.Tick)
                    .OrderByDescending(re => re.Tick)
                    .First().Tick);

            int totalClutches = 0;
            int clutchesWon = 0;

            foreach (var round in roundEvents)
            {
                var deathEvents = round.Where(e => e.Type == "PlayerDeath").ToList();
                var roundEnd = round.FirstOrDefault(e => e.Type == "RoundEnd");

                if (roundEnd == null) continue;

                bool wasClutch = false;
                bool clutchWon = false;

                for (int i = 0; i < deathEvents.Count; i++)
                {
                    if (IsClutchSituation(deathEvents, i, playerStats.Name))
                    {
                        wasClutch = true;
                        clutchWon = DidWinClutch(deathEvents.Skip(i), roundEnd, playerStats.Name);
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

        private bool IsClutchSituation(List<GameEvent> deathEvents, int currentIndex, string playerName)
        {
            var alivePlayers = new Dictionary<string, bool>();
            var playerTeam = DeterminePlayerTeam(deathEvents, playerName);

            foreach (var death in deathEvents)
            {
                string killer = death.Data["KillerName"].ToString() ?? "";
                string victim = death.Data["VictimName"].ToString() ?? "";

                if (!alivePlayers.ContainsKey(killer))
                    alivePlayers[killer] = true;
                if (!alivePlayers.ContainsKey(victim))
                    alivePlayers[victim] = true;
            }

            for (int i = 0; i <= currentIndex; i++)
            {
                string victim = deathEvents[i].Data["VictimName"].ToString() ?? "";
                alivePlayers[victim] = false;
            }

            int aliveTeammates = alivePlayers.Count(p =>
                p.Key != playerName &&
                p.Value &&
                IsOnSameTeam(deathEvents, p.Key, playerName));

            int aliveEnemies = alivePlayers.Count(p =>
                p.Value &&
                !IsOnSameTeam(deathEvents, p.Key, playerName));

            return alivePlayers[playerName] && aliveTeammates == 0 && aliveEnemies > 0;
        }

        private bool IsOnSameTeam(List<GameEvent> events, string player1, string player2)
        {
            var player1Event = events.FirstOrDefault(e =>
                (e.Data["KillerName"].ToString() == player1) ||
                (e.Data["VictimName"].ToString() == player1));

            var player2Event = events.FirstOrDefault(e =>
                (e.Data["KillerName"].ToString() == player2) ||
                (e.Data["VictimName"].ToString() == player2));

            if (player1Event == null || player2Event == null)
                return false;

            int player1Team = GetPlayerTeamFromEvent(player1Event, player1);
            int player2Team = GetPlayerTeamFromEvent(player2Event, player2);

            return player1Team == player2Team;
        }

        private int GetPlayerTeamFromEvent(GameEvent gameEvent, string playerName)
        {
            if (gameEvent.Data["KillerName"].ToString() == playerName)
                return Convert.ToInt32(gameEvent.Data["KillerTeam"]);
            return Convert.ToInt32(gameEvent.Data["VictimTeam"]);
        }

        private bool DidWinClutch(IEnumerable<GameEvent> remainingEvents, GameEvent roundEnd, string playerName)
        {
            bool playerDied = remainingEvents.Any(e =>
                e.Type == "PlayerDeath" &&
                e.Data["VictimName"].ToString() == playerName);

            if (playerDied)
                return false;

            var playerTeam = DeterminePlayerTeam(remainingEvents, playerName);
            return Convert.ToInt32(roundEnd.Data["Winner"]) == playerTeam;
        }

        private int DeterminePlayerTeam(IEnumerable<GameEvent> events, string playerName)
        {
            var playerEvent = events.FirstOrDefault(e =>
                e.Type == "PlayerDeath" &&
                (e.Data["KillerName"].ToString() == playerName ||
                 e.Data["VictimName"].ToString() == playerName));

            if (playerEvent == null)
                return 0;

            return playerEvent.Data["KillerName"].ToString() == playerName
                ? Convert.ToInt32(playerEvent.Data["KillerTeam"])
                : Convert.ToInt32(playerEvent.Data["VictimTeam"]);
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

        private float CalculateFlashAssists(MatchData matchData, PlayerStats playerStats)
        {
            var flashEvents = matchData.Events
                .Where(e => e.Type == "PlayerFlashed")
                .ToDictionary(
                    e => e.Tick,
                    e => new
                    {
                        FlashedPlayer = e.Data["VictimName"].ToString() ?? "",
                        Flasher = e.Data["AttackerName"].ToString() ?? "",
                        Duration = Convert.ToSingle(e.Data["FlashDuration"])
                    }
                );

            int flashAssists = 0;
            var killEvents = matchData.Events
                .Where(e => e.Type == "PlayerDeath" &&
                           e.Data["KillerName"].ToString() == playerStats.Name);

            foreach (var kill in killEvents)
            {
                string victim = kill.Data["VictimName"].ToString() ?? "";
                float killTick = kill.Tick;

                var relevantFlash = flashEvents
                    .Where(f => f.Key <= killTick &&
                               f.Key >= killTick - FLASH_ASSIST_WINDOW_TICKS &&
                               f.Value.FlashedPlayer == victim &&
                               f.Value.Flasher == playerStats.Name &&
                               f.Value.Duration >= SIGNIFICANT_FLASH_DURATION)
                    .OrderByDescending(f => f.Key)
                    .FirstOrDefault();

                if (relevantFlash.Value != null)
                    flashAssists++;
            }

            return flashAssists;
        }

        private float CalculateUtilityDamage(MatchData matchData, PlayerStats playerStats)
        {
            float totalUtilityDamage = 0;

            var utilityDamageEvents = matchData.Events
                .Where(e => e.Type == "PlayerHurt" &&
                           e.Data["AttackerName"].ToString() == playerStats.Name &&
                           IsUtilityWeapon(e.Data["Weapon"].ToString() ?? ""));

            foreach (var damageEvent in utilityDamageEvents)
            {
                float damage = Convert.ToSingle(damageEvent.Data["Damage"]);
                totalUtilityDamage += damage;
            }

            return totalUtilityDamage;
        }

        private bool IsUtilityWeapon(string weapon)
        {
            var weaponLower = weapon.ToLower();
            return weaponLower is "hegrenade" or "molotov" or "incgrenade";
        }

        private float CalculateSupportScore(MatchData matchData, PlayerStats playerStats)
        {
            return (float)playerStats.Assists / Math.Max(1, playerStats.Kills);
        }

        private float CalculateEconomyScore(MatchData matchData, PlayerStats playerStats, int roundCount)
        {
            float score = 7.5f; // Base score
            var buyRounds = matchData.Events
                .Where(e => e.Type == "BuyTimeEnd")
                .ToList();

            if (!buyRounds.Any())
                return score;

            float avgEquipmentValue = buyRounds
                .Where(r => r.Data.ContainsKey($"Equipment_{playerStats.Name}"))
                .Average(r => Convert.ToSingle(r.Data[$"Equipment_{playerStats.Name}"]));

            score += ScaleValue(avgEquipmentValue, 2000, 4500) * 7.5f;
            return score;
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
            metrics["AverageAccuracy"] = (float)(playerStats.WeaponUsage.Any() ?
                playerStats.WeaponUsage.Average(w => w.GetAccuracy()) : 0);

            return metrics;
        }

        private float ScaleValue(float value, float min, float max)
        {
            return Math.Clamp((value - min) / (max - min), 0, 1);
        }
    }
}