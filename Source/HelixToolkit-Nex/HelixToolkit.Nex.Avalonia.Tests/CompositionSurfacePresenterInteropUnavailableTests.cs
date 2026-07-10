using Avalonia.Rendering.Composition;
using HelixToolkit.Nex.Graphics;

namespace HelixToolkit.Nex.Avalonia.Tests;

/// <summary>
/// Feature: avalonia-interop, Task 6.3: interop-unavailable presentation path.
///
/// When the Avalonia render session exposes no usable <c>ICompositionGpuInterop</c>, the
/// <see cref="CompositionSurfacePresenter"/> reports <see cref="CompositionSurfacePresenter.GpuInteropAvailable"/>
/// as <see langword="false"/> and <see cref="CompositionSurfacePresenter.PresentAsync"/> skips
/// presentation without throwing or terminating the application.
///
/// A freshly constructed presenter that has never attached to a compositor models this
/// interop-unavailable state (its internal interop handle is never acquired), so we can assert the
/// skip behaviour deterministically without a live GPU/compositor.
///
/// **Validates: Requirements 4.4**
/// </summary>
[TestClass]
public sealed class CompositionSurfacePresenterInteropUnavailableTests
{
    /// <summary>
    /// Records whether <see cref="UpdateAsync"/> was invoked so the test can prove presentation was
    /// skipped (the surface update step is never reached when interop is unavailable).
    /// </summary>
    private sealed class RecordingSurfaceUpdateSync : ISurfaceUpdateSync
    {
        public bool WasInvoked { get; private set; }

        public Task UpdateAsync(CompositionDrawingSurface surface, ICompositionImportedGpuImage image)
        {
            WasInvoked = true;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Requirement 4.4: a presenter with no acquired GPU interop reports it as unavailable, and
    /// presenting a frame completes without throwing while skipping the surface update entirely.
    /// </summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    public async Task PresentAsync_WhenInteropUnavailable_SkipsPresentationWithoutThrowing()
    {
        // Never attached to a visual tree/compositor, so the GPU interop handle is never acquired.
        var presenter = new CompositionSurfacePresenter();

        Assert.IsFalse(
            presenter.GpuInteropAvailable,
            "A presenter that never attached to a compositor must report interop as unavailable.");

        // Non-zero dimensions ensure the skip is attributable to unavailable interop rather than the
        // separate zero-dimension guard.
        var image = new SharedImageDescription
        {
            Width = 64,
            Height = 64,
            Format = Format.BGRA_UN8,
            ExternalHandleType = "D3D11TextureGlobalSharedHandle",
        };

        var sync = new RecordingSurfaceUpdateSync();

        // Must complete without throwing.
        await presenter.PresentAsync(image, sync);

        Assert.IsFalse(
            sync.WasInvoked,
            "Presentation must be skipped (surface update never invoked) when interop is unavailable.");
    }
}
