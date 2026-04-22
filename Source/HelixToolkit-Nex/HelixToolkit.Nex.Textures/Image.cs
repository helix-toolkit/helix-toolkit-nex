using System.Runtime.InteropServices;
using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// CPU-side container for pixel data. Manages an unmanaged memory buffer holding all pixel data
/// contiguously across array slices, mip levels, and z-slices.
/// </summary>
public sealed class Image : IDisposable
{
    // ---- Delegate types for the pluggable loader/saver registry ----

    public delegate Image? ImageLoadDelegate(
        IntPtr dataPointer,
        int dataSize,
        bool makeACopy,
        GCHandle? handle
    );
    public delegate void ImageSaveDelegate(
        PixelBuffer[] pixelBuffers,
        int count,
        ImageDescription description,
        Stream imageStream
    );

    // ---- Internal pixel buffer array ----

    /// <summary>
    /// All pixel buffers, indexed by (arraySlice * zBufferCountPerArraySlice + mipZOffset + zSlice).
    /// </summary>
    internal PixelBuffer[] PixelBuffers = [];

    /// <summary>
    /// Maps mip level index → starting z-buffer index within one array slice.
    /// Has MipLevels+1 entries; the last entry equals zBufferCountPerArraySlice.
    /// </summary>
    private List<int> _mipMapToZIndex = [];

    /// <summary>
    /// Total number of z-buffers (across all mip levels) per array slice.
    /// </summary>
    private int _zBufferCountPerArraySlice;

    private MipMapDescription[] _mipmapDescriptions = [];

    // ---- Static constructor: register built-in codecs ----

    static Image()
    {
        Register(ImageFileType.Dds, DDSCodec.LoadFromMemory, DDSCodec.SaveToStream);

        // Register ImageSharp for all raster formats.
        // Each entry shares the same decoder but uses a format-specific save delegate.
        var dec = ImageSharpDecoder.Instance;
        Register(
            ImageFileType.Png,
            dec.Decode,
            (pb, c, d, s) => dec.SaveAs(pb, c, d, s, ImageFileType.Png)
        );
        Register(
            ImageFileType.Jpg,
            dec.Decode,
            (pb, c, d, s) => dec.SaveAs(pb, c, d, s, ImageFileType.Jpg)
        );
        Register(
            ImageFileType.Bmp,
            dec.Decode,
            (pb, c, d, s) => dec.SaveAs(pb, c, d, s, ImageFileType.Bmp)
        );
        Register(
            ImageFileType.Gif,
            dec.Decode,
            (pb, c, d, s) => dec.SaveAs(pb, c, d, s, ImageFileType.Gif)
        );
        Register(
            ImageFileType.Tiff,
            dec.Decode,
            (pb, c, d, s) => dec.SaveAs(pb, c, d, s, ImageFileType.Tiff)
        );
        Register(
            ImageFileType.Webp,
            dec.Decode,
            (pb, c, d, s) => dec.SaveAs(pb, c, d, s, ImageFileType.Webp)
        );
        Register(
            ImageFileType.Tga,
            dec.Decode,
            (pb, c, d, s) => dec.SaveAs(pb, c, d, s, ImageFileType.Tga)
        );
    }

    // ---- Static loader/saver registry ----

    private static readonly ImageLoaderRegistry _registry = new();

    // ---- Buffer ownership fields ----

    private int _totalSizeInBytes;
    private IntPtr _buffer;
    private bool _bufferIsDisposable;
    private GCHandle? _handle;
    private bool _disposed;

    // ---- Public properties ----

    public ImageDescription Description { get; private set; }
    public int TotalSizeInBytes => _totalSizeInBytes;
    public IntPtr DataPointer => _buffer;

    // ---- Constructors ----

    private Image() { }

    internal Image(
        ImageDescription description,
        IntPtr dataPointer,
        int offset,
        GCHandle? handle,
        bool bufferIsDisposable,
        PitchFlags pitchFlags = PitchFlags.None
    )
    {
        Initialize(description, dataPointer, offset, handle, bufferIsDisposable, pitchFlags);
    }

