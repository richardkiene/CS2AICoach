namespace CS2AICoach.Models
{
    public class PlayerStats
    {
        public string Name { get; set; } = "";
        public string SteamId { get; set; } = "";
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public double HeadshotPercentage { get; set; }
        public List<WeaponStats> WeaponUsage { get; set; } = new();
        public Dictionary<string, object> Data { get; set; } = new();
    }
}