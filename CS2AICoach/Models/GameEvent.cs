namespace CS2AICoach.Models
{
    public class GameEvent
    {
        public string Type { get; set; } = "";
        public float Tick { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
    }
}