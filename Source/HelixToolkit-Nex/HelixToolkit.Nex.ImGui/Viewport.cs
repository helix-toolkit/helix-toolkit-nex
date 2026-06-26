using System.Numerics;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.ImGui;

/// <summary>
/// Logical operations that can be bound to a mouse button. The numeric values also encode the
/// fixed priority order used when more than one operation is mapped to the same button
/// (Pick first, then Rotate, then Pan).
/// </summary>
/// <remarks>Requirement 4.5.</remarks>
public enum ViewportOperation
{
    /// <summary>Picking. Highest priority when buttons collide.</summary>
    Pick = 0,

    /// <summary>Orbit/rotate gesture. Medium priority.</summary>
    Rotate = 1,

    /// <summary>Pan gesture. Lowest priority.</summary>
    Pan = 2,
}

/// <summary>
/// Immutable per-frame snapshot of everything <c>ViewportInputCore</c> needs from ImGui for a
/// single frame, so the pure interaction core never touches the static ImGui API.
/// </summary>
internal readonly record struct ViewportFrameInput
{
    // Measurement
    /// <summary>Content region width (<c>GetContentRegionAvail().X</c>).</summary>
    public float ContentWidth { get; init; }

    /// <summary>Content region height (<c>GetContentRegionAvail().Y</c>).</summary>
    public float ContentHeight { get; init; }

    // Pointer (logical, relative to canvas top-left)
    /// <summary>Pointer X relative to the canvas top-left (<c>mousePos.X - canvasPos.X</c>).</summary>
    public float RelativeX { get; init; }

    /// <summary>Pointer Y relative to the canvas top-left (<c>mousePos.Y - canvasPos.Y</c>).</summary>
    public float RelativeY { get; init; }

    // Hover / focus gating
    /// <summary>Whether the pointer is over the drawn image (<c>IsItemHovered()</c>).</summary>
    public bool Hovered { get; init; }

    // Button mappings for this frame
    /// <summary>Button mapped to the rotate operation this frame.</summary>
    public ImGuiMouseButton RotateButton { get; init; }

    /// <summary>Button mapped to the pan operation this frame.</summary>
    public ImGuiMouseButton PanButton { get; init; }

    /// <summary>Button mapped to the pick operation this frame.</summary>
    public ImGuiMouseButton PickButton { get; init; }

    // Per-button transitions (resolved by the façade for the mapped buttons)
    /// <summary>Rotate button transitioned to pressed (<c>IsMouseClicked(RotateButton)</c>).</summary>
    public bool RotatePressed { get; init; }

    /// <summary>Rotate button released (<c>IsMouseReleased(RotateButton)</c>).</summary>
    public bool RotateReleased { get; init; }

    /// <summary>Pan button transitioned to pressed.</summary>
    public bool PanPressed { get; init; }

    /// <summary>Pan button released.</summary>
    public bool PanReleased { get; init; }

    /// <summary>Pick button completed a press+release click over the image.</summary>
    public bool PickClicked { get; init; }

    // Scroll
    /// <summary>Scroll-wheel delta for the frame (<c>GetIO().MouseWheel</c>).</summary>
    public float ScrollWheel { get; init; }

    // Configuration
    /// <summary>Whether pointer reporting to the render context is enabled.</summary>
    public bool ReportPointer { get; init; }

    /// <summary>Whether a pick callback is configured.</summary>
    public bool HasPickCallback { get; init; }

    /// <summary>Display scale factor (validated to be greater than zero).</summary>
    public float DpiScale { get; init; }
}

