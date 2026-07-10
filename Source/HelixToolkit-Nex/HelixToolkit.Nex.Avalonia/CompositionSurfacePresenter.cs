using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Microsoft.Extensions.Logging;
using Format = HelixToolkit.Nex.Graphics.Format;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Avalonia <see cref="Control"/> that presents the shared engine output through a
/// <see cref="CompositionDrawingSurface"/> and <see cref="ICompositionGpuInterop"/> (the same interop
/// surface used by the AvaloniaUI <c>GpuInterop/D3DDemo</c> sample), rather than a WinUI-style
/// <c>SwapChainPanel</c> or DXGI swap chain.
/// </summary>
/// <remarks>
/// <para>
/// On attach the presenter obtains the compositor for the element, creates a
/// <see cref="CompositionDrawingSurface"/> and a <see cref="CompositionSurfaceVisual"/>, wires the
/// visual as the element's child visual, and attempts to acquire the GPU interop handshake through
/// <see cref="Compositor.TryGetCompositionGpuInterop"/>.
/// </para>
/// <para>
/// Each available frame is presented through <see cref="PresentAsync(SharedImageDescription, ISurfaceUpdateSync)"/>,
/// which imports the platform-neutral <see cref="SharedImageDescription"/> via
/// <see cref="ICompositionGpuInterop.ImportImage(IPlatformHandle, PlatformGraphicsExternalImageProperties)"/>
/// and updates the drawing surface through the supplied <see cref="ISurfaceUpdateSync"/>. The surface
/// visual is kept sized to the control. When the render session exposes no usable interop, the
/// presenter logs a warning, reports <see cref="GpuInteropAvailable"/> as <see langword="false"/>,
/// and skips presentation without throwing or terminating the application.
/// </para>
/// </remarks>
internal sealed class CompositionSurfacePresenter : Control
{
    private static readonly ILogger _logger = LogManager.Create<CompositionSurfacePresenter>();

    private CompositionDrawingSurface? _surface;
    private ICompositionGpuInterop? _interop;
    private CompositionSurfaceVisual? _visual;
    private Task? _initializeTask;

    // Imported GPU images cached by their platform handle. Importing a shared texture into the
    // compositor is expensive (it opens the shared handle and creates GPU resources), so each buffer
    // is imported once and reused. With a multi-buffered bridge the presenter sees several distinct
    // handles rotating frame to frame, so one entry is kept per buffer. The whole cache is dropped
    // when the image size/format changes (a resize), because every buffer then has a new handle.
    private readonly Dictionary<nint, ICompositionImportedGpuImage> _importedImages = new();
    private uint _importedWidth;
    private uint _importedHeight;
    private Format _importedFormat;

    /// <summary>
    /// Indicates whether the Avalonia render session exposed a usable <see cref="ICompositionGpuInterop"/>.
    /// When <see langword="false"/>, presentation is skipped rather than throwing or terminating the
    /// application.
    /// </summary>
    public bool GpuInteropAvailable => _interop is not null;

