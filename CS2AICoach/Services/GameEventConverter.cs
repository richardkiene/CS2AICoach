using System.Text.Json;
using System.Text.Json.Serialization;
using CS2AICoach.Models;

namespace CS2AICoach.Services
{
    public class GameEventConverter : JsonConverter<GameEvent>
    {
        public override GameEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, GameEvent value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id.ToString());
            writer.WriteString("type", value.Type);
            writer.WriteNumber("tick", value.Tick);

            writer.WriteStartObject("data");
            foreach (var kvp in value.Data)
            {
                writer.WritePropertyName(kvp.Key);
                JsonSerializer.Serialize(writer, kvp.Value, options);
            }
            writer.WriteEndObject();

            writer.WriteEndObject();
        }
    }
}