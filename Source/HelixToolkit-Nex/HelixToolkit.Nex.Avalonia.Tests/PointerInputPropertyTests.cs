using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Interop;

namespace HelixToolkit.Nex.Avalonia.Tests;

// The four viewport mouse buttons form the input domain for the pointer-input properties.
file static class Buttons
{
    public static readonly ViewportMouseButton[] All =
    [
        ViewportMouseButton.None,
        ViewportMouseButton.Left,
        ViewportMouseButton.Middle,
        ViewportMouseButton.Right,
    ];

    public static Gen<ViewportMouseButton> Gen => FsCheck.Fluent.Gen.Elements(All);
}

/// <summary>
/// avalonia-interop Property 2: Pointer button mapping is total and exact.
///
/// For any Avalonia pointer button state, <c>ToViewportButton</c> yields <c>Left</c> when the left
/// button is pressed, <c>Middle</c> when the middle button is pressed, <c>Right</c> when the right
/// button is pressed, and <c>None</c> when no button is pressed — a total function with left &gt;
/// middle &gt; right priority when several buttons are held.
///
/// This exercises the real production helper <c>HelixViewport.ToViewportButton</c> against genuine
/// <see cref="Avalonia.Input.PointerPointProperties"/> values constructed over the full 2^3 button
/// domain (see <see cref="PointerInputTestSupport.MakeProperties"/>).
///
/// **Validates: Requirements 7.6**
/// </summary>
[TestClass]
public sealed class PointerButtonMappingTotalityTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    private static ViewportMouseButton Expected(bool left, bool middle, bool right)
    {
        if (left)
        {
            return ViewportMouseButton.Left;
        }
        if (middle)
        {
            return ViewportMouseButton.Middle;
        }
        if (right)
        {
            return ViewportMouseButton.Right;
        }
        return ViewportMouseButton.None;
    }

    /// <summary>**Validates: Requirements 7.6**</summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    [TestCategory("Property 2")]
    public void ToViewportButton_IsTotalAndExact_OverAllButtonStates()
    {
        var gen =
            from left in Gen.Elements(false, true)
            from middle in Gen.Elements(false, true)
            from right in Gen.Elements(false, true)
            select (left, middle, right);

        Prop.ForAll(
                Arb.From(gen),
                ((bool L, bool M, bool R) s) =>
                {
                    var props = PointerInputTestSupport.MakeProperties(s.L, s.M, s.R);
                    ViewportMouseButton mapped = HelixViewport.ToViewportButton(props);
                    return mapped == Expected(s.L, s.M, s.R);
                })
            .Check(FsCheckConfig);
    }
}

/// <summary>
/// avalonia-interop Property 3: Drag begins only on the bound button.
///
/// For any rotate/pan bindings and any pressed button, pressing begins the rotate action iff the
/// pressed button equals the rotate binding, begins the pan action iff it equals the pan binding
/// (rotate taking priority when both bindings coincide), and begins no action for an unbound button.
///
/// Seam: the Avalonia <c>OnPointerPressed</c> override delegates the "which action begins" decision to
/// the shared <c>HandlePointerPressed</c>, which sets <c>_activeDrag = ResolveDragAction(button)</c>.
/// <c>HandlePointerPressed</c> also calls <c>RenderContext.TryPick</c>, which needs a live GPU context,
/// so this property validates the real decision function <c>ResolveDragAction</c> directly (invoked on
/// a genuinely constructed control with real <c>RotateMouseButton</c>/<c>PanMouseButton</c> bindings).
///
/// **Validates: Requirements 7.1**
/// </summary>
[TestClass]
public sealed class DragBeginsOnBoundButtonTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    private static string ExpectedAction(
        ViewportMouseButton pressed,
        ViewportMouseButton rotate,
        ViewportMouseButton pan)
    {
        if (pressed == ViewportMouseButton.None)
        {
            return "None";
        }
        if (pressed == rotate)
        {
            return "Rotate"; // rotate wins ties
        }
        if (pressed == pan)
        {
            return "Pan";
        }
        return "None";
    }

    /// <summary>**Validates: Requirements 7.1**</summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    [TestCategory("Property 3")]
    public void Press_BeginsActionOnlyForBoundButton()
    {
        var gen =
            from rotate in Buttons.Gen
            from pan in Buttons.Gen
            from pressed in Buttons.Gen
            select (rotate, pan, pressed);

        Prop.ForAll(
                Arb.From(gen),
                ((ViewportMouseButton Rotate, ViewportMouseButton Pan, ViewportMouseButton Pressed) t) =>
                {
                    var camera = new RecordingCameraController();
                    var viewport = PointerInputTestSupport.CreateInputReadyViewport(camera);
                    viewport.RotateMouseButton = t.Rotate;
                    viewport.PanMouseButton = t.Pan;

                    string resolved = PointerInputTestSupport.ResolveDragAction(viewport, t.Pressed);
                    return resolved == ExpectedAction(t.Pressed, t.Rotate, t.Pan);
                })
            .Check(FsCheckConfig);
    }
}

