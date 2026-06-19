namespace HelixToolkit.Nex.glTF.Internal.Draco;

/// <summary>
/// Decodes a <c>KHR_draco_mesh_compression</c> bitstream into a
/// <see cref="DecodedMesh"/>. Abstracted as an interface so the conversion logic
/// can be exercised with a deterministic fake decoder in tests, while the
/// production implementation wraps the native Draco library.
/// </summary>
internal interface IDracoDecoder
{
    /// <summary>
    /// Decodes a Draco-compressed byte range into a <see cref="DecodedMesh"/>.
    /// </summary>
    /// <param name="compressed">The Draco bitstream slice (the extension's bufferView range).</param>
    /// <param name="attributeMap">
    /// A map of glTF semantic name to Draco attribute unique id, taken from the
    /// extension's <c>attributes</c> map.
    /// </param>
    /// <returns>
    /// An outcome describing success (with a <see cref="DecodedMesh"/>) or a typed
    /// failure.
    /// </returns>
    DracoDecodeOutcome Decode(
        ReadOnlySpan<byte> compressed,
        IReadOnlyDictionary<string, int> attributeMap
    );

    /// <summary>
    /// Gets a value indicating whether the native Draco library is loaded and usable.
    /// </summary>
    bool IsAvailable { get; }
}
