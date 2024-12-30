namespace CS2AICoach.Models
{
    public class MatchData
    {
        public string MapName { get; set; } = "";
        public float TickRate { get; set; }
        public List<GameEvent> Events { get; set; } = new();
        public Dictionary<long, PlayerStats> PlayerStats { get; set; } = new();
    }
}