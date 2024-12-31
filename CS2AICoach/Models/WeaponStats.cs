namespace CS2AICoach.Models
{
    public class WeaponStats
    {
        public string WeaponName { get; set; } = "";
        public int Kills { get; set; }
        public int TotalShots { get; set; }
        public int Hits { get; set; }

        public void RegisterHit()
        {
            Hits++;
        }

        public double GetAccuracy()
        {
            return TotalShots > 0 ? (double)Hits / TotalShots : 0;
        }
    }
}