/// <summary>
/// avalonia-interop Property 4: Drag ends only on the initiating button.
///
/// For any rotate/pan bindings, after a drag begins on a bound button, releasing that same button
/// ends the active drag, while releasing any other button leaves the active drag unchanged.
///
/// This drives the real shared <c>HandlePointerReleased</c> handler and observes the public
/// <c>ActiveDrag</c> state. The initiating drag state is established by setting <c>_activeDrag</c> to
/// the action the initiating button resolves to (the same value <c>HandlePointerPressed</c> would
/// assign), avoiding the GPU-bound <c>TryPick</c> call on press.
///
/// **Validates: Requirements 7.3**
/// </summary>
[TestClass]
public sealed class DragEndsOnInitiatingButtonTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>**Validates: Requirements 7.3**</summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    [TestCategory("Property 4")]
    public void Release_EndsDragOnlyForInitiatingButton()
    {
        // Generate bindings and an initiating button that is actually bound (so a drag is active),
        // plus an arbitrary release button.
        var gen =
            from rotate in Gen.Elements(ViewportMouseButton.Left, ViewportMouseButton.Middle, ViewportMouseButton.Right)
            from pan in Gen.Elements(ViewportMouseButton.Left, ViewportMouseButton.Middle, ViewportMouseButton.Right)
            from initiating in Gen.Elements(ViewportMouseButton.Left, ViewportMouseButton.Middle, ViewportMouseButton.Right)
            from release in Buttons.Gen
            where initiating == rotate || initiating == pan
            select (rotate, pan, initiating, release);

        Prop.ForAll(
                Arb.From(gen),
                ((ViewportMouseButton Rotate, ViewportMouseButton Pan, ViewportMouseButton Initiating, ViewportMouseButton Release) t) =>
                {
                    var camera = new RecordingCameraController();
                    var viewport = PointerInputTestSupport.CreateInputReadyViewport(camera);
                    viewport.RotateMouseButton = t.Rotate;
                    viewport.PanMouseButton = t.Pan;

                    // Establish the active drag exactly as a press on the initiating button would.
                    string initialAction = PointerInputTestSupport.ResolveDragAction(viewport, t.Initiating);
                    PointerInputTestSupport.SetActiveDrag(viewport, initialAction);

                    PointerInputTestSupport.HandlePointerReleased(viewport, t.Release);

                    // The release ends the drag iff it resolves to the same action that started it.
                    string releaseAction = PointerInputTestSupport.ResolveDragAction(viewport, t.Release);
                    bool shouldEnd = releaseAction == initialAction;

                    return viewport.ActiveDrag == !shouldEnd;
                })
            .Check(FsCheckConfig);
    }
}

/// <summary>
/// avalonia-interop Property 5: Move during an active drag forwards to the matching action.
///
/// For any active drag and any pointer position, a pointer move forwards that position to the rotate
/// delta when the active drag is rotate and to the pan delta when the active drag is pan, and forwards
/// nothing when there is no active drag.
///
/// This drives the real shared <c>HandlePointerMoved</c> handler with <c>CanHandleInput</c> satisfied
/// and observes a <see cref="RecordingCameraController"/> to confirm the forwarded call and position.
///
/// **Validates: Requirements 7.2**
/// </summary>
[TestClass]
public sealed class MoveForwardsToActiveActionTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>**Validates: Requirements 7.2**</summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    [TestCategory("Property 5")]
    public void Move_ForwardsPositionToActiveDragAction()
    {
        var gen =
            from drag in Gen.Elements("None", "Rotate", "Pan")
            from x in Gen.Choose(-5000, 5000)
            from y in Gen.Choose(-5000, 5000)
            select (drag, (float)x, (float)y);

        Prop.ForAll(
                Arb.From(gen),
                ((string Drag, float X, float Y) t) =>
                {
                    var camera = new RecordingCameraController();
                    var viewport = PointerInputTestSupport.CreateInputReadyViewport(camera);
                    PointerInputTestSupport.SetActiveDrag(viewport, t.Drag);

                    PointerInputTestSupport.HandlePointerMoved(viewport, t.X, t.Y);

                    return t.Drag switch
                    {
                        "Rotate" => camera.Calls.Count == 1
                            && camera.Calls[0] == new RecordingCameraController.Call("RotateDelta", t.X, t.Y),
                        "Pan" => camera.Calls.Count == 1
                            && camera.Calls[0] == new RecordingCameraController.Call("PanDelta", t.X, t.Y),
                        _ => camera.Calls.Count == 0,
                    };
                })
            .Check(FsCheckConfig);
    }
}