/// <summary>
/// Immutable per-frame set of decisions returned by <c>ViewportInputCore.Process</c>. The
/// <c>Viewport</c> façade dispatches these to its collaborators; nothing here calls ImGui or the GPU.
/// </summary>
internal readonly record struct ViewportFrameActions
{
    // Size reporting
    /// <summary>False when the content region was non-positive and no measurement was taken.</summary>
    public bool SizeMeasured { get; init; }

    /// <summary>Measured viewport size in physical pixels reported to the render context.</summary>
    public Size ViewportSize { get; init; }

    /// <summary>Whether to report <see cref="ViewportSize"/> to the render context this frame.</summary>
    public bool SizeChanged { get; init; }

    // Camera gesture forwarding (coordinates are logical relative-pointer values)
    /// <summary>Whether to invoke rotate-begin on the camera controller.</summary>
    public bool RotateBegin { get; init; }

    /// <summary>Whether to invoke rotate-delta on the camera controller.</summary>
    public bool RotateDelta { get; init; }

    /// <summary>Whether to invoke pan-begin on the camera controller.</summary>
    public bool PanBegin { get; init; }

    /// <summary>Whether to invoke pan-delta on the camera controller.</summary>
    public bool PanDelta { get; init; }

    /// <summary>Logical relative-pointer X used for gesture forwarding.</summary>
    public float GestureX { get; init; }

    /// <summary>Logical relative-pointer Y used for gesture forwarding.</summary>
    public float GestureY { get; init; }

    // Zoom
    /// <summary>Whether to invoke zoom-delta on the camera controller.</summary>
    public bool Zoom { get; init; }

    /// <summary>Zoom-delta value (the frame scroll-wheel value) to forward.</summary>
    public float ZoomDelta { get; init; }

    // Pointer reporting (physical pixels)
    /// <summary>Whether to report the pointer to the render context this frame.</summary>
    public bool ReportPointer { get; init; }

    /// <summary>Pointer X in physical pixels reported to the render context.</summary>
    public int PointerX { get; init; }

    /// <summary>Pointer Y in physical pixels reported to the render context.</summary>
    public int PointerY { get; init; }

    // Picking (physical pixels, clamped to [0, size-1])
    /// <summary>Whether to invoke the pick callback this frame.</summary>
    public bool Pick { get; init; }

    /// <summary>Pick X in physical pixels, clamped to <c>[0, ViewportSize.Width - 1]</c>.</summary>
    public int PickX { get; init; }

    /// <summary>Pick Y in physical pixels, clamped to <c>[0, ViewportSize.Height - 1]</c>.</summary>
    public int PickY { get; init; }
}

/// <summary>
/// Pure, dependency-free interaction core for the viewport. Holds the persistent interaction
/// state (last measured size, last relative pointer, drag flags) and decides the per-frame
/// actions from an immutable <see cref="ViewportFrameInput"/> snapshot. Performs no ImGui calls
/// and no GPU work, so it can be exercised with property-based tests without a live context.
/// </summary>
internal sealed class ViewportInputCore
{
    /// <summary>Last successfully measured viewport size in physical pixels. <c>(0,0)</c> initially.</summary>
    /// <remarks>Requirement 2.3.</remarks>
    public Size ViewportSize { get; private set; }

    /// <summary>Last relative pointer position in logical coordinates. <c>(0,0)</c> initially.</summary>
    /// <remarks>Requirement 8.3.</remarks>
    public Vector2 RelativePointer { get; private set; }

    /// <summary>Whether a rotate drag is currently in progress.</summary>
    /// <remarks>Requirements 5.1, 6.1.</remarks>
    public bool RotateActive { get; private set; }

    /// <summary>Whether a pan drag is currently in progress.</summary>
    /// <remarks>Requirements 5.3, 6.2.</remarks>
    public bool PanActive { get; private set; }

