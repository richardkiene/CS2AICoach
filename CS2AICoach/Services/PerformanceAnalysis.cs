using System.Text;

namespace CS2AICoach.Services
{
    public class PerformanceAnalysis
    {
        public float PredictedScore { get; set; }
        public Dictionary<string, float>? Metrics { get; set; }
        public List<string>? Insights { get; set; }

        public string ToLlamaPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Based on ML analysis of the match data:\n");
            sb.AppendLine($"Overall Performance Score: {PredictedScore:F1}/100\n");

            sb.AppendLine("Key Metrics:");
            if (Metrics != null)
            {
                foreach (var metric in Metrics)
                {
                    sb.AppendLine($"- {metric.Key}: {metric.Value:F2}");
                }
            }

            sb.AppendLine("\nML-Generated Insights:");
            if (Insights != null)
            {
                foreach (var insight in Insights)
                {
                    sb.AppendLine($"- {insight}");
                }
            }

            sb.AppendLine("\nPlease provide detailed coaching advice based on these metrics and insights.");
            return sb.ToString();
        }
    }
}