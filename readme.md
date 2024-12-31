# CS2 AI Coach 🎮

An AI-powered Counter-Strike 2 coaching tool that analyzes demo files to provide personalized feedback and improvement suggestions using machine learning and LLM technology.

## Features 🚀

- **Demo Analysis**: Parse and analyze CS2 demo files (.dem, .dem.gz)
- **Performance Metrics**: Track detailed statistics including K/D/A, headshot percentage, weapon accuracy, and trading effectiveness
- **ML-Powered Insights**: Uses machine learning to evaluate player performance and identify areas for improvement
- **AI Coaching**: Leverages Ollama's LLM capabilities to provide detailed, context-aware coaching advice
- **Training Data Collection**: Build a customized dataset for more accurate performance predictions

## Prerequisites 📋

- [.NET 6.0](https://dotnet.microsoft.com/download/dotnet/6.0) or newer
- [Ollama](https://ollama.ai/) running locally (default: http://localhost:11434)
- CS2 demo files (.dem or .dem.gz format)

## Installation 🛠️

1. Clone the repository:
```bash
git clone https://github.com/yourusername/cs2-ai-coach.git
cd cs2-ai-coach
```

2. Install dependencies:
```bash
dotnet restore
```

3. Build the project:
```bash
dotnet build
```

## Usage 💡

### Analyze a Demo

Analyze a single demo file with AI coaching feedback:
```bash
dotnet run analyze path/to/demo.dem.gz "PlayerName"
```

Analyze multiple demos in a directory:
```bash
dotnet run analyze path/to/demos_folder "PlayerName"
```

Include subdirectories with recursive search:
```bash
dotnet run analyze path/to/demos_folder "PlayerName" --recursive
```

### Rate Matches for Training

Rate matches to build training data for the ML model:
```bash
dotnet run rate path/to/demo.dem.gz "PlayerName"
```

### Train the ML Model

Train the model using collected match data:
```bash
dotnet run train
```

### List Training Data

View all collected training data:
```bash
dotnet run list
```

## How It Works 🔍

### Demo Parsing
The application uses the DemoFile library to parse CS2 demo files, extracting detailed match statistics including:
- Player performance metrics
- Weapon usage and accuracy
- Kill/death events
- Round information

### Performance Analysis
Each match is analyzed using multiple factors:
- K/D ratio and kills per round
- Headshot percentage
- Weapon accuracy
- Trading effectiveness
- Survival rate
- Map-specific performance

### Machine Learning
The ML model is trained on rated matches to predict performance scores and identify improvement areas. Features include:
- Per-round statistics
- Accuracy metrics
- Map-specific patterns
- Trading and utility effectiveness

### AI Coaching
The Ollama integration provides detailed coaching advice by:
1. Analyzing match statistics and ML insights
2. Identifying player strengths and weaknesses
3. Providing specific improvement suggestions
4. Recommending practice routines and workshop maps

## Project Structure 📁

- `Program.cs`: Main application entry point and command handling
- `Services/`
  - `DemoParser.cs`: CS2 demo file parsing
  - `MLService.cs`: Machine learning model training and predictions
  - `OllamaService.cs`: LLM integration for coaching advice
  - `PerformanceRatingService.cs`: Match performance evaluation
  - `TrainingDataService.cs`: Training data management
- `Models/`
  - `MatchData.cs`: Match statistics and events
  - `PlayerStats.cs`: Player performance metrics
  - `WeaponStats.cs`: Weapon usage statistics

## Contributing 🤝

Contributions are welcome! Please feel free to submit a Pull Request.

## License 📄

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

```
CS2 AI Coach - An AI-powered Counter-Strike 2 coaching tool
Copyright (C) 2024

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.

## Acknowledgments 👏

- [DemoFile](https://github.com/StatsHelix/demoinfo) library for CS2 demo parsing
- [Ollama](https://ollama.ai/) for local LLM capabilities
- [ML.NET](https://dotnet.microsoft.com/apps/machinelearning-ai/ml-dotnet) for machine learning functionality
