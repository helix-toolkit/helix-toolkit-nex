using System.Text.Json;
using System.Text.Json.Serialization;

namespace HelixToolkit.Nex;

/// <summary>
/// JSON converter factory for FastList&lt;T&gt;.
/// </summary>
public class FastListJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }

        var genericType = typeToConvert.GetGenericTypeDefinition();
        return genericType == typeof(FastList<>);
    }

    public override JsonConverter? CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var elementType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(FastListJsonConverter<>).MakeGenericType(elementType);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}

/// <summary>
/// JSON converter for FastList&lt;T&gt;.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public class FastListJsonConverter<T> : JsonConverter<FastList<T>>
{
    public override FastList<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected StartArray token");
        }

        var list = new FastList<T>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return list;
            }

            var item = JsonSerializer.Deserialize<T>(ref reader, options);
            if (item != null)
            {
                list.Add(item);
            }
        }

        throw new JsonException("Unexpected end of JSON");
    }

    public override void Write(
        Utf8JsonWriter writer,
        FastList<T> value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartArray();

        for (int i = 0; i < value.Count; i++)
        {
            JsonSerializer.Serialize(writer, value[i], options);
        }

        writer.WriteEndArray();
    }
}
