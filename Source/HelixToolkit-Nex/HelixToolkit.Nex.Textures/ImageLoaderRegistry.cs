using System.Runtime.InteropServices;

namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Internal registry of image loader and saver delegates, keyed by <see cref="ImageFileType"/>.
/// Loaders are iterated in registration order; the first non-null result is returned.
/// </summary>
internal sealed class ImageLoaderRegistry
{
    private readonly List<LoadSaveEntry> _entries = [];

    /// <summary>
    /// Registers a loader and/or saver for the specified image file type.
    /// At least one of <paramref name="loader"/> or <paramref name="saver"/> must be non-null.
    /// If a registration already exists for the file type, it is replaced.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when both <paramref name="loader"/> and <paramref name="saver"/> are null.</exception>
    public void Register(
        ImageFileType fileType,
        Image.ImageLoadDelegate? loader,
        Image.ImageSaveDelegate? saver
    )
    {
        if (loader == null && saver == null)
            throw new ArgumentNullException(
                "loader/saver",
                "Cannot set both loader and saver to null"
            );

        var entry = new LoadSaveEntry(fileType, loader, saver);
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].FileType == fileType)
            {
                _entries[i] = entry;
                return;
            }
        }
        _entries.Add(entry);
    }

    /// <summary>
    /// Attempts to load an image from the given data pointer by iterating registered loaders in order.
    /// Returns the first non-null result, or <c>null</c> if no loader succeeds.
    /// </summary>
    public Image? Load(IntPtr dataPointer, int dataSize, bool makeACopy, GCHandle? handle)
    {
        foreach (var entry in _entries)
        {
            if (entry.Load != null)
            {
                var image = entry.Load(dataPointer, dataSize, makeACopy, handle);
                if (image != null)
                    return image;
            }
        }
        return null;
    }

    /// <summary>
    /// Saves pixel data using the registered saver for the specified file type.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown when no saver is registered for the file type.</exception>
    public void Save(
        ImageFileType fileType,
        PixelBuffer[] pixelBuffers,
        int count,
        ImageDescription description,
        Stream stream
    )
    {
        foreach (var entry in _entries)
        {
            if (entry.FileType == fileType && entry.Save != null)
            {
                entry.Save(pixelBuffers, count, description, stream);
                return;
            }
        }
        throw new NotSupportedException($"No saver registered for file type {fileType}");
    }

    private sealed class LoadSaveEntry(
        ImageFileType fileType,
        Image.ImageLoadDelegate? load,
        Image.ImageSaveDelegate? save
        )
    {
        public readonly ImageFileType FileType = fileType;
        public readonly Image.ImageLoadDelegate? Load = load;
        public readonly Image.ImageSaveDelegate? Save = save;
    }
}