    /// <summary>
    /// Advances the state machine for one frame and returns the decided actions. Pure with
    /// respect to ImGui/GPU: depends only on <paramref name="input"/> and prior state.
    /// </summary>
    /// <param name="input">Immutable per-frame snapshot of ImGui state.</param>
    /// <returns>The decided actions for the façade to dispatch.</returns>
    public ViewportFrameActions Process(in ViewportFrameInput input)
    {
        // Size measurement, DPI conversion, and change detection (Requirements 2.1, 2.4, 9.1).
        if (input.ContentWidth > 0f && input.ContentHeight > 0f)
        {
            int width = Math.Max(1, (int)(input.ContentWidth * input.DpiScale));
            int height = Math.Max(1, (int)(input.ContentHeight * input.DpiScale));
            Size measured = new(width, height);
            bool sizeChanged = measured != ViewportSize;
            ViewportSize = measured;

            // Relative-pointer update from the frame snapshot (Requirement 8.3).
            RelativePointer = new Vector2(input.RelativeX, input.RelativeY);

            // Button-priority resolution (Requirement 4.5, Property 4): when more than one
            // operation is mapped to the same button, only the highest-priority operation for that
            // button is evaluated, using the fixed order Pick > Rotate > Pan. The resolved per-
            // operation transitions below carry the surviving (non-suppressed) press/release/click
            // flags; the subsequent drag state machine (task 2.8), zoom (2.10), pointer reporting
            // (2.12), and pick (2.14) logic consume these instead of the raw snapshot transitions.
            ResolvedButtonInput resolved = ResolveButtons(in input);

            // Rotate/pan drag state machine (Requirements 5.1-5.4, 6.1-6.5, Property 5). The two
            // operations are advanced independently using the same rules, so clearing one never
            // affects the other (Requirement 6.4). Order within an operation is begin -> delta ->
            // release so that a press+release within the same frame emits begin once and leaves the
            // drag cleared, rather than getting stuck active.

            // Rotate begin (Requirement 5.1): only when hovered, no rotate drag is active, and the
            // (priority-resolved) rotate button transitioned to pressed.
            bool rotateBegin = input.Hovered && !RotateActive && resolved.RotatePressed;
            if (rotateBegin)
            {
                RotateActive = true;
            }

            // Rotate delta (Requirement 5.2): forwarded every frame while the drag is active,
            // regardless of hover, but not on the begin frame (the begin frame forwards begin).
            bool rotateDelta = RotateActive && !rotateBegin;

            // Rotate release (Requirements 6.1, 6.3): clear the drag on button release regardless
            // of hover, stopping all subsequent rotate-delta forwarding.
            if (resolved.RotateReleased)
            {
                RotateActive = false;
            }

            // Pan begin (Requirement 5.3): only when hovered, no pan drag is active, and the
            // (priority-resolved) pan button transitioned to pressed.
            bool panBegin = input.Hovered && !PanActive && resolved.PanPressed;
            if (panBegin)
            {
                PanActive = true;
            }

            // Pan delta (Requirement 5.4): forwarded every frame while the drag is active,
            // regardless of hover, but not on the begin frame.
            bool panDelta = PanActive && !panBegin;

            // Pan release (Requirements 6.2, 6.3): clear the drag on button release regardless of
            // hover, stopping all subsequent pan-delta forwarding.
            if (resolved.PanReleased)
            {
                PanActive = false;
            }

            // Scroll-wheel zoom dead-zone (Requirements 5.5, 5.6, Property 6): forward a zoom-delta
            // equal to the frame scroll value if and only if the viewport is hovered and the
            // absolute scroll-wheel delta exceeds the 0.001 dead-zone; otherwise skip zoom.
            bool zoom = input.Hovered && Math.Abs(input.ScrollWheel) > 0.001f;

            // Pointer-reporting gating and DPI-scaled coordinates (Requirements 8.1, 8.2, 8.5,
            // 9.2, Property 9): report the pointer to the render context if and only if the
            // viewport is hovered and reporting is enabled by configuration. The physical-pixel
            // coordinates are the logical relative pointer multiplied by the DPI scale and
            // truncated toward zero (the (int) cast), matching the size-measurement conversion.
            bool reportPointer = input.Hovered && input.ReportPointer;
            int pointerX = (int)(input.RelativeX * input.DpiScale);
            int pointerY = (int)(input.RelativeY * input.DpiScale);

            // Pick gating and clamped coordinate computation (Requirements 7.1-7.5, 9.2,
            // Properties 7 and 8): decide a pick if and only if the viewport is hovered, the
            // (priority-resolved) pick-button click completed, a pick callback is configured, and
            // the measured size has a positive width and height. The measured size is always > 0
            // in this branch because of the 1-pixel floor above, but Requirement 7.5 is checked
            // explicitly here. The pick coordinates are the logical relative pointer multiplied by
            // the DPI scale, truncated toward zero (the (int) cast), then clamped to the inclusive
            // range [0, size - 1] (Requirement 7.2).
            bool pick = input.Hovered
                        && resolved.PickClicked
                        && input.HasPickCallback
                        && measured.Width > 0
                        && measured.Height > 0;
            int pickX = Math.Clamp((int)(input.RelativeX * input.DpiScale), 0, measured.Width - 1);
            int pickY = Math.Clamp((int)(input.RelativeY * input.DpiScale), 0, measured.Height - 1);

            return new ViewportFrameActions
            {
                SizeMeasured = true,
                ViewportSize = measured,
                SizeChanged = sizeChanged,
                RotateBegin = rotateBegin,
                RotateDelta = rotateDelta,
                PanBegin = panBegin,
                PanDelta = panDelta,
                GestureX = input.RelativeX,
                GestureY = input.RelativeY,
                Zoom = zoom,
                ZoomDelta = zoom ? input.ScrollWheel : 0f,
                ReportPointer = reportPointer,
                PointerX = pointerX,
                PointerY = pointerY,
                Pick = pick,
                PickX = pickX,
                PickY = pickY,
            };
        }

        // Non-positive content-region guard (Requirement 2.5, Property 3): when either content
        // dimension is <= 0, retain all prior interaction state and skip the interactive work for
        // the frame. The previously measured ViewportSize, RelativePointer, and the RotateActive /
        // PanActive drag flags are left untouched, and the returned actions carry only their
        // defaults (false) so the façade reports no size change, no pointer, and no rotate / pan /
        // zoom gesture or pick for this frame.
        return new ViewportFrameActions
        {
            SizeMeasured = false,
            ViewportSize = ViewportSize,
            SizeChanged = false,
        };
    }

