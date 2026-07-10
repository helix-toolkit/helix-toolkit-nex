using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.Avalonia.Tests;

/// <summary>
/// avalonia-interop Property 8: Viewport size propagates to the camera controller.
///
/// For any nonzero width and height, updating the viewport size sets the camera controller's
/// <c>ViewportWidth</c> and <c>ViewportHeight</c> to exactly those values.
///
/// <para>
/// This drives the real shared <c>UpdateViewportSize(float,float)</c> method
/// (<c>ViewportCommon.cs</c>) — the exact method the render tick's <c>EnsureSize</c> calls on the
/// first nonzero-size tick and on every subsequent resize (Requirement 8.3). The method early-returns
/// unless <c>CanHandleInput</c> holds, so the test uses the input-ready viewport seam
/// (<see cref="PointerInputTestSupport.CreateInputReadyViewport"/>) and observes a
/// <see cref="RecordingCameraController"/> injected through the real public dependency property.
/// </para>
///
/// **Validates: Requirements 8.3**
/// </summary>
[TestClass]
public sealed class ViewportSizePropagationTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>**Validates: Requirements 8.3**</summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    [TestCategory("Property 8")]
    public void UpdateViewportSize_SetsCameraViewportToExactInputs()
    {
        // Nonzero widths/heights spanning a realistic control-size range.
        var gen =
            from width in Gen.Choose(1, 10_000)
            from height in Gen.Choose(1, 10_000)
            select ((float)width, (float)height);

        Prop.ForAll(
                Arb.From(gen),
                ((float Width, float Height) t) =>
                {
                    var camera = new RecordingCameraController();
                    var viewport = PointerInputTestSupport.CreateInputReadyViewport(camera);

                    PointerInputTestSupport.UpdateViewportSize(viewport, t.Width, t.Height);

                    return camera.ViewportWidth == t.Width
                        && camera.ViewportHeight == t.Height;
                })
            .Check(FsCheckConfig);
    }
}

/// <summary>
/// avalonia-interop Property 9: A frame is presented iff the context is valid and the size is nonzero.
///
/// For any combination of engine / render-context / viewport-client validity and control dimensions,
/// a render tick presents a frame iff the engine, render context, and viewport client are all valid
/// AND both the width and height are greater than zero; otherwise the tick skips resource creation and
/// presentation.
///
/// <para>
/// <b>Seam.</b> A full GPU-driven tick cannot run headlessly: <c>TickAsync</c> needs a live Vulkan
/// engine, a compositor-backed <c>CompositionSurfacePresenter</c>, and a real bridge, and its width /
/// height are read from <c>ActualWidth</c>/<c>ActualHeight</c> which map to Avalonia
/// <c>Bounds</c> and are not settable without a live layout pass. The presentation decision, however,
/// is a pure boolean guard assembled from two production checks in <c>TickAsync</c>
/// (<c>HelixViewport.Render.cs</c>): the entry guard <c>IsContextValid &amp;&amp; ViewportClient is not
/// null</c> and the zero-size skip <c>width &gt; 0 &amp;&amp; height &gt; 0</c>. This property exercises
/// that guard over <em>real production state</em>: the real protected <c>IsContextValid</c> property
/// (computed from the concrete <c>_engine</c>/<c>_renderContext</c> fields) and the real public
/// <c>ViewportClient</c> property, combined with generated width/height values. It asserts the guard
/// both (a) matches the independent model <c>engineValid &amp;&amp; contextValid &amp;&amp; hasClient
/// &amp;&amp; width &gt; 0 &amp;&amp; height &gt; 0</c> and (b) is false whenever any precondition fails
/// (skip resource creation/presentation).
/// </para>
///
/// **Validates: Requirements 8.1, 8.4**
/// </summary>
[TestClass]
public sealed class PresentIffValidAndNonzeroTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>
    /// Mirrors the production <c>TickAsync</c> guard exactly, evaluated against the viewport's real
    /// <c>IsContextValid</c> and <c>ViewportClient</c> state plus the tick's width/height.
    /// </summary>
    private static bool ShouldPresent(HelixViewport viewport, float width, float height) =>
        PointerInputTestSupport.GetIsContextValid(viewport)
        && viewport.ViewportClient is not null
        && width > 0f
        && height > 0f;

    /// <summary>**Validates: Requirements 8.1, 8.4**</summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    [TestCategory("Property 9")]
    public void Tick_PresentsIff_ContextValidAndSizeNonzero()
    {
        // Independent booleans for each precondition, plus width/height that span zero, negative,
        // and positive so the nonzero-size guard is exercised in both directions.
        var gen =
            from engineValid in Gen.Elements(false, true)
            from contextValid in Gen.Elements(false, true)
            from hasClient in Gen.Elements(false, true)
            from width in Gen.Choose(-10, 10_000)
            from height in Gen.Choose(-10, 10_000)
            select (engineValid, contextValid, hasClient, (float)width, (float)height);

        Prop.ForAll(
                Arb.From(gen),
                ((bool EngineValid, bool ContextValid, bool HasClient, float Width, float Height) t) =>
                {
                    var viewport = PointerInputTestSupport.CreateBareViewport();

                    PointerInputTestSupport.SetEngineValid(viewport, t.EngineValid);
                    PointerInputTestSupport.SetRenderContextValid(viewport, t.ContextValid);
                    viewport.ViewportClient = t.HasClient ? new StubViewportClient() : null;

                    bool actual = ShouldPresent(viewport, t.Width, t.Height);

                    bool expected =
                        t.EngineValid
                        && t.ContextValid
                        && t.HasClient
                        && t.Width > 0f
                        && t.Height > 0f;

                    // The guard must equal the model, and (equivalently) must be false whenever any
                    // precondition fails — i.e. the tick skips presentation.
                    return actual == expected;
                })
            .Check(FsCheckConfig);
    }
}
