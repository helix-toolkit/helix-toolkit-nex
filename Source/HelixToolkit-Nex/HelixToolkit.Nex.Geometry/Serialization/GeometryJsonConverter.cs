using System.Text.Json;
using System.Text.Json.Serialization;

namespace HelixToolkit.Nex.Geometries.Serialization;

/// <summary>
/// JSON converter for the Geometry class.
/// </summary>
public class GeometryJsonConverter : JsonConverter<Geometry>
{
    public override Geometry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token");
        }

        Guid id = Guid.NewGuid();
        Topology topology = Topology.Triangle;
        FastList<Vertex>? vertices = null;
        FastList<uint>? indices = null;
        FastList<BiNormal>? biNormals = null;
        bool isDynamic = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                var geometry = new Geometry(
                    vertices ?? new FastList<Vertex>(),
                    indices ?? new FastList<uint>(),
                    biNormals,
                    topology
                )
                {
                    Id = id,
                    IsDynamic = isDynamic
                };
                return geometry;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected PropertyName token");
            }

            string? propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "Id":
                    id = reader.GetGuid();
                    break;
                case "Topology":
                    topology = JsonSerializer.Deserialize<Topology>(ref reader, options);
                    break;
                case "Vertices":
                    vertices = JsonSerializer.Deserialize<FastList<Vertex>>(ref reader, options);
                    break;
                case "Indices":
                    indices = JsonSerializer.Deserialize<FastList<uint>>(ref reader, options);
                    break;
                case "BiNormals":
                    biNormals = JsonSerializer.Deserialize<FastList<BiNormal>>(ref reader, options);
                    break;
                case "IsDynamic":
                    isDynamic = reader.GetBoolean();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unexpected end of JSON");
    }

    public override void Write(Utf8JsonWriter writer, Geometry value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("Id");
        writer.WriteStringValue(value.Id);

        writer.WritePropertyName("Topology");
        JsonSerializer.Serialize(writer, value.Topology, options);

        writer.WritePropertyName("Vertices");
        JsonSerializer.Serialize(writer, value.Vertices, options);

        writer.WritePropertyName("Indices");
        JsonSerializer.Serialize(writer, value.Indices, options);

        if (value.BiNormals.Count > 0)
        {
            writer.WritePropertyName("BiNormals");
            JsonSerializer.Serialize(writer, value.BiNormals, options);
        }

        writer.WritePropertyName("IsDynamic");
        writer.WriteBooleanValue(value.IsDynamic);

        writer.WriteEndObject();
    }
}