    /// <summary>
    /// Per-operation transition flags after button-priority resolution. When more than one
    /// operation is mapped to the same <see cref="ImGuiMouseButton"/>, only the highest-priority
    /// operation for that button keeps its transition flags (fixed order Pick &gt; Rotate &gt; Pan);
    /// the lower-priority operations sharing that button are suppressed for the frame.
    /// </summary>
    /// <remarks>Requirement 4.5.</remarks>
    private readonly record struct ResolvedButtonInput
    {
        /// <summary>Pick click that survived resolution (Pick has the highest priority).</summary>
        public bool PickClicked { get; init; }

        /// <summary>Rotate press that survived resolution.</summary>
        public bool RotatePressed { get; init; }

        /// <summary>Rotate release that survived resolution.</summary>
        public bool RotateReleased { get; init; }

        /// <summary>Pan press that survived resolution.</summary>
        public bool PanPressed { get; init; }

        /// <summary>Pan release that survived resolution.</summary>
        public bool PanReleased { get; init; }
    }

    /// <summary>
    /// Resolves colliding button mappings using the fixed priority order Pick &gt; Rotate &gt; Pan
    /// (the numeric order encoded by <see cref="ViewportOperation"/>). Pick always survives; Rotate
    /// survives only when its button differs from the Pick button; Pan survives only when its
    /// button differs from both the Pick and Rotate buttons. The transitions of any suppressed
    /// (lower-priority) operation sharing a button are cleared for the frame so the downstream
    /// gesture, zoom, pointer, and pick logic evaluates only the highest-priority operation per
    /// button.
    /// </summary>
    /// <param name="input">Immutable per-frame snapshot of ImGui state.</param>
    /// <returns>The priority-resolved per-operation transition flags.</returns>
    /// <remarks>Requirement 4.5, Property 4.</remarks>
    private static ResolvedButtonInput ResolveButtons(in ViewportFrameInput input)
    {
        // Pick has the highest priority and is always evaluated.
        // Rotate is suppressed when it collides with the Pick button.
        bool rotateEnabled = input.RotateButton != input.PickButton;

        // Pan is suppressed when it collides with either the Pick or the Rotate button.
        bool panEnabled = input.PanButton != input.PickButton
                          && input.PanButton != input.RotateButton;

        return new ResolvedButtonInput
        {
            PickClicked = input.PickClicked,
            RotatePressed = rotateEnabled && input.RotatePressed,
            RotateReleased = rotateEnabled && input.RotateReleased,
            PanPressed = panEnabled && input.PanPressed,
            PanReleased = panEnabled && input.PanReleased,
        };
    }
}

