using System.Text.Json;

namespace HelixToolkit.Nex.Geometries.Serialization;

/// <summary>
/// JSON converter for the Geometry class.
/// </summary>
public class GeometryJsonConverter : JsonConverter<Geometry>
{
    private static readonly JsonSerializerOptions InternalOptions = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new FastListJsonConverterFactory());
        options.Converters.Add(new Vector4JsonConverter());
        return options;
    }

    private static JsonSerializerOptions GetEffectiveOptions(JsonSerializerOptions options)
    {
        // Check if the required converters are already registered
        bool hasFastListConverter = false;
        bool hasVector4Converter = false;

        foreach (var converter in options.Converters)
        {
            if (converter is FastListJsonConverterFactory)
                hasFastListConverter = true;
            if (converter is Vector4JsonConverter)
                hasVector4Converter = true;
        }

        // If both converters are present, use the provided options
        if (hasFastListConverter && hasVector4Converter)
            return options;

        // Otherwise, use our internal options with the required converters
        return InternalOptions;
    }

    public override Geometry Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token");
        }

        var effectiveOptions = GetEffectiveOptions(options);

        Guid id = Guid.NewGuid();
        Topology topology = Topology.Triangle;
        FastList<Vector4>? vertices = null;
        FastList<VertexProperties>? vertProps = null;
        FastList<uint>? indices = null;
        FastList<Vector4>? colors = null;
        bool isDynamic = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                var geometry = new Geometry(
                    vertices ?? new FastList<Vector4>(),
                    vertProps ?? new FastList<VertexProperties>(),
                    indices ?? new FastList<uint>(),
                    colors,
                    topology,
                    isDynamic
                )
                {
                    Id = id,
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
                case nameof(Geometry.Id):
                    id = reader.GetGuid();
                    break;
                case nameof(Geometry.Topology):
                    topology = JsonSerializer.Deserialize<Topology>(ref reader, effectiveOptions);
                    break;
                case nameof(Geometry.Vertices):
                    vertices = JsonSerializer.Deserialize<FastList<Vector4>>(
                        ref reader,
                        effectiveOptions
                    );
                    break;
                case nameof(Geometry.VertexProps):
                    vertProps = JsonSerializer.Deserialize<FastList<VertexProperties>>(
                        ref reader,
                        effectiveOptions
                    );
                    break;
                case nameof(Geometry.Indices):
                    indices = JsonSerializer.Deserialize<FastList<uint>>(
                        ref reader,
                        effectiveOptions
                    );
                    break;
                case nameof(Geometry.VertexColors):
                    colors = JsonSerializer.Deserialize<FastList<Vector4>>(
                        ref reader,
                        effectiveOptions
                    );
                    break;
                case nameof(Geometry.IsDynamic):
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
        var effectiveOptions = GetEffectiveOptions(options);

        writer.WriteStartObject();

        writer.WritePropertyName(nameof(Geometry.Id));
        writer.WriteStringValue(value.Id);

        writer.WritePropertyName(nameof(Geometry.Topology));
        JsonSerializer.Serialize(writer, value.Topology, effectiveOptions);

        writer.WritePropertyName(nameof(Geometry.Vertices));
        JsonSerializer.Serialize(writer, value.Vertices, effectiveOptions);

        if (value.VertexProps.Count > 0)
        {
            writer.WritePropertyName(nameof(Geometry.VertexProps));
            JsonSerializer.Serialize(writer, value.VertexProps, effectiveOptions);
        }

        writer.WritePropertyName(nameof(Geometry.Indices));
        JsonSerializer.Serialize(writer, value.Indices, effectiveOptions);

        if (value.VertexColors.Count > 0)
        {
            writer.WritePropertyName("VertexColors");
            JsonSerializer.Serialize(writer, value.VertexColors, effectiveOptions);
        }

        writer.WritePropertyName("IsDynamic");
        writer.WriteBooleanValue(value.IsDynamic);

        writer.WriteEndObject();
    }
}
