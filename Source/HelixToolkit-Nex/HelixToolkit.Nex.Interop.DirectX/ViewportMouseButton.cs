namespace HelixToolkit.Nex.Interop;

/// <summary>
/// Identifies a mouse button for viewport camera interaction bindings.
/// Used to configure which button triggers rotate, pan, or zoom actions.
/// </summary>
public enum ViewportMouseButton
{
    /// <summary>No mouse button assigned; the action is disabled.</summary>
    None = 0,

    /// <summary>The left mouse button.</summary>
    Left,

    /// <summary>The middle mouse button (wheel click).</summary>
    Middle,

    /// <summary>The right mouse button.</summary>
    Right,
}
