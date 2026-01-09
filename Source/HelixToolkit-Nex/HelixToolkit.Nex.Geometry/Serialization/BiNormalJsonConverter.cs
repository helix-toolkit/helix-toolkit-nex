using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HelixToolkit.Nex.Geometries.Serialization;

/// <summary>
/// JSON converter for the BiNormal struct.
/// </summary>
public class BiNormalJsonConverter : JsonConverter<BiNormal>
{
    public override BiNormal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token");
        }

        Vector3 bitangent = default;
        Vector3 tangent = default;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new BiNormal(bitangent, tangent);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected PropertyName token");
            }

            string? propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "Bitangent":
                    bitangent = ReadVector3(ref reader);
                    break;
                case "Tangent":
                    tangent = ReadVector3(ref reader);
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

    public override void Write(Utf8JsonWriter writer, BiNormal value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("Bitangent");
        writer.WriteStartObject();
        writer.WriteNumber("X", value.Bitangent.X);
        writer.WriteNumber("Y", value.Bitangent.Y);
        writer.WriteNumber("Z", value.Bitangent.Z);
        writer.WriteEndObject();

        writer.WritePropertyName("Tangent");
        writer.WriteStartObject();
        writer.WriteNumber("X", value.Tangent.X);
        writer.WriteNumber("Y", value.Tangent.Y);
        writer.WriteNumber("Z", value.Tangent.Z);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }
}
