using System;
using System.Collections.Generic;
using System.Text;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Interop;
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
    /// <summary>Tracks which camera action (if any) is currently being driven by a mouse drag.</summary>
    private enum ActiveDragAction
    {
        None,
        Rotate,
        Pan,
    }

    private ActiveDragAction _activeDrag = ActiveDragAction.None;

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
        if (_cameraController is null)
            return;

        var action = ResolveDragAction(button);
        if (action == ActiveDragAction.None)
            return;

        _activeDrag = action;

        switch (action)
        {
            case ActiveDragAction.Rotate:
                _cameraController.OnRotateBegin(x, y);
                break;
            case ActiveDragAction.Pan:
                _cameraController.OnPanBegin(x, y);
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
}