    // ---- IDisposable ----

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_handle.HasValue)
            _handle.Value.Free();

        if (_bufferIsDisposable && _buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    // ---- Pixel buffer access ----

    /// <summary>
    /// Gets the mipmap description for the specified mip level.
    /// </summary>
    public MipMapDescription GetMipMapDescription(int mipmap) => _mipmapDescriptions[mipmap];

    /// <summary>
    /// Gets the pixel buffer for the specified array/z-slice index and mip level.
    /// For Texture3D, <paramref name="arrayOrZSliceIndex"/> is the z-slice index.
    /// For other dimensions, it is the array slice index.
    /// </summary>
    public PixelBuffer GetPixelBuffer(int arrayOrZSliceIndex, int mipmap)
    {
        if (mipmap >= Description.MipLevels)
            throw new ArgumentException("Invalid mipmap level", nameof(mipmap));

        if (Description.Dimension == TextureDimension.Texture3D)
        {
            if (arrayOrZSliceIndex >= Description.Depth)
                throw new ArgumentException("Invalid z slice index", nameof(arrayOrZSliceIndex));
            return GetPixelBufferUnsafe(0, arrayOrZSliceIndex, mipmap);
        }

        if (arrayOrZSliceIndex >= Description.ArraySize)
            throw new ArgumentException("Invalid array slice index", nameof(arrayOrZSliceIndex));
        return GetPixelBufferUnsafe(arrayOrZSliceIndex, 0, mipmap);
    }

    /// <summary>
    /// Gets the pixel buffer for the specified array index, z-slice index, and mip level.
    /// </summary>
    public PixelBuffer GetPixelBuffer(int arrayIndex, int zIndex, int mipmap)
    {
        if (mipmap >= Description.MipLevels)
            throw new ArgumentException("Invalid mipmap level", nameof(mipmap));
        if (arrayIndex >= Description.ArraySize)
            throw new ArgumentException("Invalid array slice index", nameof(arrayIndex));
        if (zIndex >= Description.Depth)
            throw new ArgumentException("Invalid z slice index", nameof(zIndex));
        return GetPixelBufferUnsafe(arrayIndex, zIndex, mipmap);
    }

    private PixelBuffer GetPixelBufferUnsafe(int arrayIndex, int zIndex, int mipmap)
    {
        var depthIndex = _mipMapToZIndex[mipmap];
        var pixelBufferIndex = arrayIndex * _zBufferCountPerArraySlice + depthIndex + zIndex;
        return PixelBuffers[pixelBufferIndex];
    }

    // ---- Factory methods ----

    /// <summary>Creates a new Image from an <see cref="ImageDescription"/>, allocating a new buffer.</summary>
    public static Image New(ImageDescription description) => New(description, IntPtr.Zero);

    /// <summary>Creates a new Image from an <see cref="ImageDescription"/> using an existing data pointer.</summary>
    public static Image New(ImageDescription description, IntPtr dataPointer) =>
        new Image(description, dataPointer, 0, null, false);

    /// <summary>Creates a new 2D image.</summary>
    public static Image New2D(
        int width,
        int height,
        MipMapCount mipMapCount,
        Format format,
        int arraySize = 1
    ) => New2D(width, height, mipMapCount, format, arraySize, IntPtr.Zero);

    /// <summary>Creates a new 2D image using an existing data pointer.</summary>
    public static Image New2D(
        int width,
        int height,
        MipMapCount mipMapCount,
        Format format,
        int arraySize,
        IntPtr dataPointer
    ) =>
        new(
            CreateDescription(
                TextureDimension.Texture2D,
                width,
                height,
                1,
                mipMapCount,
                format,
                arraySize
            ),
            dataPointer,
            0,
            null,
            false
        );

    /// <summary>Creates a new cube image (arraySize = 6).</summary>
    public static Image NewCube(int width, MipMapCount mipMapCount, Format format) =>
        NewCube(width, mipMapCount, format, IntPtr.Zero);

    /// <summary>Creates a new cube image using an existing data pointer.</summary>
    public static Image NewCube(
        int width,
        MipMapCount mipMapCount,
        Format format,
        IntPtr dataPointer
    ) =>
        new(
            CreateDescription(
                TextureDimension.TextureCube,
                width,
                width,
                1,
                mipMapCount,
                format,
                6
            ),
            dataPointer,
            0,
            null,
            false
        );

    /// <summary>Creates a new 3D image.</summary>
    public static Image New3D(
        int width,
        int height,
        int depth,
        MipMapCount mipMapCount,
        Format format
    ) => New3D(width, height, depth, mipMapCount, format, IntPtr.Zero);

    /// <summary>Creates a new 3D image using an existing data pointer.</summary>
    public static Image New3D(
        int width,
        int height,
        int depth,
        MipMapCount mipMapCount,
        Format format,
        IntPtr dataPointer
    ) =>
        new(
            CreateDescription(
                TextureDimension.Texture3D,
                width,
                height,
                depth,
                mipMapCount,
                format,
                1
            ),
            dataPointer,
            0,
            null,
            false
        );

    private static ImageDescription CreateDescription(
        TextureDimension dimension,
        int width,
        int height,
        int depth,
        MipMapCount mipMapCount,
        Format format,
        int arraySize
    ) =>
        new()
        {
            Dimension = dimension,
            Width = width,
            Height = height,
            Depth = depth,
            ArraySize = arraySize,
            Format = format,
            MipLevels = mipMapCount,
        };

    // ---- Loader/saver registry ----

    /// <summary>
    /// Registers a loader and/or saver for the specified image file type.
    /// At least one of <paramref name="loader"/> or <paramref name="saver"/> must be non-null.
    /// </summary>
    public static void Register(
        ImageFileType type,
        ImageLoadDelegate? loader,
        ImageSaveDelegate? saver
    )
    {
        _registry.Register(type, loader, saver);
    }

    // ---- Loading ----

    /// <summary>Loads an image from a managed byte array.</summary>
    public static Image? Load(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var size = buffer.Length;

        // Pin large arrays on the LOH instead of copying
        if (size > (85 * 1024))
        {
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            return Load(handle.AddrOfPinnedObject(), size, false, handle);
        }

        unsafe
        {
            fixed (void* pbuffer = buffer)
                return Load((IntPtr)pbuffer, size, true, null);
        }
    }

    /// <summary>Loads an image from a stream.</summary>
    public static Image? Load(Stream imageStream)
    {
        using var ms = new MemoryStream();
        imageStream.CopyTo(ms);
        return Load(ms.ToArray());
    }

    /// <summary>Loads an image from a file.</summary>
    public static Image? Load(string fileName)
    {
        var data = File.ReadAllBytes(fileName);
        return Load(data);
    }

    /// <summary>Loads an image from an unmanaged memory pointer.</summary>
    public static Image? Load(IntPtr dataPointer, int dataSize, bool makeACopy = false) =>
        Load(dataPointer, dataSize, makeACopy, null);

    internal static Image? Load(IntPtr dataPointer, int dataSize, bool makeACopy, GCHandle? handle)
    {
        return _registry.Load(dataPointer, dataSize, makeACopy, handle);
    }

    // ---- Saving ----

    /// <summary>Saves this image to a stream in the specified format.</summary>
    public void Save(Stream imageStream, ImageFileType fileType)
    {
        _registry.Save(fileType, PixelBuffers, PixelBuffers.Length, Description, imageStream);
    }

    /// <summary>Saves this image to a file in the specified format.</summary>
    public void Save(string fileName, ImageFileType fileType)
    {
        using var stream = File.OpenWrite(fileName);
        Save(stream, fileType);
    }

    // ---- Internal initialization ----

    internal unsafe void Initialize(
        ImageDescription description,
        IntPtr dataPointer,
        int offset,
        GCHandle? handle,
        bool bufferIsDisposable,
        PitchFlags pitchFlags = PitchFlags.None
    )
    {
        _handle = handle;

        // Validate and resolve mip levels per dimension
        switch (description.Dimension)
        {
            case TextureDimension.Texture2D:
            case TextureDimension.TextureCube:
                if (
                    description.Width <= 0
                    || description.Height <= 0
                    || description.Depth != 1
                    || description.ArraySize == 0
                )
                    throw new InvalidOperationException(
                        "Invalid Width/Height/Depth/ArraySize for Image 2D"
                    );
                if (
                    description.Dimension == TextureDimension.TextureCube
                    && (description.ArraySize % 6) != 0
                )
                    throw new InvalidOperationException(
                        "TextureCube must have an arraysize that is a multiple of 6"
                    );
                description.MipLevels = PitchCalculator.CalculateMipLevels(
                    description.Width,
                    description.Height,
                    description.MipLevels
                );
                break;

            case TextureDimension.Texture3D:
                if (
                    description.Width <= 0
                    || description.Height <= 0
                    || description.Depth <= 0
                    || description.ArraySize != 1
                )
                    throw new InvalidOperationException(
                        "Invalid Width/Height/Depth/ArraySize for Image 3D"
                    );
                description.MipLevels = PitchCalculator.CalculateMipLevels(
                    description.Width,
                    description.Height,
                    description.Depth,
                    description.MipLevels
                );
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported texture dimension: {description.Dimension}"
                );
        }

        // Calculate image array layout
        _mipMapToZIndex = CalculateImageArray(
            description,
            pitchFlags,
            out var pixelBufferCount,
            out _totalSizeInBytes
        );
        _mipmapDescriptions = CalculateMipMapDescriptions(description, pitchFlags);
        _zBufferCountPerArraySlice = _mipMapToZIndex[^1];

        // Allocate pixel buffer array
        PixelBuffers = new PixelBuffer[pixelBufferCount];

        // Handle buffer ownership
        _bufferIsDisposable = !handle.HasValue && bufferIsDisposable;
        _buffer = dataPointer;

        if (dataPointer == IntPtr.Zero)
        {
            _buffer = Marshal.AllocHGlobal(_totalSizeInBytes);
            offset = 0;
            _bufferIsDisposable = true;
        }

        SetupImageArray(
            (IntPtr)((byte*)_buffer + offset),
            _totalSizeInBytes,
            description,
            pitchFlags,
            PixelBuffers
        );
        Description = description;
    }

    /// <summary>
    /// Calculates the total pixel buffer count and total size in bytes for the image layout.
    /// Returns a list mapping mip level → starting z-buffer index within one array slice.
    /// </summary>
    private static List<int> CalculateImageArray(
        ImageDescription desc,
        PitchFlags pitchFlags,
        out int bufferCount,
        out int pixelSizeInBytes
    )
    {
        pixelSizeInBytes = 0;
        bufferCount = 0;
        var mipMapToZIndex = new List<int>();

        for (var j = 0; j < desc.ArraySize; j++)
        {
            int w = desc.Width,
                h = desc.Height,
                d = desc.Depth;
            for (var i = 0; i < desc.MipLevels; i++)
            {
                PitchCalculator.ComputePitch(
                    desc.Format,
                    w,
                    h,
                    out _,
                    out var slicePitch,
                    out _,
                    out _,
                    pitchFlags
                );
                if (j == 0)
                    mipMapToZIndex.Add(bufferCount);
                pixelSizeInBytes += d * slicePitch;
                bufferCount += d;
                if (h > 1)
                    h >>= 1;
                if (w > 1)
                    w >>= 1;
                if (d > 1)
                    d >>= 1;
            }
            if (j == 0)
                mipMapToZIndex.Add(bufferCount); // sentinel: total z-buffers per slice
        }
        return mipMapToZIndex;
    }

    private static MipMapDescription[] CalculateMipMapDescriptions(
        ImageDescription desc,
        PitchFlags pitchFlags
    )
    {
        int w = desc.Width,
            h = desc.Height,
            d = desc.Depth;
        var mipmaps = new MipMapDescription[desc.MipLevels];
        for (var level = 0; level < desc.MipLevels; level++)
        {
            PitchCalculator.ComputePitch(
                desc.Format,
                w,
                h,
                out var rowPitch,
                out var slicePitch,
                out var wPacked,
                out var hPacked,
                pitchFlags
            );
            mipmaps[level] = new MipMapDescription(w, h, d, rowPitch, slicePitch, wPacked, hPacked);
            if (h > 1)
                h >>= 1;
            if (w > 1)
                w >>= 1;
            if (d > 1)
                d >>= 1;
        }
        return mipmaps;
    }

    private static unsafe void SetupImageArray(
        IntPtr buffer,
        int pixelSize,
        ImageDescription desc,
        PitchFlags pitchFlags,
        PixelBuffer[] output
    )
    {
        var index = 0;
        var pixels = (byte*)buffer;
        for (var item = 0; item < desc.ArraySize; item++)
        {
            int w = desc.Width,
                h = desc.Height,
                d = desc.Depth;
            for (var level = 0; level < desc.MipLevels; level++)
            {
                PitchCalculator.ComputePitch(
                    desc.Format,
                    w,
                    h,
                    out var rowPitch,
                    out var slicePitch,
                    out _,
                    out _,
                    pitchFlags
                );
                for (var zSlice = 0; zSlice < d; zSlice++)
                {
                    output[index] = new PixelBuffer(
                        w,
                        h,
                        desc.Format,
                        rowPitch,
                        slicePitch,
                        (IntPtr)pixels
                    );
                    index++;
                    pixels += slicePitch;
                }
                if (h > 1)
                    h >>= 1;
                if (w > 1)
                    w >>= 1;
                if (d > 1)
                    d >>= 1;
            }
        }
    }
}
