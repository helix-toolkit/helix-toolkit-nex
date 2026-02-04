using System.Text.Json;

namespace HelixToolkit.Nex.Geometries.Serialization;

/// <summary>
/// JSON converter for the Vector4 struct.
/// </summary>
public class Vector4JsonConverter : JsonConverter<Vector4>
{
    public override Vector4 Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject for Vector4");
        }

        float x = 0,
            y = 0,
            z = 0,
            w = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new Vector4(x, y, z, w);
            }

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string? propName = reader.GetString();
                reader.Read();

                switch (propName)
                {
                    case "X":
                        x = reader.GetSingle();
                        break;
                    case "Y":
                        y = reader.GetSingle();
                        break;
                    case "Z":
                        z = reader.GetSingle();
                        break;
                    case "W":
                        w = reader.GetSingle();
                        break;
                }
            }
        }
        throw new JsonException("Unexpected end while reading Vector4");
    }

    public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("X", value.X);
        writer.WriteNumber("Y", value.Y);
        writer.WriteNumber("Z", value.Z);
        writer.WriteNumber("W", value.W);
        writer.WriteEndObject();
    }
}
