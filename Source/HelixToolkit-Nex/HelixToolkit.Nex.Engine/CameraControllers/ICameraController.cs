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
    /// Gets or sets the viewport width in pixels.
    /// Must be set by the host before input events are processed so that
    /// panning and zooming can compute accurate world-space deltas.
    /// </summary>
    float ViewportWidth { get; set; }

    /// <summary>
    /// Gets or sets the viewport height in pixels.
    /// Must be set by the host before input events are processed so that
    /// panning and zooming can compute accurate world-space deltas.
    /// </summary>
    float ViewportHeight { get; set; }
    /// <summary>
    /// Called when a rotation gesture begins (e.g., left mouse button down).
    /// </summary>
    /// <param name="x">The starting X position in screen pixels.</param>
    /// <param name="y">The starting Y position in screen pixels.</param>
    /// <param name="pickPosition">Optional world-space position under the cursor, used as the rotation pivot.</param>
    void OnRotateBegin(float x, float y, Vector3? pickPosition = null);

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
    /// <param name="pickPosition">Optional world-space position under the cursor, used as the pan anchor.</param>
    void OnPanBegin(float x, float y, Vector3? pickPosition = null);

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
    /// <param name="pickPosition">Optional world-space position under the cursor, used as the zoom target.</param>
    void OnZoomDelta(float delta, Vector3? pickPosition = null);

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
