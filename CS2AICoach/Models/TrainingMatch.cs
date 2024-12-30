namespace CS2AICoach.Models
{
    public class TrainingMatch
    {
        public MatchData MatchData { get; set; } = new();
        public string PlayerName { get; set; } = "";
        public float PerformanceRating { get; set; }
        public Dictionary<string, float> DetailedMetrics { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }
}