/// <summary>
/// avalonia-interop Property 6: Wheel delta forwards a normalized zoom.
///
/// For any wheel delta value, the zoom delta forwarded to the camera controller equals the
/// platform-normalized wheel delta. Avalonia reports wheel deltas in notches, so the value is
/// forwarded directly (no /120 scaling) — the forwarded zoom delta equals the input delta exactly.
///
/// This drives the real shared <c>HandleMouseWheel</c> handler with <c>CanHandleInput</c> satisfied
/// and observes the forwarded <c>OnZoomDelta</c> value.
///
/// **Validates: Requirements 7.4**
/// </summary>
[TestClass]
public sealed class WheelDeltaNormalizationTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>**Validates: Requirements 7.4**</summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    [TestCategory("Property 6")]
    public void Wheel_ForwardsDeltaDirectlyAsZoom()
    {
        // Avalonia notch-space deltas: small signed fractional/whole values.
        var gen =
            from thousandths in Gen.Choose(-20_000, 20_000)
            select thousandths / 1000f;

        Prop.ForAll(
                Arb.From(gen),
                (float delta) =>
                {
                    var camera = new RecordingCameraController();
                    var viewport = PointerInputTestSupport.CreateInputReadyViewport(camera);

                    PointerInputTestSupport.HandleMouseWheel(viewport, delta);

                    return camera.Calls.Count == 1
                        && camera.Calls[0] == new RecordingCameraController.Call("Zoom", delta, 0f);
                })
            .Check(FsCheckConfig);
    }
}

/// <summary>
/// avalonia-interop Property 7: Pointer exit resets state only without an active drag.
///
/// For any tracked pointer state, a pointer exit resets the tracked pointer location to the sentinel
/// (-1,-1) and clears input tracking when there is no active drag, and preserves the active drag state
/// when a drag is in progress.
///
/// Seam: the Avalonia <c>OnPointerExited</c> override cannot receive a headlessly-constructable
/// <c>PointerEventArgs</c>, so <see cref="PointerInputTestSupport.PointerExit"/> replays the override's
/// exact control flow over the real <c>ResetPointerLocation</c>/<c>HandlePointerExited</c> handlers and
/// the public <c>ActiveDrag</c> guard.
///
/// **Validates: Requirements 7.5**
/// </summary>
[TestClass]
public sealed class PointerExitResetsStateTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    private static readonly System.Numerics.Vector2 Sentinel = new(-1, -1);

    /// <summary>**Validates: Requirements 7.5**</summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    [TestCategory("Property 7")]
    public void Exit_ResetsLocationAndClearsDragOnlyWhenNotDragging()
    {
        var gen =
            from drag in Gen.Elements("None", "Rotate", "Pan")
            from x in Gen.Choose(-5000, 5000)
            from y in Gen.Choose(-5000, 5000)
            select (drag, (float)x, (float)y);

        Prop.ForAll(
                Arb.From(gen),
                ((string Drag, float X, float Y) t) =>
                {
                    var camera = new RecordingCameraController();
                    var viewport = PointerInputTestSupport.CreateInputReadyViewport(camera);
                    PointerInputTestSupport.SetActiveDrag(viewport, t.Drag);
                    PointerInputTestSupport.SetPointerLocation(viewport, new System.Numerics.Vector2(t.X, t.Y));

                    bool wasDragging = viewport.ActiveDrag;

                    PointerInputTestSupport.PointerExit(viewport);

                    // The tracked pointer location is always reset to the sentinel on exit.
                    bool locationReset = PointerInputTestSupport.GetPointerLocation(viewport) == Sentinel;

                    // Active drag is preserved mid-drag, and cleared (stays None) when not dragging.
                    bool dragCorrect = wasDragging ? viewport.ActiveDrag : !viewport.ActiveDrag;

                    return locationReset && dragCorrect;
                })
            .Check(FsCheckConfig);
    }
}
