using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Input;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Engine.Cameras;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Rendering;

namespace HelixToolkit.Nex.Avalonia.Tests;

/// <summary>
/// Shared test doubles and a reflection-based accessor used by the pointer-input property tests
/// (avalonia-interop Properties 2–7).
///
/// <para><b>Seam rationale.</b> The pointer forwarding lives in two layers:</para>
/// <list type="bullet">
/// <item>
/// The Avalonia event overrides in <c>HelixViewport.Input.cs</c> (<c>OnPointerPressed</c> etc.) which
/// require Avalonia <c>PointerEventArgs</c>/<c>PointerReleasedEventArgs</c> instances. Those event
/// argument types cannot be constructed headlessly (their public constructors require live
/// <c>Pointer</c>/<c>IInputRoot</c> plumbing), so the tests exercise the production logic through the
/// most direct accessible seam instead.
/// </item>
/// <item>
/// The platform-neutral handlers in the shared <c>ViewportCommon.cs</c> (<c>ResolveDragAction</c>,
/// <c>HandlePointerReleased</c>, <c>HandlePointerMoved</c>, <c>HandleMouseWheel</c>,
/// <c>HandlePointerExited</c>, <c>ResetPointerLocation</c>). These are the real production methods the
/// overrides delegate to; the tests invoke them directly (private members reached via reflection,
/// enabled by <c>InternalsVisibleTo</c> for the public surface) so every property validates the
/// shipping decision logic rather than a re-implementation.
/// </item>
/// </list>
///
/// <para><b>CanHandleInput.</b> Several handlers early-out unless <c>CanHandleInput</c> holds, which
/// requires non-null <c>_engine</c>, <c>_renderContext</c>, <c>_viewportClient</c>, and
/// <c>_cameraController</c>. <c>_viewportClient</c>/<c>_cameraController</c> are strongly typed to
/// interfaces and are supplied as recording doubles through the real public dependency properties.
/// <c>_engine</c>/<c>_renderContext</c> are strongly typed to sealed/concrete engine classes that need
/// a live Vulkan device to construct, so they are set to uninitialized instances via reflection: the
/// exercised handlers (<c>HandlePointerMoved</c>, <c>HandleMouseWheel</c>) only null-check those
/// fields and never dereference them, so an uninitialized placeholder faithfully satisfies the guard
/// without a GPU. (<c>HandlePointerPressed</c> is the exception — it calls <c>RenderContext.TryPick</c>
/// — so Property 3 validates its decision function <c>ResolveDragAction</c> directly instead.)
/// </summary>
internal static class PointerInputTestSupport
{
    private static readonly Type ViewportType = typeof(HelixViewport);