    /// <summary>
    /// Begins the composition handshake once the control is attached to the visual tree and the
    /// compositor is available.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _initializeTask = InitializeCompositionAsync();
    }

    /// <summary>
    /// Obtains the compositor for this element, creates the drawing surface and surface visual, wires
    /// the visual as the element's child visual, and attempts to acquire the GPU interop handshake.
    /// </summary>
    private async Task InitializeCompositionAsync()
    {
        Compositor? compositor = ElementComposition.GetElementVisual(this)?.Compositor;
        if (compositor is null)
        {
            return;
        }

        _surface = compositor.CreateDrawingSurface();
        _visual = compositor.CreateSurfaceVisual();
        _visual.Surface = _surface;

        // The engine renders the shared image with the opposite vertical origin to what the Avalonia
        // compositor samples when it draws the imported image, so the surface would appear upside
        // down. Mirror the surface visual on the Y axis (about its vertical centre, set per-size in
        // UpdateVisualSize) to correct the orientation. This is platform-neutral: both the Windows
        // shared-texture bridge and the Linux external-memory bridge feed the same compositor.
        _visual.Scale = new Vector3D(1, -1, 1);

        UpdateVisualSize();
        ElementComposition.SetElementChildVisual(this, _visual);

        // May return null when the current Avalonia render session has no GPU interop backend.
        _interop = await compositor.TryGetCompositionGpuInterop();
        if (_interop is null)
        {
            // GPU interop is unavailable in this render session. Log the condition and continue;
            // PresentAsync will skip frame presentation without throwing or terminating the app.
            _logger.LogWarning(
                "Composition GPU interop is unavailable in the current Avalonia render session; "
                    + "engine frame presentation will be skipped."
            );
        }
    }

    /// <summary>
    /// Imports the described shared engine image through <see cref="ICompositionGpuInterop"/> and
    /// updates the composition drawing surface with it, keeping the surface visual sized to the control.
    /// When GPU interop is unavailable, or the described image has a zero dimension, presentation is
    /// skipped without throwing.
    /// </summary>
    /// <param name="image">The platform-neutral description of the shared engine output image.</param>
    /// <param name="sync">The synchronization step used to update the surface (keyed mutex on Windows, semaphore on Linux).</param>
    public async Task PresentAsync(SharedImageDescription image, ISurfaceUpdateSync sync)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(sync);

        if (_interop is null || _surface is null)
        {
            // GPU interop unavailable in this render session; skip presentation.
            return;
        }

        if (image.Width == 0 || image.Height == 0)
        {
            return;
        }

        // Reuse the previously imported image while the shared texture is unchanged; only re-import on
        // the first frame or after a resize. Re-importing every frame is a major cost and pipeline
        // stall, and was the main source of poor performance.
        ICompositionImportedGpuImage imported = GetOrImportImage(image);

        UpdateVisualSize();

        // Update the surface with the freshly written frame; the sync abstraction performs the
        // platform wait/signal (keyed-mutex acquire/release on Windows, semaphore wait/signal on Linux).
        await sync.UpdateAsync(_surface, imported);
    }

    /// <summary>
    /// Returns the imported GPU image for the given description, importing it only when the shared
    /// image has changed since the last import (first frame or resize). The imported image is cached
    /// and reused across frames because importing is expensive; the per-frame surface update
    /// re-synchronizes the same imported image via the keyed mutex / semaphores.
    /// </summary>
    private ICompositionImportedGpuImage GetOrImportImage(SharedImageDescription image)
    {
        // A size/format change means every buffer's handle is stale (the bridge recreated them on
        // resize), so drop the whole cache and re-import lazily.
        if (
            image.Width != _importedWidth
            || image.Height != _importedHeight
            || image.Format != _importedFormat
        )
        {
            ClearImportedImages();
            _importedWidth = image.Width;
            _importedHeight = image.Height;
            _importedFormat = image.Format;
        }

        nint key = image.NtHandle != nint.Zero ? image.NtHandle : image.MemoryFd;
        if (_importedImages.TryGetValue(key, out ICompositionImportedGpuImage? existing))
        {
            return existing;
        }

        var platformHandle = new PlatformHandle(key, image.ExternalHandleType);

        var properties = new PlatformGraphicsExternalImageProperties
        {
            Width = (int)image.Width,
            Height = (int)image.Height,
            Format = MapFormat(image.Format),
            MemorySize = image.MemorySize,
        };

        ICompositionImportedGpuImage imported = _interop!.ImportImage(platformHandle, properties);
        _importedImages[key] = imported;
        return imported;
    }

    /// <summary>Disposes and clears every cached imported image.</summary>
    private void ClearImportedImages()
    {
        if (_importedImages.Count == 0)
        {
            return;
        }

        var toDispose = new List<ICompositionImportedGpuImage>(_importedImages.Values);
        _importedImages.Clear();
        foreach (ICompositionImportedGpuImage imported in toDispose)
        {
            _ = DisposeImportedAsync(imported);
        }
    }

    /// <summary>
    /// Releases the most recently imported GPU image, which references the current engine output.
    /// Idempotent and safe to call from the UI thread during engine reassignment or teardown. The
    /// composition surface and visual are left intact so presentation can resume after a new engine
    /// output is imported. The imported image is disposed asynchronously (fire-and-forget) because
    /// composition GPU resources expose only <see cref="IAsyncDisposable"/>; failures are logged and
    /// swallowed so teardown never throws.
    /// </summary>
    public void ReleaseImportedImage()
    {
        ClearImportedImages();

        // Reset the cached size/format so the next PresentAsync re-imports the (new) shared images.
        _importedWidth = 0;
        _importedHeight = 0;
        _importedFormat = default;
    }

    /// <summary>
    /// Tears down every interop resource this presenter created: the most recently imported GPU image
    /// and the composition child visual/surface. Idempotent and safe to call from the UI thread during
    /// unload or disposal; after it runs, <see cref="PresentAsync"/> becomes a no-op because the
    /// surface and interop are cleared.
    /// </summary>
    public void Cleanup()
    {
        ReleaseImportedImage();

        // Detach the surface visual from this element so no composition work references it after
        // teardown, then drop the surface/interop so PresentAsync short-circuits.
        if (_visual is not null)
        {
            ElementComposition.SetElementChildVisual(this, null);
            _visual = null;
        }

        _surface = null;
        _interop = null;
    }

    /// <summary>Disposes an imported composition image, logging and swallowing any failure.</summary>
    private static async Task DisposeImportedAsync(ICompositionImportedGpuImage imported)
    {
        try
        {
            await imported.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose imported composition image during teardown.");
        }
    }

    /// <summary>Keeps the surface visual sized to the control whenever the layout arranges it.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Size result = base.ArrangeOverride(finalSize);
        UpdateVisualSize(result);
        return result;
    }

    /// <summary>Keeps the composition surface visual sized to the control's rendered bounds.</summary>
    private void UpdateVisualSize() => UpdateVisualSize(Bounds.Size);

    /// <summary>Keeps the composition surface visual sized to the supplied dimensions.</summary>
    private void UpdateVisualSize(Size size)
    {
        if (_visual is not null)
        {
            _visual.Size = new Vector(size.Width, size.Height);
            // Keep the Y-flip (set in InitializeCompositionAsync) pivoting about the current centre
            // so the mirrored surface stays in place as the control resizes.
            _visual.CenterPoint = new Vector3D(size.Width / 2.0, size.Height / 2.0, 0);
        }
    }

    /// <summary>Maps the engine pixel format onto the Avalonia external-image format.</summary>
    private static PlatformGraphicsExternalImageFormat MapFormat(Format format) =>
        format switch
        {
            Format.BGRA_UN8 or Format.BGRA_SRGB8 =>
                PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm,
            _ => PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,
        };
}
