using System.Reflection;
using HelixToolkit.Nex.Engine.CameraControllers;
using HelixToolkit.Nex.Interop;

namespace HelixToolkit.Nex.Avalonia.Tests;

/// <summary>
/// Feature: avalonia-interop, Task 2.4: adapter default values and member-surface parity.
///
/// These unit tests verify two things about the <see cref="HelixViewport"/> styled-property adapter:
/// <list type="number">
/// <item>
/// The registered defaults surfaced through the shared <c>ViewportProperties.cs</c> partial class match
/// the WinUI control: <c>RotateMouseButton</c> defaults to <see cref="ViewportMouseButton.Left"/> and
/// <c>PanMouseButton</c> defaults to <see cref="ViewportMouseButton.Middle"/>.
/// </item>
/// <item>
/// The control exposes the <c>Engine</c>, <c>ViewportClient</c>, <c>CameraController</c>,
/// <c>RotateMouseButton</c>, <c>PanMouseButton</c>, and <c>PointerRingEnabled</c> members with the same
/// names and value types as the WinUI control. The expected names/types are asserted directly (rather
/// than reflecting over the WinUI control) because the WinUI host targets a Windows-App-SDK TFM that the
/// cross-platform test project does not reference.
/// </item>
/// </list>
///
/// **Validates: Requirements 2.4, 2.5**
/// </summary>
[TestClass]
public sealed class AdapterDefaultsTests
{
    /// <summary>
    /// Requirement 2.5: a freshly constructed control reports the registered default bindings.
    /// </summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    public void RotateAndPanMouseButtons_HaveRegisteredDefaults()
    {
        var viewport = new HelixViewport();

        Assert.AreEqual(
            ViewportMouseButton.Left,
            viewport.RotateMouseButton,
            "RotateMouseButton should default to Left.");
        Assert.AreEqual(
            ViewportMouseButton.Middle,
            viewport.PanMouseButton,
            "PanMouseButton should default to Middle.");
    }

    /// <summary>
    /// Requirement 2.5: the defaults are readable through the generic dependency-property surface too,
    /// confirming the registered <see cref="ViewportMouseButton"/> defaults flow through the adapter's
    /// <see cref="HelixViewport.GetValue(DependencyProperty)"/> bridge.
    /// </summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    public void RotateAndPanMouseButtons_DefaultsReadableViaGetValue()
    {
        var viewport = new HelixViewport();

        Assert.AreEqual(
            ViewportMouseButton.Left,
            (ViewportMouseButton)viewport.GetValue(HelixViewport.RotateMouseButtonDp)!);
        Assert.AreEqual(
            ViewportMouseButton.Middle,
            (ViewportMouseButton)viewport.GetValue(HelixViewport.PanMouseButtonDp)!);
    }

    /// <summary>
    /// Requirement 2.4: the control exposes each mirrored member with the expected name and value type.
    /// The expected map mirrors the WinUI control's public surface declared in the shared
    /// <c>ViewportProperties.cs</c>.
    /// </summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    public void Control_ExposesMirroredMembers_WithMatchingNamesAndTypes()
    {
        var expected = new (string Name, Type Type)[]
        {
            ("Engine", typeof(HelixToolkit.Nex.Engine.Engine)),
            ("ViewportClient", typeof(IViewportClient)),
            ("CameraController", typeof(ICameraController)),
            ("RotateMouseButton", typeof(ViewportMouseButton)),
            ("PanMouseButton", typeof(ViewportMouseButton)),
            ("PointerRingEnabled", typeof(bool)),
        };

        foreach (var (name, type) in expected)
        {
            PropertyInfo? property = typeof(HelixViewport).GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(property, $"HelixViewport should expose a public '{name}' property.");
            Assert.AreEqual(
                type,
                property!.PropertyType,
                $"'{name}' should have value type {type.Name}.");
            Assert.IsTrue(property.CanRead, $"'{name}' should be readable.");
            Assert.IsTrue(property.CanWrite, $"'{name}' should be writable.");
        }
    }
}
