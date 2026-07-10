using Avalonia.Input;
using HelixToolkit.Nex.Interop;

// Note: internal visibility to the test project (HelixToolkit.Nex.Avalonia.Tests) is granted via the
// <InternalsVisibleTo> item in HelixToolkit.Nex.Avalonia.csproj, which the SDK turns into the
// assembly-level attribute. Do not re-declare [assembly: InternalsVisibleTo] here (causes CS0579).

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Avalonia pointer-input portion of the <see cref="HelixViewport"/> partial class. It translates
/// Avalonia pointer events into the platform-neutral calls the shared <c>ViewportCommon.cs</c> logic
/// (compiled under the <c>HxAvalonia</c> symbol) exposes: <c>HandlePointerPressed</c>,
/// <c>HandlePointerReleased</c>, <c>HandlePointerMoved</c>, <c>HandleMouseWheel</c>,
/// <c>HandlePointerExited</c>, and <c>ResetPointerLocation</c>. This mirrors the WinUI host's pointer
/// forwarding while using Avalonia's <see cref="Avalonia.Input"/> event vocabulary.
/// </summary>
public partial class HelixViewport
{
    /// <summary>
    /// Maps an Avalonia <see cref="PointerPointProperties"/> button state to the corresponding
    /// <see cref="ViewportMouseButton"/>. This is a total function over the button domain: it yields
    /// <see cref="ViewportMouseButton.Left"/>/<see cref="ViewportMouseButton.Middle"/>/
    /// <see cref="ViewportMouseButton.Right"/> when the matching button is pressed and
    /// <see cref="ViewportMouseButton.None"/> when no button is pressed.
    /// </summary>
    /// <param name="p">The Avalonia pointer button state.</param>
    /// <returns>The mapped viewport mouse button.</returns>
    internal static ViewportMouseButton ToViewportButton(PointerPointProperties p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.IsLeftButtonPressed)
            return ViewportMouseButton.Left;
        if (p.IsMiddleButtonPressed)
            return ViewportMouseButton.Middle;
        if (p.IsRightButtonPressed)
            return ViewportMouseButton.Right;
        return ViewportMouseButton.None;
    }

    /// <summary>
    /// Determines which button ended a drag from a release event. On release the pressed-button state
    /// no longer reports the released button, so the button that initiated the press is read from
    /// <see cref="PointerReleasedEventArgs.InitialPressMouseButton"/> and mapped to the corresponding
    /// <see cref="ViewportMouseButton"/>.
    /// </summary>
    /// <param name="e">The Avalonia pointer-released event.</param>
    /// <returns>The mapped viewport mouse button that initiated the press.</returns>
    internal static ViewportMouseButton InferReleasedButton(PointerReleasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.InitialPressMouseButton switch
        {
            MouseButton.Left => ViewportMouseButton.Left,
            MouseButton.Middle => ViewportMouseButton.Middle,
            MouseButton.Right => ViewportMouseButton.Right,
            _ => ViewportMouseButton.None,
        };
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        var button = ToViewportButton(p.Properties);
        HandlePointerPressed(button, (float)p.Position.X, (float)p.Position.Y);
        if (ActiveDrag)
        {
            // Capture the pointer so drag deltas keep flowing even outside the control bounds.
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var button = InferReleasedButton(e);
        HandlePointerReleased(button);
        if (!ActiveDrag)
        {
            e.Pointer.Capture(null);
        }
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        HandlePointerMoved((float)p.Position.X, (float)p.Position.Y);
    }

    /// <inheritdoc/>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // Avalonia reports wheel deltas in notches (~1 per detent), so forward directly with no /120
        // scaling (unlike the WinUI host, which reports 120 units per notch).
        HandleMouseWheel((float)e.Delta.Y);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        ResetPointerLocation();
        if (ActiveDrag)
        {
            return;
        }
        HandlePointerExited();
    }
}
