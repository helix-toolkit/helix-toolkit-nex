using System.Reflection;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration — cross-layer placement smoke test (engine side).
///
/// <para>
/// Complements <c>HelixToolkit.Nex.Rendering.Tests.CrossLayerPlacementSmokeTests</c> (which asserts
/// the rendering assembly does not reference the engine assembly — Requirements 5.1, 5.4). This
/// engine-side test asserts the reverse-direction placement:
/// </para>
/// <list type="bullet">
/// <item>
/// The engine-hosting type (<see cref="Engine"/>, which exposes the hosted gizmo service) resolves
/// from the <c>HelixToolkit.Nex.Engine</c> assembly (Requirement 5.2).
/// </item>
/// <item>
/// The cross-layer pick-routing type (<see cref="GizmoPickRouter"/>) resolves from the
/// <c>HelixToolkit.Nex.Engine</c> assembly (Requirement 5.3).
/// </item>
/// <item>
/// The hosted service itself (<see cref="GizmoManager"/>) is still a rendering-layer type, and the
/// engine assembly references the rendering assembly (the permitted one-way direction).
/// </item>
/// </list>
///
/// Validates: Requirements 5.2, 5.3.
/// </summary>
[TestClass]
public class CrossLayerPlacementSmokeTests
{
    private const string RenderingAssemblyName = "HelixToolkit.Nex.Rendering";
    private const string EngineAssemblyName = "HelixToolkit.Nex.Engine";

    /// <summary>
    /// Requirement 5.2: the engine-hosting type lives in the engine assembly.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void EngineHostingType_LivesInEngineAssembly()
    {
        Assert.AreEqual(
            EngineAssemblyName,
            typeof(global::HelixToolkit.Nex.Engine.Engine).Assembly.GetName().Name,
            "The engine-hosting type must live in the engine assembly (Requirement 5.2)."
        );
    }

    /// <summary>
    /// Requirement 5.3: the cross-layer gizmo pick-routing type lives in the engine assembly.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void GizmoPickRouter_LivesInEngineAssembly()
    {
        Type routerType = typeof(GizmoPickRouter);

        Assert.AreEqual(
            EngineAssemblyName,
            routerType.Assembly.GetName().Name,
            "GizmoPickRouter must live in the engine assembly (Requirement 5.3)."
        );

        // Also resolvable by fully-qualified name from the loaded engine assembly.
        Type? loaded = routerType.Assembly.GetType("HelixToolkit.Nex.Engine.GizmoPickRouter");
        Assert.IsNotNull(loaded, "GizmoPickRouter must be resolvable from the engine assembly by name.");
    }

    /// <summary>
    /// The hosted service type remains a rendering-layer type, and the engine assembly declares the
    /// permitted one-way reference to the rendering assembly (Requirements 5.2, 5.3).
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void HostedService_IsRenderingType_AndEngineReferencesRendering()
    {
        Assert.AreEqual(
            RenderingAssemblyName,
            typeof(GizmoManager).Assembly.GetName().Name,
            "The hosted gizmo service (GizmoManager) must remain a rendering-layer type."
        );

        AssemblyName[] engineReferences =
            typeof(global::HelixToolkit.Nex.Engine.Engine).Assembly.GetReferencedAssemblies();

        bool referencesRendering = engineReferences.Any(a =>
            string.Equals(a.Name, RenderingAssemblyName, StringComparison.Ordinal)
        );

        Assert.IsTrue(
            referencesRendering,
            $"The engine assembly '{EngineAssemblyName}' must reference the rendering assembly "
                + $"'{RenderingAssemblyName}' (the permitted one-way direction)."
        );
    }
}
