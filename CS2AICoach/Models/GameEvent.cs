namespace CS2AICoach.Models
{
    public class GameEvent
    {
        private static readonly Dictionary<float, int> _sequenceNumbers = new();
        private static readonly object _lockObj = new();

        public string Type { get; set; } = string.Empty;
        public float Tick { get; set; }
        public int SequenceNumber { get; private set; }
        public Dictionary<string, object> Data { get; }
        public Guid Id { get; }

        private GameEvent()
        {
            Id = Guid.NewGuid();
            Data = new Dictionary<string, object>();
        }

        public static GameEvent Create(string type, float tick, Dictionary<string, object> data)
        {
            lock (_lockObj)
            {
                if (!_sequenceNumbers.ContainsKey(tick))
                {
                    _sequenceNumbers[tick] = 0;
                }
                int sequence = _sequenceNumbers[tick]++;

                var gameEvent = new GameEvent
                {
                    Type = type,
                    Tick = tick,
                    SequenceNumber = sequence
                };

                foreach (var kvp in data)
                {
                    gameEvent.Data[kvp.Key] = kvp.Value;
                }

                return gameEvent;
            }
        }

        public static void ClearSequenceNumbers()
        {
            lock (_lockObj)
            {
                _sequenceNumbers.Clear();
            }
        }

        public GameEvent Clone()
        {
            var clone = new GameEvent
            {
                Type = this.Type,
                Tick = this.Tick,
                SequenceNumber = this.SequenceNumber
            };

            foreach (var kvp in this.Data)
            {
                clone.Data[kvp.Key] = kvp.Value;
            }

            return clone;
        }

        public override string ToString()
        {
            return $"Event[Type={Type}, Tick={Tick}, Seq={SequenceNumber}]";
        }
    }
}