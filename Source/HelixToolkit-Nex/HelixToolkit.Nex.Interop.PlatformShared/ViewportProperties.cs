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
    #region Dependency Properties
    public static readonly DependencyProperty EngineDp = HelixProperty.Register<
        HelixViewport,
        Engine.Engine?
    >(
        "Engine",
        null,
        static (d, e) =>
        {
            if (d is not HelixViewport viewport)
            {
                return;
            }
            viewport.SetEngine((Engine.Engine?)e.NewValue);
        }
    );
    public Engine.Engine? Engine
    {
        get { return (Engine.Engine)GetValue(EngineDp); }
        set { SetValue(EngineDp, value); }
    }

    public static readonly DependencyProperty ViewportClientDp = HelixProperty.Register<
        HelixViewport,
        IViewportClient?
    >(
        "ViewportClient",
        null,
        static (d, e) =>
        {
            if (d is not HelixViewport viewport)
            {
                return;
            }
            viewport.SetClient((IViewportClient?)e.NewValue);
        }
    );

    /// <summary>
    /// Gets or sets the <see cref="IViewportClient"/> that provides per-frame camera
    /// updates and scene data for this viewport. When <c>null</c>, no frames are rendered.
    /// </summary>
    public IViewportClient? ViewportClient
    {
        get { return (IViewportClient?)GetValue(ViewportClientDp); }
        set { SetValue(ViewportClientDp, value); }
    }
    public static readonly DependencyProperty CameraControllerDp = HelixProperty.Register<
        HelixViewport,
        ICameraController?
    >(
        "CameraController",
        null,
        static (d, e) =>
        {
            if (d is not HelixViewport viewport)
            {
                return;
            }
            viewport.SetCameraController((ICameraController?)e.NewValue);
        }
    );

    /// <summary>
    /// Gets or sets the <see cref="ICameraController"/> that handles user input to manipulate the camera.
    /// </summary>
    public ICameraController? CameraController
    {
        get { return (ICameraController?)GetValue(CameraControllerDp); }
        set { SetValue(CameraControllerDp, value); }
    }

    public static readonly DependencyProperty RotateMouseButtonDp = HelixProperty.Register<
        HelixViewport,
        ViewportMouseButton
    >("RotateMouseButton", ViewportMouseButton.Left);

    /// <summary>
    /// Gets or sets the mouse button used to rotate the camera. Default is <see cref="ViewportMouseButton.Left"/>.
    /// </summary>
    public ViewportMouseButton RotateMouseButton
    {
        get { return (ViewportMouseButton)GetValue(RotateMouseButtonDp); }
        set { SetValue(RotateMouseButtonDp, value); }
    }

    public static readonly DependencyProperty PanMouseButtonDp = HelixProperty.Register<
        HelixViewport,
        ViewportMouseButton
    >("PanMouseButton", ViewportMouseButton.Middle);

    /// <summary>
    /// Gets or sets the mouse button used to pan the camera. Default is <see cref="ViewportMouseButton.Middle"/>.
    /// </summary>
    public ViewportMouseButton PanMouseButton
    {
        get { return (ViewportMouseButton)GetValue(PanMouseButtonDp); }
        set { SetValue(PanMouseButtonDp, value); }
    }

    public static readonly DependencyProperty PointerRingEnabledDp = HelixProperty.Register<
        HelixViewport,
        bool
    >("PointerRingEnabled", false, static (d, e) =>
    {
        (d as HelixViewport)?.RenderContext?.PointerRing.Enabled = (bool)e.NewValue ? 1u : 0;
    });

    /// <summary>
    /// Gets or sets a value indicating whether the pointer ring is enabled.
    /// </summary>
    public bool PointerRingEnabled
    {
        get { return (bool)GetValue(PointerRingEnabledDp); }
        set { SetValue(PointerRingEnabledDp, value); }
    }
    #endregion
}