/// <summary>
/// Reusable 3D viewport region for ImGui-hosted demos. Draws an offscreen render texture inside
/// a borderless ImGui window and translates mouse input into camera-controller and picking
/// operations. The interesting interaction rules live in the pure <see cref="ViewportInputCore"/>;
/// this façade performs the ImGui I/O and dispatches the decided actions to the
/// <see cref="RenderContext"/>, <see cref="ICameraController"/>, and the pick callback.
/// </summary>
public sealed class Viewport
{
    /// <summary>The render context to report size and pointer to. Never null after construction.</summary>
    private readonly RenderContext _renderContext;

    /// <summary>Pure interaction core that holds persistent state and decides per-frame actions.</summary>
    private readonly ViewportInputCore _core = new();

    /// <summary>Backing field for <see cref="DpiScale"/>; always greater than zero.</summary>
    private float _dpiScale;

    /// <summary>
    /// Initializes a new <see cref="Viewport"/>.
    /// </summary>
    /// <param name="renderContext">Render context to report size and pointer to. Required.</param>
    /// <param name="cameraController">Optional controller for gesture forwarding; may be set later.</param>
    /// <param name="dpiScale">Display scale factor (greater than zero). Defaults to <c>1</c>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="renderContext"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="dpiScale"/> is less than or equal to zero.</exception>
    /// <remarks>Requirements 1.1, 1.2, 1.3, 9.4.</remarks>
    public Viewport(RenderContext renderContext,
                    ICameraController? cameraController = null,
                    float dpiScale = 1f)
    {
        _renderContext = renderContext ?? throw new ArgumentNullException(nameof(renderContext));
        if (dpiScale <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(dpiScale));
        }

