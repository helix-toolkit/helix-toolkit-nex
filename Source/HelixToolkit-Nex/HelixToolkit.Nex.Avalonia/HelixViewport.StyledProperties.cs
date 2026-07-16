using Avalonia;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Interop;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Exposes the shared partial class's registered dependency properties as conventionally-named
/// Avalonia <see cref="StyledProperty{TValue}"/> fields (<c>&lt;Name&gt;Property</c>).
/// </summary>
/// <remarks>
/// <para>
/// The shared <c>ViewportProperties.cs</c> registers each bindable property through
/// <see cref="HelixProperty.Register{TOwner, TValue}"/> and stores the resulting
/// <see cref="DependencyProperty"/> handle in a <c>&lt;Name&gt;Dp</c> field. That handle wraps a real
/// Avalonia <see cref="StyledProperty{TValue}"/>, but Avalonia's compiled-XAML binding compiler
/// discovers bindable properties by looking for a <c>public static</c> field named
/// <c>&lt;Name&gt;Property</c> whose type derives from <see cref="AvaloniaProperty"/>. These aliases
/// provide exactly that so consumers can bind <c>Engine</c>, <c>ViewportClient</c>,
/// <c>CameraController</c>, <c>RotateMouseButton</c>, <c>PanMouseButton</c>, and
/// <c>PointerRingEnabled</c> from XAML (Requirements 2.1, 9.4).
/// </para>
/// <para>
/// The aliases are assigned in the static constructor rather than via field initializers so their
/// initialization is order-independent: C# guarantees all static field initializers (including the
/// shared <c>&lt;Name&gt;Dp</c> fields declared in the other partial) run before the static
/// constructor body executes, so the wrapped <see cref="StyledProperty{TValue}"/> instances are
/// already available here.
/// </para>
/// </remarks>
public partial class HelixViewport
{
    /// <summary>Bindable alias for the shared <c>EngineDp</c> registration.</summary>
    public static readonly StyledProperty<Engine.Engine?> EngineProperty;

    /// <summary>Bindable alias for the shared <c>ViewportClientDp</c> registration.</summary>
    public static readonly StyledProperty<IViewportClient?> ViewportClientProperty;

    /// <summary>Bindable alias for the shared <c>CameraControllerDp</c> registration.</summary>
    public static readonly StyledProperty<ICameraController?> CameraControllerProperty;

    /// <summary>Bindable alias for the shared <c>RotateMouseButtonDp</c> registration.</summary>
    public static readonly StyledProperty<ViewportMouseButton> RotateMouseButtonProperty;

    /// <summary>Bindable alias for the shared <c>PanMouseButtonDp</c> registration.</summary>
    public static readonly StyledProperty<ViewportMouseButton> PanMouseButtonProperty;

    /// <summary>Bindable alias for the shared <c>PointerRingEnabledDp</c> registration.</summary>
    public static readonly StyledProperty<bool> PointerRingEnabledProperty;

    static HelixViewport()
    {
        EngineProperty = (StyledProperty<Engine.Engine?>)EngineDp.Avalonia;
        ViewportClientProperty = (StyledProperty<IViewportClient?>)ViewportClientDp.Avalonia;
        CameraControllerProperty = (StyledProperty<ICameraController?>)CameraControllerDp.Avalonia;
        RotateMouseButtonProperty = (StyledProperty<ViewportMouseButton>)RotateMouseButtonDp.Avalonia;
        PanMouseButtonProperty = (StyledProperty<ViewportMouseButton>)PanMouseButtonDp.Avalonia;
        PointerRingEnabledProperty = (StyledProperty<bool>)PointerRingEnabledDp.Avalonia;
    }
}
