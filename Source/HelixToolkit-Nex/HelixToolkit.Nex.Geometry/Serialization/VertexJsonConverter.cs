using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HelixToolkit.Nex.Geometries.Serialization;

/// <summary>
/// JSON converter for the Vertex struct.
/// </summary>
public class VertexJsonConverter : JsonConverter<Vertex>
{
    public override Vertex Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token");
        }

        Vector3 position = default;
        Vector3 normal = default;
        Vector2 texCoord = default;
        Vector4 color = new Vector4(1, 1, 1, 1);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new Vertex(position, normal, texCoord, color);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected PropertyName token");
            }

            string? propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "Position":
                    position = ReadVector3(ref reader);
                    break;
                case "Normal":
                    normal = ReadVector3(ref reader);
                    break;
                case "TexCoord":
                    texCoord = ReadVector2(ref reader);
                    break;
                case "Color":
                    color = ReadVector4(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unexpected end of JSON");
    }

    private static Vector3 ReadVector3(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject for Vector3");
        }

        float x = 0, y = 0, z = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new Vector3(x, y, z);
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
                }
            }
        }
        throw new JsonException("Unexpected end while reading Vector3");
    }

    private static Vector2 ReadVector2(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject for Vector2");
        }

        float x = 0, y = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new Vector2(x, y);
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
                }
            }
        }
        throw new JsonException("Unexpected end while reading Vector2");
    }

    private static Vector4 ReadVector4(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject for Vector4");
        }

        float x = 0, y = 0, z = 0, w = 0;
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

    public override void Write(Utf8JsonWriter writer, Vertex value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("Position");
        writer.WriteStartObject();
        writer.WriteNumber("X", value.Position.X);
        writer.WriteNumber("Y", value.Position.Y);
        writer.WriteNumber("Z", value.Position.Z);
        writer.WriteEndObject();

        writer.WritePropertyName("Normal");
        writer.WriteStartObject();
        writer.WriteNumber("X", value.Normal.X);
        writer.WriteNumber("Y", value.Normal.Y);
        writer.WriteNumber("Z", value.Normal.Z);
        writer.WriteEndObject();

        writer.WritePropertyName("TexCoord");
        writer.WriteStartObject();
        writer.WriteNumber("X", value.TexCoord.X);
        writer.WriteNumber("Y", value.TexCoord.Y);
        writer.WriteEndObject();

        writer.WritePropertyName("Color");
        writer.WriteStartObject();
        writer.WriteNumber("X", value.Color.X);
        writer.WriteNumber("Y", value.Color.Y);
        writer.WriteNumber("Z", value.Color.Z);
        writer.WriteNumber("W", value.Color.W);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }
}