    private static readonly Type ActiveDragActionType =
        ViewportType.GetNestedType("ActiveDragAction", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Could not locate the private ActiveDragAction enum.");

    // Keep placeholder engine/context instances rooted so they are never collected mid-test.
    private static readonly List<object> Rooted = [];

    private static FieldInfo Field(string name) =>
        ViewportType.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Could not locate private field '{name}'.");

    private static MethodInfo Method(string name) =>
        ViewportType.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Could not locate private method '{name}'.");

    /// <summary>Boxes an <see cref="ActiveDragAction"/>-equivalent value by name.</summary>
    internal static object DragValue(string name) => Enum.Parse(ActiveDragActionType, name);

    /// <summary>Reads the private <c>_activeDrag</c> field as its string name.</summary>
    internal static string GetActiveDragName(HelixViewport viewport) =>
        Field("_activeDrag").GetValue(viewport)!.ToString()!;

    /// <summary>Sets the private <c>_activeDrag</c> field by enum name.</summary>
    internal static void SetActiveDrag(HelixViewport viewport, string name) =>
        Field("_activeDrag").SetValue(viewport, DragValue(name));

    /// <summary>Reads the private <c>_pointerLocation</c> field.</summary>
    internal static Vector2 GetPointerLocation(HelixViewport viewport) =>
        (Vector2)Field("_pointerLocation").GetValue(viewport)!;

    /// <summary>Sets the private <c>_pointerLocation</c> field.</summary>
    internal static void SetPointerLocation(HelixViewport viewport, Vector2 value) =>
        Field("_pointerLocation").SetValue(viewport, value);

    /// <summary>Invokes the real private <c>ResolveDragAction</c> and returns the enum result name.</summary>
    internal static string ResolveDragAction(HelixViewport viewport, ViewportMouseButton pressed) =>
        Method("ResolveDragAction").Invoke(viewport, [pressed])!.ToString()!;

    /// <summary>Invokes the real private <c>HandlePointerReleased</c>.</summary>
    internal static void HandlePointerReleased(HelixViewport viewport, ViewportMouseButton button) =>
        Method("HandlePointerReleased").Invoke(viewport, [button]);

    /// <summary>Invokes the real private <c>HandlePointerMoved</c>.</summary>
    internal static void HandlePointerMoved(HelixViewport viewport, float x, float y) =>
        Method("HandlePointerMoved").Invoke(viewport, [x, y]);

    /// <summary>Invokes the real private <c>HandleMouseWheel</c>.</summary>
    internal static void HandleMouseWheel(HelixViewport viewport, float delta) =>
        Method("HandleMouseWheel").Invoke(viewport, [delta]);

    /// <summary>Invokes the real private <c>HandlePointerExited</c>.</summary>
    internal static void HandlePointerExited(HelixViewport viewport) =>
        Method("HandlePointerExited").Invoke(viewport, null);

    /// <summary>Invokes the real private <c>ResetPointerLocation</c>.</summary>
    internal static void ResetPointerLocation(HelixViewport viewport) =>
        Method("ResetPointerLocation").Invoke(viewport, null);

    /// <summary>Invokes the real private <c>UpdateViewportSize(float,float)</c>.</summary>
    internal static void UpdateViewportSize(HelixViewport viewport, float width, float height) =>
        Method("UpdateViewportSize").Invoke(viewport, [width, height]);

    /// <summary>Reads the real protected <c>IsContextValid</c> property.</summary>
    internal static bool GetIsContextValid(HelixViewport viewport) =>
        (bool)(ViewportType
                   .GetProperty("IsContextValid", BindingFlags.NonPublic | BindingFlags.Instance)
               ?? throw new InvalidOperationException("Could not locate protected property 'IsContextValid'."))
            .GetValue(viewport)!;

    /// <summary>
    /// Sets or clears the private concrete <c>_engine</c> field. A non-null value is an uninitialized
    /// placeholder (see the class remarks) that satisfies the null-check that <c>IsContextValid</c>
    /// performs without needing a live Vulkan device.
    /// </summary>
    internal static void SetEngineValid(HelixViewport viewport, bool valid) =>
        SetFieldValid(viewport, "_engine", valid);

    /// <summary>Sets or clears the private concrete <c>_renderContext</c> field (see <see cref="SetEngineValid"/>).</summary>
    internal static void SetRenderContextValid(HelixViewport viewport, bool valid) =>
        SetFieldValid(viewport, "_renderContext", valid);

    private static void SetFieldValid(HelixViewport viewport, string fieldName, bool valid)
    {
        if (valid)
        {
            SetPlaceholder(viewport, fieldName);
        }
        else
        {
            Field(fieldName).SetValue(viewport, null);
        }
    }

    /// <summary>Builds a bare <see cref="HelixViewport"/> with no engine/context/client configured.</summary>
    internal static HelixViewport CreateBareViewport() => new();

    /// <summary>
    /// Replays the exact control flow of the production <c>OnPointerExited</c> override
    /// (<c>HelixViewport.Input.cs</c>) without constructing an un-constructable Avalonia
    /// <c>PointerEventArgs</c>: it always resets the tracked pointer location, and only clears the
    /// active drag (via <c>HandlePointerExited</c>) when no drag is in progress.
    /// </summary>
    internal static void PointerExit(HelixViewport viewport)
    {
        ResetPointerLocation(viewport);
        if (viewport.ActiveDrag)
        {
            return;
        }
        HandlePointerExited(viewport);
    }

    /// <summary>
    /// Builds a <see cref="HelixViewport"/> whose <c>CanHandleInput</c> guard is satisfied: interface
    /// collaborators are injected through the real public dependency properties and the concrete
    /// engine/context fields are populated with uninitialized placeholders (see the class remarks).
    /// </summary>
    internal static HelixViewport CreateInputReadyViewport(RecordingCameraController camera)
    {
        var viewport = new HelixViewport
        {
            ViewportClient = new StubViewportClient(),
            CameraController = camera,
        };

        SetPlaceholder(viewport, "_engine");
        SetPlaceholder(viewport, "_renderContext");
        return viewport;
    }

    private static void SetPlaceholder(HelixViewport viewport, string fieldName)
    {
        FieldInfo field = Field(fieldName);
        object placeholder = RuntimeHelpers.GetUninitializedObject(field.FieldType);
        GC.SuppressFinalize(placeholder);
        Rooted.Add(placeholder);
        field.SetValue(viewport, placeholder);
    }

    /// <summary>Constructs a real <see cref="PointerPointProperties"/> with the given buttons pressed.</summary>
    internal static PointerPointProperties MakeProperties(bool left, bool middle, bool right)
    {
        var modifiers = RawInputModifiers.None;
        if (left)
        {
            modifiers |= RawInputModifiers.LeftMouseButton;
        }
        if (middle)
        {
            modifiers |= RawInputModifiers.MiddleMouseButton;
        }
        if (right)
        {
            modifiers |= RawInputModifiers.RightMouseButton;
        }

        // Kind=Other keeps the pressed-state derived purely from the modifier flags, so the mapping is
        // exercised over the full 2^3 button-state domain deterministically.
        return new PointerPointProperties(modifiers, PointerUpdateKind.Other);
    }
}

/// <summary>Records the camera-controller calls the pointer handlers forward.</summary>
internal sealed class RecordingCameraController : ICameraController
{
    internal readonly record struct Call(string Kind, float A, float B);

    internal List<Call> Calls { get; } = [];

    public Camera Camera => null!;
    public float ViewportWidth { get; set; }
    public float ViewportHeight { get; set; }

    public void OnRotateBegin(float x, float y, Vector3? pickPosition = null)
        => Calls.Add(new Call("RotateBegin", x, y));

    public void OnRotateDelta(float x, float y)
        => Calls.Add(new Call("RotateDelta", x, y));

    public void OnPanBegin(float x, float y, Vector3? pickPosition = null)
        => Calls.Add(new Call("PanBegin", x, y));

    public void OnPanDelta(float x, float y)
        => Calls.Add(new Call("PanDelta", x, y));

    public void OnZoomDelta(float delta, Vector3? pickPosition = null)
        => Calls.Add(new Call("Zoom", delta, 0f));

    public void Update(float deltaTime) { }

    public void Reset() { }

    public void FocusOn(Vector3 target, float? distance = null) { }
}

/// <summary>Minimal viewport client used only to satisfy the non-null <c>CanHandleInput</c> guard.</summary>
internal sealed class StubViewportClient : IViewportClient
{
    public IRenderDataProvider? DataProvider => null;

    public ICameraParamsProvider Update(RenderContext context, float deltaTime) => null!;
}
