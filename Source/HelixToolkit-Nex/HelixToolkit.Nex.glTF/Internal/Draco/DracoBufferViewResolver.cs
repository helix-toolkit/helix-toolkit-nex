using glTFLoader.Schema;

namespace HelixToolkit.Nex.glTF.Internal.Draco;

/// <summary>
/// Resolves a Draco extension's <c>bufferView</c> index to a concrete byte slice
/// (<see cref="byte"/> array, offset, length) using the already-loaded buffer data.
/// </summary>
/// <remarks>
/// Unlike <see cref="AccessorReader"/>, the Draco-compressed bitstream is not described by an
/// accessor, so resolution stops one level shallower at the <c>bufferView → buffer</c> chain.
/// </remarks>
internal static class DracoBufferViewResolver
{
    /// <summary>
    /// Resolves a <c>bufferView</c> index to the underlying buffer slice that contains the
    /// Draco-compressed bitstream.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="bufferData">The raw binary buffer data arrays (already loaded).</param>
    /// <param name="bufferViewIndex">The <c>bufferView</c> index declared by the Draco extension.</param>
    /// <param name="buffer">
    /// When resolution succeeds, the buffer byte array that backs the slice; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="offset">When resolution succeeds, the byte offset of the slice within <paramref name="buffer"/>; otherwise 0.</param>
    /// <param name="length">When resolution succeeds, the byte length of the slice; otherwise 0.</param>
    /// <returns>
    /// <see langword="true"/> when the slice resolves within bounds; <see langword="false"/> when the
    /// <c>bufferView</c> index or buffer index is out of range, the referenced buffer data is not
    /// loaded, or the slice range falls outside the buffer bounds.
    /// </returns>
    public static bool TryResolve(
        Gltf model,
        byte[][] bufferData,
        int bufferViewIndex,
        out byte[]? buffer,
        out int offset,
        out int length
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bufferData);

        buffer = null;
        offset = 0;
        length = 0;

        // BufferView index must be in range.
        if (
            model.BufferViews == null
            || bufferViewIndex < 0
            || bufferViewIndex >= model.BufferViews.Length
        )
        {
            return false;
        }

        var bufferView = model.BufferViews[bufferViewIndex];

        // Buffer index must be in range of the loaded buffer data.
        if (bufferView.Buffer < 0 || bufferView.Buffer >= bufferData.Length)
        {
            return false;
        }

        var bufferBytes = bufferData[bufferView.Buffer];

        // Buffer data must actually be loaded.
        if (bufferBytes == null)
        {
            return false;
        }

        int byteOffset = bufferView.ByteOffset;
        int byteLength = bufferView.ByteLength;

        // Reject negative or overflowing ranges, and ranges past the end of the buffer.
        if (byteOffset < 0 || byteLength < 0)
        {
            return false;
        }

        if ((long)byteOffset + byteLength > bufferBytes.Length)
        {
            return false;
        }

        buffer = bufferBytes;
        offset = byteOffset;
        length = byteLength;
        return true;
    }
}
