using CS2AICoach.Models;

namespace CS2AICoach.Services
{
    public class PerformanceRatingService
    {

        public float CalculatePerformanceScore(MatchData matchData, PlayerStats playerStats)
        {
            int roundCount = matchData.Events.Count(e => e.Type == "RoundStart");
            roundCount = Math.Max(1, roundCount);

            // Core performance metrics with adjusted scaling
            float kdr = playerStats.Deaths > 0 ? (float)playerStats.Kills / playerStats.Deaths : playerStats.Kills;
            float kpr = (float)playerStats.Kills / roundCount;
            float survivalRate = 1.0f - ((float)playerStats.Deaths / roundCount);

            // Accuracy metrics
            float hsPercentage = (float)playerStats.HeadshotPercentage / 100;
            float averageAccuracy = CalculateAverageAccuracy(playerStats);
            float tradingEffectiveness = CalculateTradingEffectiveness(matchData, playerStats);

            // Adjusted scaling factors
            float kdrScore = ScaleValue(kdr, 0, 2) * 25;        // Max 25 points, scaled to 2.0 KDR
            float kprScore = ScaleValue(kpr, 0, 1.2f) * 20;     // Max 20 points, scaled to 1.2 KPR
            float survivalScore = survivalRate * 15;            // Max 15 points
            float hsScore = ScaleValue(hsPercentage, 0.2f, 0.7f) * 15;  // Max 15 points, scaled between 20-70%
            float accuracyScore = ScaleValue(averageAccuracy, 0.1f, 0.4f) * 15;  // Max 15 points, scaled between 10-40%
            float tradingScore = ScaleValue(tradingEffectiveness, 0.2f, 0.6f) * 10;  // Max 10 points, scaled between 20-60%

            // Combine scores and ensure it's between 0 and 100
            float totalScore = kdrScore + kprScore + survivalScore + hsScore + accuracyScore + tradingScore;
            return Math.Clamp(totalScore, 0, 100);
        }

        private float CalculateAverageAccuracy(PlayerStats playerStats)
        {
            var totalHits = playerStats.WeaponUsage.Sum(w => w.Hits);
            var totalShots = playerStats.WeaponUsage.Sum(w => w.TotalShots);
            return totalShots > 0 ? (float)totalHits / totalShots : 0;
        }

        private float CalculateTradingEffectiveness(MatchData matchData, PlayerStats playerStats)
        {
            const int TRADE_WINDOW_TICKS = 128; // Approximately 2 seconds at 64 tick
            float successfulTrades = 0;
            float tradeOpportunities = 0;

            // Sort events by tick for chronological processing
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
                    // Player died - look for revenge trade by teammates
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
                    // Player got a kill - check if it was a trade
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

        private float ScaleValue(float value, float min, float max)
        {
            return Math.Clamp((value - min) / (max - min), 0, 1);
        }

        public Dictionary<string, float> GetDetailedMetrics(MatchData matchData, PlayerStats playerStats)
        {
            int roundCount = matchData.Events.Count(e => e.Type == "RoundStart");
            roundCount = Math.Max(1, roundCount);

            float kdr = playerStats.Deaths > 0 ? (float)playerStats.Kills / playerStats.Deaths : playerStats.Kills;
            float kpr = (float)playerStats.Kills / roundCount;
            float survivalRate = 1.0f - ((float)playerStats.Deaths / roundCount);
            float hsPercentage = (float)playerStats.HeadshotPercentage / 100;
            float averageAccuracy = CalculateAverageAccuracy(playerStats);
            float tradingEffectiveness = CalculateTradingEffectiveness(matchData, playerStats);

            return new Dictionary<string, float>
            {
                { "KDR", kdr },
                { "KillsPerRound", kpr },
                { "SurvivalRate", survivalRate },
                { "HeadshotPercentage", hsPercentage },
                { "AverageAccuracy", averageAccuracy },
                { "TradingEffectiveness", tradingEffectiveness }
            };
        }
    }
}