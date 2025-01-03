namespace CS2AICoach.Models
{
    public class MatchData
    {
        public string MapName { get; set; }
        public float TickRate { get; set; }
        public List<GameEvent> Events { get; set; } = new List<GameEvent>();
        public Dictionary<string, PlayerStats> PlayerStats { get; set; } = new Dictionary<string, PlayerStats>();
    }
}