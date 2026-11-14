using System.Text;
using System.Text.RegularExpressions;
using glTFLoader;
using glTFLoader.Schema;

namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// Parses glTF/GLB files with <see cref="Interface"/>, tolerating <c>KHR_animation_pointer</c>.
/// The schema's animation path enum has no <c>pointer</c> value, so such files fail to deserialize.
/// </summary>
internal static partial class GltfModelLoader
{
    [GeneratedRegex("\"path\"\\s*:\\s*\"pointer\"")]
    private static partial Regex PointerPathRegex();

    /// <summary>
    /// Loads the model. When the standard parse fails, retries with <c>pointer</c> animation channel
    /// paths rewritten to <c>translation</c>; the channels keep their <c>KHR_animation_pointer</c>
    /// extension, carry no target node, and are skipped by the animation converter.
    /// </summary>
    public static Gltf Load(string filePath)
    {
        try
        {
            return Interface.LoadModel(filePath);
        }
        catch (Exception)
        {
            var json = ReadJson(filePath);
            var patched = PointerPathRegex().Replace(json, "\"path\":\"translation\"");
            if (patched == json)
            {
                throw;
            }
            return Interface.DeserializeModel(patched);
        }
    }

    private static string ReadJson(string filePath)
    {
        if (!Path.GetExtension(filePath).Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            return File.ReadAllText(filePath, Encoding.UTF8);
        }
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);
        stream.Seek(12, SeekOrigin.Begin); // magic, version, length
        var jsonLength = reader.ReadUInt32();
        reader.ReadUInt32(); // chunk type
        return Encoding.UTF8.GetString(reader.ReadBytes((int)jsonLength));
    }
}
