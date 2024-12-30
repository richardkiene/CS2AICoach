using Microsoft.ML;
using Microsoft.ML.Data;

namespace CS2AICoach.Services
{
    public class MatchMLData
    {
        [LoadColumn(0)]
        public float KillsPerRound { get; set; }

        [LoadColumn(1)]
        public float DeathsPerRound { get; set; }

        [LoadColumn(2)]
        public float HeadshotPercentage { get; set; }

        [LoadColumn(3)]
        public float AccuracyScore { get; set; }

        [LoadColumn(4)]
        public float UtilityScore { get; set; }

        [LoadColumn(5)]
        public string? MapName { get; set; }

        [LoadColumn(6)]
        public float PerformanceScore { get; set; }  // Label
    }
}