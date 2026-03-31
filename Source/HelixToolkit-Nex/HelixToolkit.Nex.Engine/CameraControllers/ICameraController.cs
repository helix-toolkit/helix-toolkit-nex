using HelixToolkit.Nex.Engine.Cameras;

namespace HelixToolkit.Nex.Engine.CameraControllers;

/// <summary>
/// Defines the interface for a camera controller that manipulates a <see cref="Camera"/>
/// based on user input such as mouse and keyboard events.
/// </summary>
public interface ICameraController
{
    /// <summary>
    /// Gets the camera being controlled.
    /// </summary>
    Camera Camera { get; }

    /// <summary>
    /// Called when a rotation gesture begins (e.g., left mouse button down).
    /// </summary>
    /// <param name="x">The starting X position in screen pixels.</param>
    /// <param name="y">The starting Y position in screen pixels.</param>
    void OnRotateBegin(float x, float y);

    /// <summary>
    /// Called during a rotation gesture (e.g., left mouse button drag).
    /// </summary>
    /// <param name="x">The current X position in screen pixels.</param>
    /// <param name="y">The current Y position in screen pixels.</param>
    void OnRotateDelta(float x, float y);

    /// <summary>
    /// Called when a pan gesture begins (e.g., middle mouse button down).
    /// </summary>
    /// <param name="x">The starting X position in screen pixels.</param>
    /// <param name="y">The starting Y position in screen pixels.</param>
    void OnPanBegin(float x, float y);

    /// <summary>
    /// Called during a pan gesture (e.g., middle mouse button drag).
    /// </summary>
    /// <param name="x">The current X position in screen pixels.</param>
    /// <param name="y">The current Y position in screen pixels.</param>
    void OnPanDelta(float x, float y);

    /// <summary>
    /// Called when a zoom gesture occurs (e.g., mouse scroll wheel).
    /// </summary>
    /// <param name="delta">The zoom delta. Positive values zoom in, negative values zoom out.</param>
    void OnZoomDelta(float delta);

    /// <summary>
    /// Updates the controller state. Should be called once per frame.
    /// </summary>
    /// <param name="deltaTime">The elapsed time in seconds since the last update.</param>
    void Update(float deltaTime);

    /// <summary>
    /// Resets the controller to its default state.
    /// </summary>
    void Reset();
}
