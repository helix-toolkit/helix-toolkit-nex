using System.Numerics;
using HelixToolkit.Nex.Engine;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Interop;
using HelixToolkit.Nex.Rendering;
using Size = HelixToolkit.Nex.Maths.Size;
#if HxWPF
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace HelixToolkit.Nex.Wpf;

#elif HxWinUI
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HelixToolkit.Nex.WinUI;

#else
#error Unknown framework
#endif

public partial class HelixViewport
{
    private ViewportRenderingEventArgs? _renderArgs;
    private readonly PickingResult _pickResult = new();

    private Engine.Engine? _engine;
    private IViewportClient? _viewportClient;
    private ICameraController? _cameraController;
    private Vector2 _pointerLocation = new(-1, -1);

    /// <summary>Tracks which camera action (if any) is currently being driven by a mouse drag.</summary>
    private enum ActiveDragAction
    {
        None,
        Rotate,
        Pan,
    }

    /// <summary>Per-viewport render context (window size, camera, final output texture).</summary>
    private RenderContext? _renderContext;

    /// <summary>
    /// Gets the current rendering context associated with this instance.
    /// </summary>
    public RenderContext? RenderContext => _renderContext;
    private ActiveDragAction _activeDrag = ActiveDragAction.None;

    public bool ActiveDrag => _activeDrag != ActiveDragAction.None;

    /// <summary>
    /// Resolves which camera action a pressed button should trigger based on the
    /// current <see cref="RotateMouseButton"/> and <see cref="PanMouseButton"/> bindings.
    /// Returns <see cref="ActiveDragAction.None"/> if the button is not bound.
    /// </summary>
    private ActiveDragAction ResolveDragAction(ViewportMouseButton pressedButton)
    {
        if (pressedButton == ViewportMouseButton.None)
            return ActiveDragAction.None;

        // Rotate takes priority when both are bound to the same button.
        if (pressedButton == RotateMouseButton)
            return ActiveDragAction.Rotate;
        if (pressedButton == PanMouseButton)
            return ActiveDragAction.Pan;

        return ActiveDragAction.None;
    }

    /// <summary>
    /// Called by platform-specific code when a mouse button is pressed.
    /// </summary>
    private void HandlePointerPressed(ViewportMouseButton button, float x, float y)
    {
        if (_cameraController is null || _renderContext is null)
            return;
        var action = ResolveDragAction(button);
        if (action == ActiveDragAction.None)
            return;

        _activeDrag = action;
        var hitted = _renderContext.TryPick((int)x, (int)y, _pickResult);
        switch (action)
        {
            case ActiveDragAction.Rotate:
                _cameraController.OnRotateBegin(x, y, hitted ? _pickResult.WorldPosition : null);
                break;
            case ActiveDragAction.Pan:
                _cameraController.OnPanBegin(x, y, hitted ? _pickResult.WorldPosition : null);
                break;
        }
    }

    /// <summary>
    /// Called by platform-specific code when a mouse button is released.
    /// </summary>
    private void HandlePointerReleased(ViewportMouseButton button)
    {
        // Only end the drag if the released button matches the action that started it.
        var action = ResolveDragAction(button);
        if (action == _activeDrag)
        {
            _activeDrag = ActiveDragAction.None;
        }
    }

    /// <summary>
    /// Called by platform-specific code when the pointer moves.
    /// </summary>
    private void HandlePointerMoved(float x, float y)
    {
        _pointerLocation = new Vector2(x, y);
        if (_cameraController is null || _activeDrag == ActiveDragAction.None)
            return;

        switch (_activeDrag)
        {
            case ActiveDragAction.Rotate:
                _cameraController.OnRotateDelta(x, y);
                break;
            case ActiveDragAction.Pan:
                _cameraController.OnPanDelta(x, y);
                break;
        }
    }

    private void ResetPointerLocation()
    {
        _pointerLocation = new Vector2(-1, -1);
    }

    /// <summary>
    /// Called by platform-specific code when the mouse wheel scrolls.
    /// </summary>
    private void HandleMouseWheel(float delta)
    {
        if (_cameraController is null)
            return;

        _cameraController.OnZoomDelta(delta);
    }

    /// <summary>
    /// Called by platform-specific code when the pointer leaves the control.
    /// </summary>
    private void HandlePointerExited()
    {
        _activeDrag = ActiveDragAction.None;
    }

    private void UpdateViewportSize(float width, float height)
    {
        if (_cameraController is not null)
        {
            _cameraController.ViewportWidth = width;
            _cameraController.ViewportHeight = height;
        }
    }

    private bool Render(float width, float height, TextureHandle target)
    {
        // Pull per-frame data from the viewport client
        if (
            _viewportClient is null
            || Engine is null
            || _renderContext is null
            || _renderArgs is null
        )
            return false;

        var dataProvider = _viewportClient.DataProvider;

        if (dataProvider is null)
            return false;

        // Compute delta time
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        float delta =
            _lastTimestamp == 0
                ? 0f
                : (float)(now - _lastTimestamp) / System.Diagnostics.Stopwatch.Frequency;
        _lastTimestamp = now;
        _renderArgs.DeltaTime = delta;
        _renderContext.WindowSize = new Size((int)ActualWidth, (int)ActualHeight);
        EnsureSize();
        _cameraController?.Update(delta);

        var camera = _viewportClient.Update(_renderContext, delta);
        // Notify optional subscribers (read-only)
        BeforeRender?.Invoke(this, _renderArgs);

        _renderContext.Update(camera);
        _renderContext.SetPointer(_pointerLocation);

        var context = Engine.Context;

        // Render offscreen
        var cmdBuf = Engine.RenderOffscreen(_renderContext, dataProvider, target);
#if HxWPF
        var submitHandle = context.Submit(cmdBuf, TextureHandle.Null);
        context.Wait(submitHandle);
#elif HxWinUI
        _ = context.Submit(cmdBuf, TextureHandle.Null, _vulkanSyncInfo);
        // GPU-side sync via _vulkanSyncInfo — CPU wait not needed here
#else
#error Unknown framework
#endif
        return true;
    }
}
