using System.Runtime.InteropServices;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Interface for pluggable image decoders/encoders.
/// Implementations can decode common image formats (PNG, JPG, BMP, etc.) into <see cref="Image"/> objects
/// and save pixel data back to a stream.
/// </summary>
public interface IImageDecoder
{
    /// <summary>
    /// Attempts to decode image data from an unmanaged memory pointer.
    /// Returns <c>null</c> if the format is not recognized by this decoder.
    /// </summary>
    /// <param name="dataPointer">Pointer to the raw image data.</param>
    /// <param name="dataSize">Size of the data in bytes.</param>
    /// <param name="makeACopy">If true, the decoder should copy the data; otherwise it may take ownership.</param>
    /// <param name="handle">Optional GCHandle pinning a managed buffer; the decoder should free it if it takes ownership.</param>
    /// <returns>A decoded <see cref="Image"/>, or <c>null</c> if the format is not recognized.</returns>
    Image? Decode(IntPtr dataPointer, int dataSize, bool makeACopy, GCHandle? handle);

    /// <summary>
    /// Saves pixel buffer data to a stream in this decoder's format.
    /// </summary>
    /// <param name="pixelBuffers">Array of pixel buffers to save.</param>
    /// <param name="count">Number of pixel buffers to write.</param>
    /// <param name="description">The image description (dimensions, format, mip levels, etc.).</param>
    /// <param name="stream">The output stream to write to.</param>
    void Save(PixelBuffer[] pixelBuffers, int count, ImageDescription description, Stream stream);
}
