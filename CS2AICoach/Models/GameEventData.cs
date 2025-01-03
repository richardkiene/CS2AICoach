namespace CS2AICoach.Models
{
    public class GameEventData
    {
        private Dictionary<string, object> _data = new();
        private string _prefix;

        public GameEventData(string prefix = "")
        {
            _prefix = prefix;
        }

        public void Add(string key, object value)
        {
            // Ensure uniqueness by prefixing all keys
            var uniqueKey = $"{_prefix}{key}";
            _data[uniqueKey] = value;
        }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>(_data);
        }
    }
}