        CameraController = cameraController;
        _dpiScale = dpiScale;
    }

    /// <summary>The camera controller that receives gestures. May be null.</summary>
    /// <remarks>Requirements 1.2, 1.4, 1.5.</remarks>
    public ICameraController? CameraController { get; set; }

    /// <summary>Button that triggers rotate. Defaults to <see cref="ImGuiMouseButton.Right"/>.</summary>
    /// <remarks>Requirement 4.1.</remarks>
    public ImGuiMouseButton RotateButton { get; set; } = ImGuiMouseButton.Right;

    /// <summary>Button that triggers pan. Defaults to <see cref="ImGuiMouseButton.Middle"/>.</summary>
    /// <remarks>Requirement 4.2.</remarks>
    public ImGuiMouseButton PanButton { get; set; } = ImGuiMouseButton.Middle;

    /// <summary>Button that triggers pick. Defaults to <see cref="ImGuiMouseButton.Left"/>.</summary>
    /// <remarks>Requirement 4.3.</remarks>
    public ImGuiMouseButton PickButton { get; set; } = ImGuiMouseButton.Left;

    /// <summary>Whether to report the pointer to the render context each frame. Defaults to <c>true</c>.</summary>
    /// <remarks>Requirement 8.4.</remarks>
    public bool ReportPointerToRenderContext { get; set; } = true;

    /// <summary>Callback invoked with viewport-relative integer pixel coordinates on pick.</summary>
    /// <remarks>Requirement 7.3.</remarks>
    public Action<int, int>? PickCallback { get; set; }

    /// <summary>
    /// Display scale factor used to convert logical coordinates to physical pixels. Defaults to
    /// <c>1</c>. Setting a value less than or equal to zero is rejected and the previous value is
    /// retained.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when set to a value less than or equal to zero.</exception>
    /// <remarks>Requirements 9.3, 9.4.</remarks>
    public float DpiScale
    {
        get => _dpiScale;
        set
        {
            if (value <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(DpiScale));
            }

            _dpiScale = value;
        }
    }

    /// <summary>Optional ImGui window identifier; defaults to a stable internal id.</summary>
    public string WindowId { get; set; } = "##Viewport";

    /// <summary>
    /// Optional title text drawn as an overlay in the top-left corner of the viewport image.
    /// When null or empty (the default) no title is drawn. The overlay does not consume layout
    /// space and does not affect hit-testing of the image.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>Title text color. Defaults to opaque white.</summary>
    public Vector4 TitleColor { get; set; } = new(1f, 1f, 1f, 1f);

    /// <summary>
    /// Most recently measured viewport size in physical pixels. <c>(0,0)</c> before the first
    /// measurement. Read-only projection of <see cref="ViewportInputCore"/> state.
    /// </summary>
    /// <remarks>Requirement 2.3.</remarks>
    public Size ViewportSize => _core.ViewportSize;

    /// <summary>
    /// Most recent viewport-relative pointer position in logical coordinates. <c>(0,0)</c>
    /// initially. Read-only projection of <see cref="ViewportInputCore"/> state.
    /// </summary>
    /// <remarks>Requirement 8.3.</remarks>
    public Vector2 RelativePointer => _core.RelativePointer;

    /// <summary>
    /// Whether the pointer was over the drawn viewport image during the most recent
    /// <see cref="Draw"/> call. <c>false</c> before the first frame. Exposes the hover state that
    /// gates input so callers can implement additional hover-driven behavior (e.g. continuous
    /// picking) without re-querying ImGui.
    /// </summary>
    public bool IsHovered { get; private set; }

    /// <summary>
    /// Draws the viewport for the current frame.
    /// </summary>
    /// <param name="offscreenTexture">Handle of the texture to display.</param>
    /// <param name="windowPos">Optional window position; fills the host area when null.</param>
    /// <param name="windowSize">Optional window size; fills the host area when null.</param>
    public void Draw(TextureHandle offscreenTexture,
                     Vector2? windowPos = null,
                     Vector2? windowSize = null)
    {
        // Borderless window flags: suppress title bar, resize, move, collapse, scrollbar,
        // scroll-with-mouse, and bring-to-front-on-focus so the 3D scene fills the region
        // without chrome (Requirement 3.1).
        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse;

        // Apply the caller-supplied position/size when provided, otherwise fill the host ImGui
        // work area. The null-coalescing handles the all-supplied (Requirement 3.5), none-supplied
        // (Requirement 3.6), and mixed cases uniformly.
        ImGuiViewportPtr mainViewport = Gui.GetMainViewport();
        Gui.SetNextWindowPos(windowPos ?? mainViewport.WorkPos, ImGuiCond.Always);
        Gui.SetNextWindowSize(windowSize ?? mainViewport.WorkSize, ImGuiCond.Always);

        // Zero the window padding for the duration of the region. PushStyleVar before Begin and
        // PopStyleVar immediately after Begin applies the override to this window only and restores
        // the prior padding for everything that follows (Requirements 3.1, 3.2).
        Gui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        Gui.Begin(WindowId, flags);
        Gui.PopStyleVar();

        // Pair Begin/End unconditionally so the window stack stays balanced even if drawing throws.
        try
        {
            // Content region available for the image, in logical ImGui units (Requirement 3.3).
            Vector2 contentSize = Gui.GetContentRegionAvail();

            // Record the canvas top-left from the cursor screen position before drawing the image,
            // so pointer coordinates can be made relative to it. Recorded regardless of whether the
            // image is actually drawn (Requirements 3.4, 3.7).
            Vector2 canvasPos = Gui.GetCursorScreenPos();

            // Draw the offscreen texture at the content-region size. When the handle is not
            // allocated, skip the image draw but keep the recorded canvas position and complete the
            // frame without error (Requirements 3.3, 3.7).
            if (offscreenTexture.Valid)
            {
                Gui.Image((nint)offscreenTexture.Index, contentSize, Vector2.Zero, Vector2.One);
            }

            // Optional title overlay drawn directly on the window draw list, on top of the image.
            // Using the draw list (rather than a Text widget) keeps the title from consuming layout
            // space or interfering with the image's hit-testing. The title is centered
            // horizontally at the top of the viewport, and a 1px dark shadow keeps the text legible
            // over bright scene content.
            if (!string.IsNullOrEmpty(Title) && contentSize.X > 0f && contentSize.Y > 0f)
            {
                ImDrawListPtr drawList = Gui.GetWindowDrawList();
                Vector2 textSize = Gui.CalcTextSize(Title);
                float textX = canvasPos.X + MathF.Max(0f, (contentSize.X - textSize.X) * 0.5f);
                Vector2 textPos = new(textX, canvasPos.Y + 6f);
                uint shadow = Gui.GetColorU32(new Vector4(0f, 0f, 0f, 0.7f));
                uint color = Gui.GetColorU32(TitleColor);
                drawList.AddText(textPos + new Vector2(1f, 1f), shadow, Title);
                drawList.AddText(textPos, color, Title);
            }

            // Build the immutable per-frame snapshot from the live ImGui state. Hover comes from
            // the just-drawn image (IsItemHovered), the mouse position is made relative to the
            // recorded canvas top-left, and the per-button transitions are read for the mapped
            // buttons. Picking represents a completed press+release click, so it maps to
            // IsMouseReleased(PickButton) (Requirement 7.1).
            Vector2 mouse = Gui.GetMousePos();
            bool hovered = Gui.IsItemHovered();
            IsHovered = hovered;
            ViewportFrameInput input = new()
            {
                ContentWidth = contentSize.X,
                ContentHeight = contentSize.Y,
                RelativeX = mouse.X - canvasPos.X,
                RelativeY = mouse.Y - canvasPos.Y,
                Hovered = hovered,
                RotateButton = RotateButton,
                PanButton = PanButton,
                PickButton = PickButton,
                RotatePressed = Gui.IsMouseClicked(RotateButton),
                RotateReleased = Gui.IsMouseReleased(RotateButton),
                PanPressed = Gui.IsMouseClicked(PanButton),
                PanReleased = Gui.IsMouseReleased(PanButton),
                PickClicked = Gui.IsMouseReleased(PickButton),
                ScrollWheel = Gui.GetIO().MouseWheel,
                ReportPointer = ReportPointerToRenderContext,
                HasPickCallback = PickCallback is not null,
                DpiScale = _dpiScale,
            };

            // Decide the per-frame actions from the pure core (no ImGui/GPU work).
            ViewportFrameActions actions = _core.Process(in input);

            // Dispatch the decided actions. Every camera-controller forward uses null-conditional
            // dispatch so a missing controller never throws and never forwards (Requirements 1.5,
            // 1.6, Property 10).

            // Report the measured size: update the camera viewport dimensions (Requirement 2.2)
            // and, when the integer size changed, the render context window size (Requirement 2.4).
            if (actions.SizeMeasured)
            {
                if (CameraController is not null)
                {
                    CameraController.ViewportWidth = actions.ViewportSize.Width;
                    CameraController.ViewportHeight = actions.ViewportSize.Height;
                }

                if (actions.SizeChanged)
                {
                    _renderContext.WindowSize = actions.ViewportSize;
                }
            }

            // Forward rotate/pan begin and delta gestures with the logical relative pointer
            // (Requirements 1.5, 1.6, 5.1, 5.2, 5.3, 5.4).
            if (actions.RotateBegin)
            {
                CameraController?.OnRotateBegin(actions.GestureX, actions.GestureY);
            }

            if (actions.RotateDelta)
            {
                CameraController?.OnRotateDelta(actions.GestureX, actions.GestureY);
            }

            if (actions.PanBegin)
            {
                CameraController?.OnPanBegin(actions.GestureX, actions.GestureY);
            }

            if (actions.PanDelta)
            {
                CameraController?.OnPanDelta(actions.GestureX, actions.GestureY);
            }

            // Forward zoom-delta when above the scroll dead-zone while hovered (Requirement 5.5).
            if (actions.Zoom)
            {
                CameraController?.OnZoomDelta(actions.ZoomDelta);
            }

            // Report the pointer to the render context with DPI-scaled coordinates (Requirement 8.1).
            if (actions.ReportPointer)
            {
                _renderContext.SetPointer(actions.PointerX, actions.PointerY);
            }

            // Invoke the pick callback with clamped, DPI-scaled coordinates (Requirement 7.1). The
            // null-conditional invoke means a missing callback never throws.
            if (actions.Pick)
            {
                PickCallback?.Invoke(actions.PickX, actions.PickY);
            }
        }
        finally
        {
            Gui.End();
        }
    }
}
