using System.Runtime.InteropServices;
using HelixToolkit.Nex.Graphics;
using SysBuffer = System.Buffer;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// An unmanaged 2D slice of pixel data. Does not own the underlying memory.
/// </summary>
public sealed class PixelBuffer
{
    private Format _format;
    private readonly bool _isStrictRowStride;

    public PixelBuffer(
        int width,
        int height,
        Format format,
        int rowStride,
        int bufferStride,
        IntPtr dataPointer
    )
    {
        if (dataPointer == IntPtr.Zero)
            throw new ArgumentException(
                "Pointer cannot be equal to IntPtr.Zero",
                nameof(dataPointer)
            );

        Width = width;
        Height = height;
        _format = format;
        RowStride = rowStride;
        BufferStride = bufferStride;
        DataPointer = dataPointer;
        PixelSize = PitchCalculator.GetBitsPerPixel(format) / 8;
        _isStrictRowStride = (PixelSize * width) == rowStride;
    }

    public int Width { get; }
    public int Height { get; }
    public int PixelSize { get; }
    public int RowStride { get; }
    public int BufferStride { get; }
    public IntPtr DataPointer { get; }

    public Format Format
    {
        get => _format;
        set
        {
            int newPixelSize = PitchCalculator.GetBitsPerPixel(value) / 8;
            if (newPixelSize != PixelSize)
                throw new ArgumentException(
                    $"Format [{value}] doesn't have same pixel size in bytes as current format [{_format}]"
                );
            _format = value;
        }
    }

    public unsafe T GetPixel<T>(int x, int y)
        where T : unmanaged
    {
        return *(T*)((byte*)DataPointer + (RowStride * y) + (x * PixelSize));
    }

    public unsafe void SetPixel<T>(int x, int y, T value)
        where T : unmanaged
    {
        *(T*)((byte*)DataPointer + (RowStride * y) + (x * PixelSize)) = value;
    }

    public T[] GetPixels<T>(int yOffset = 0)
        where T : unmanaged
    {
        var sizeOfT = Marshal.SizeOf<T>();
        var totalSize = Width * Height * PixelSize;
        if ((totalSize % sizeOfT) != 0)
            throw new ArgumentException(
                $"Invalid sizeof(T), not a multiple of current size [{totalSize}] in bytes"
            );
        var buffer = new T[totalSize / sizeOfT];
        GetPixels(buffer, yOffset, 0, buffer.Length);
        return buffer;
    }

    public void GetPixels<T>(T[] pixels, int yOffset = 0)
        where T : unmanaged
    {
        GetPixels(pixels, yOffset, 0, pixels.Length);
    }

    public unsafe void GetPixels<T>(T[] pixels, int yOffset, int pixelIndex, int pixelCount)
        where T : unmanaged
    {
        var sizeOfT = Marshal.SizeOf<T>();
        var pixelPointer = (byte*)DataPointer + yOffset * RowStride;
        if (_isStrictRowStride)
        {
            fixed (T* dst = &pixels[pixelIndex])
            {
                SysBuffer.MemoryCopy(
                    pixelPointer,
                    dst,
                    (long)pixelCount * sizeOfT,
                    (long)pixelCount * sizeOfT
                );
            }
        }
        else
        {
            var sizeOfOutputPixel = sizeOfT * pixelCount;
            var sizePerWidth = sizeOfOutputPixel / Width;
            var remainingPixels = sizeOfOutputPixel % Width;
            for (var i = 0; i < sizePerWidth; i++)
            {
                fixed (T* dst = &pixels[pixelIndex])
                    SysBuffer.MemoryCopy(
                        pixelPointer,
                        dst,
                        (long)Width * sizeOfT,
                        (long)Width * sizeOfT
                    );
                pixelPointer += RowStride;
                pixelIndex += Width;
            }
            if (remainingPixels > 0)
            {
                fixed (T* dst = &pixels[pixelIndex])
                    SysBuffer.MemoryCopy(
                        pixelPointer,
                        dst,
                        (long)remainingPixels * sizeOfT,
                        (long)remainingPixels * sizeOfT
                    );
            }
        }
    }

    public void SetPixels<T>(T[] sourcePixels, int yOffset = 0)
        where T : unmanaged
    {
        SetPixels(sourcePixels, yOffset, 0, sourcePixels.Length);
    }

    public unsafe void SetPixels<T>(T[] sourcePixels, int yOffset, int pixelIndex, int pixelCount)
        where T : unmanaged
    {
        var sizeOfT = Marshal.SizeOf<T>();
        var pixelPointer = (byte*)DataPointer + yOffset * RowStride;
        if (_isStrictRowStride)
        {
            fixed (T* src = &sourcePixels[pixelIndex])
            {
                SysBuffer.MemoryCopy(
                    src,
                    pixelPointer,
                    (long)pixelCount * sizeOfT,
                    (long)pixelCount * sizeOfT
                );
            }
        }
        else
        {
            var sizeOfOutputPixel = sizeOfT * pixelCount;
            var sizePerWidth = sizeOfOutputPixel / Width;
            var remainingPixels = sizeOfOutputPixel % Width;
            for (var i = 0; i < sizePerWidth; i++)
            {
                fixed (T* src = &sourcePixels[pixelIndex])
                    SysBuffer.MemoryCopy(
                        src,
                        pixelPointer,
                        (long)Width * sizeOfT,
                        (long)Width * sizeOfT
                    );
                pixelPointer += RowStride;
                pixelIndex += Width;
            }
            if (remainingPixels > 0)
            {
                fixed (T* src = &sourcePixels[pixelIndex])
                    SysBuffer.MemoryCopy(
                        src,
                        pixelPointer,
                        (long)remainingPixels * sizeOfT,
                        (long)remainingPixels * sizeOfT
                    );
            }
        }
    }

    public unsafe void CopyTo(PixelBuffer destination)
    {
        if (
            Width != destination.Width
            || Height != destination.Height
            || PixelSize != destination.PixelSize
        )
            throw new ArgumentException(
                "Invalid destination PixelBuffer. Must have same Width, Height and Format pixel size.",
                nameof(destination)
            );

        if (BufferStride == destination.BufferStride)
        {
            SysBuffer.MemoryCopy(
                (void*)DataPointer,
                (void*)destination.DataPointer,
                BufferStride,
                BufferStride
            );
        }
        else
        {
            var src = (byte*)DataPointer;
            var dst = (byte*)destination.DataPointer;
            var rowBytes = Math.Min(RowStride, destination.RowStride);
            for (var i = 0; i < Height; i++)
            {
                SysBuffer.MemoryCopy(src, dst, rowBytes, rowBytes);
                src += RowStride;
                dst += destination.RowStride;
            }
        }
    }
}
