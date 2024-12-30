using Microsoft.ML;
using Microsoft.ML.Data;
using CS2AICoach.Models;

namespace CS2AICoach.Services
{
    public class MLService
    {
        private readonly MLContext _mlContext;
        private ITransformer? _trainedModel;
        private readonly string _modelPath = "cs2_coach_model.zip";
        private bool _hasTrainedModel;

        public MLService()
        {
            _mlContext = new MLContext(seed: 1);
            _hasTrainedModel = File.Exists(_modelPath);
            if (_hasTrainedModel)
            {
                LoadModel();
            }
        }

        public MatchMLData ConvertToMLData(MatchData matchData, PlayerStats playerStats)
        {
            int roundCount = matchData.Events.Count(e => e.Type == "RoundStart");
            roundCount = roundCount == 0 ? 1 : roundCount; // Prevent division by zero

            var weaponAccuracies = playerStats.WeaponUsage
                .Select(w => w.TotalShots > 0 ? (float)w.Hits / w.TotalShots : 0)
                .DefaultIfEmpty(0)
                .Average();

            return new MatchMLData
            {
                KillsPerRound = playerStats.Kills / (float)roundCount,
                DeathsPerRound = playerStats.Deaths / (float)roundCount,
                HeadshotPercentage = (float)playerStats.HeadshotPercentage,
                AccuracyScore = weaponAccuracies * 100,
                UtilityScore = CalculateUtilityScore(matchData, playerStats),
                MapName = matchData.MapName,
                PerformanceScore = CalculateBasePerformanceScore(playerStats, roundCount)
            };
        }

        private float CalculateUtilityScore(MatchData matchData, PlayerStats playerStats)
        {
            // For now, returning a placeholder score
            return 50.0f;
        }

        private float CalculateBasePerformanceScore(PlayerStats stats, int roundCount)
        {
            float kdr = stats.Deaths > 0 ? (float)stats.Kills / stats.Deaths : stats.Kills;
            float kpr = (float)stats.Kills / roundCount;
            float hsPercent = (float)stats.HeadshotPercentage;

            return Math.Clamp(kdr * 20 + kpr * 30 + hsPercent * 0.5f, 0, 100);
        }

        public void TrainModel(IEnumerable<MatchMLData> trainingData)
        {
            var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

            var pipeline = _mlContext.Transforms.Categorical.OneHotEncoding(
                outputColumnName: "MapFeature",
                inputColumnName: "MapName")
                .Append(_mlContext.Transforms.Concatenate("Features",
                    "MapFeature", "KillsPerRound", "DeathsPerRound", "HeadshotPercentage",
                    "AccuracyScore", "UtilityScore"))
                .Append(_mlContext.Regression.Trainers.Sdca(labelColumnName: "PerformanceScore"));

            _trainedModel = pipeline.Fit(dataView);
            _hasTrainedModel = true;
            SaveModel();
        }

        public PerformanceAnalysis AnalyzePerformance(MatchMLData matchData)
        {
            float predictedScore;
            if (_hasTrainedModel && _trainedModel != null)
            {
                var predEngine = _mlContext.Model.CreatePredictionEngine<MatchMLData, MatchPrediction>(_trainedModel);
                var prediction = predEngine.Predict(matchData);
                predictedScore = prediction.PredictedPerformanceScore;
            }
            else
            {
                // If no trained model, use the base performance score
                predictedScore = matchData.PerformanceScore;
            }

            return new PerformanceAnalysis
            {
                PredictedScore = predictedScore,
                Metrics = new Dictionary<string, float>
                {
                    { "KillsPerRound", matchData.KillsPerRound },
                    { "HeadshotPercentage", matchData.HeadshotPercentage },
                    { "AccuracyScore", matchData.AccuracyScore },
                    { "UtilityScore", matchData.UtilityScore }
                },
                Insights = GenerateInsights(matchData)
            };
        }

        private List<string> GenerateInsights(MatchMLData data)
        {
            var insights = new List<string>();

            if (data.HeadshotPercentage < 30)
                insights.Add("Focus on crosshair placement and aim training");
            if (data.KillsPerRound < 0.5f)
                insights.Add("Work on positioning and trade opportunities");
            if (data.AccuracyScore < 20)
                insights.Add("Practice spray control and burst firing");

            if (!_hasTrainedModel)
            {
                insights.Add("Note: Analysis is based on basic statistics as no ML model is trained yet");
            }

            return insights;
        }

        private void SaveModel()
        {
            if (_trainedModel != null)
            {
                _mlContext.Model.Save(_trainedModel, null, _modelPath);
            }
        }

        private void LoadModel()
        {
            try
            {
                _trainedModel = _mlContext.Model.Load(_modelPath, out var _);
            }
            catch (Exception)
            {
                _hasTrainedModel = false;
                _trainedModel = null;
            }
        }
    }
}