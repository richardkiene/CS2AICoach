using Microsoft.ML;
using Microsoft.ML.Data;

namespace CS2AICoach.Models
{
    public class MatchPrediction
    {
        [ColumnName("Score")]
        public float PredictedPerformanceScore { get; set; }
    }
}