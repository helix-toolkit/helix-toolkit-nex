using System.Reflection;
using HelixToolkit.Nex.Rendering.Gizmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-factory-engine-integration — cross-layer placement smoke test (rendering side).
///
/// <para>
/// Enforces the one-way assembly reference direction between the rendering and engine layers:
/// </para>
/// <list type="bullet">
/// <item>
/// The <c>HelixToolkit.Nex.Rendering</c> assembly (which defines <see cref="GizmoManager"/>) declares
/// <b>no</b> assembly reference to <c>HelixToolkit.Nex.Engine</c>, so no rendering-layer type can
/// depend on an engine-layer type (Requirement 5.1).
/// </item>
/// <item>
/// The rendering-layer factory / cache / pick-resolution logic (<see cref="GizmoManager"/>,
/// <see cref="GizmoDefinition"/>, <see cref="GizmoInstanceHandle"/>) is invokable end to end from a
/// test project that does <b>not</b> reference the engine assembly — this very test project references
/// only rendering, so the fact these tests build and run demonstrates the constraint (Requirement 5.4).
/// </item>
/// </list>
///
/// <para>
/// The complementary assertions that the engine-hosting / pick-routing types resolve from the engine
/// assembly (Requirements 5.2, 5.3) live in <c>HelixToolkit.Nex.Engine.Tests</c>, because those
/// engine types are — by design — not visible from a rendering-only test project.
/// </para>
///
/// Validates: Requirements 5.1, 5.4.
/// </summary>
[TestClass]
public class CrossLayerPlacementSmokeTests
{
    private const string RenderingAssemblyName = "HelixToolkit.Nex.Rendering";
    private const string EngineAssemblyName = "HelixToolkit.Nex.Engine";

    /// <summary>
    /// Requirement 5.1: the rendering assembly must not reference the engine assembly.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void RenderingAssembly_DoesNotReference_EngineAssembly()
    {
        Assembly renderingAssembly = typeof(GizmoManager).Assembly;

        Assert.AreEqual(
            RenderingAssemblyName,
            renderingAssembly.GetName().Name,
            "GizmoManager must live in the rendering assembly."
        );

        AssemblyName[] referenced = renderingAssembly.GetReferencedAssemblies();

        bool referencesEngine = referenced.Any(a =>
            string.Equals(a.Name, EngineAssemblyName, StringComparison.Ordinal)
        );

        Assert.IsFalse(
            referencesEngine,
            $"The rendering assembly '{RenderingAssemblyName}' must not declare an assembly reference "
                + $"to the engine assembly '{EngineAssemblyName}' (Requirement 5.1). Referenced assemblies: "
                + string.Join(", ", referenced.Select(a => a.Name))
        );
    }

    /// <summary>
    /// Requirement 5.4: the rendering-layer factory / cache / resolve types are all defined in the
    /// rendering assembly, so a rendering-only consumer can use them without any engine reference.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void FactoryCacheResolveTypes_LiveInRenderingAssembly()
    {
        Assert.AreEqual(
            RenderingAssemblyName,
            typeof(GizmoManager).Assembly.GetName().Name,
            "GizmoManager (factory + cache + resolve) must live in the rendering assembly."
        );
        Assert.AreEqual(
            RenderingAssemblyName,
            typeof(GizmoDefinition).Assembly.GetName().Name,
            "GizmoDefinition must live in the rendering assembly."
        );
        Assert.AreEqual(
            RenderingAssemblyName,
            typeof(GizmoInstanceHandle).Assembly.GetName().Name,
            "GizmoInstanceHandle must live in the rendering assembly."
        );
    }

    /// <summary>
    /// Requirement 5.4: exercise the factory / cache / resolve path end to end from a test project
    /// that has no engine reference. Creating gizmos, hitting the cache, and resolving a pick all
    /// succeed using only rendering-layer types.
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void FactoryCacheResolve_RunWithoutEngineReference()
    {
        using var manager = new GizmoManager();

        var definition = new GizmoDefinition(
            GizmoMode.Translate,
            GizmoSpace.World,
            TargetEntityId: 42u,
            new GizmoHandleConfiguration(DesiredPixelSize: 64f, GizmoOcclusionMode.AlwaysOnTop)
        );

        // Factory: first request builds the handle set exactly once.
        Assert.IsTrue(
            manager.TryCreateGizmo(definition, out GizmoInstanceHandle first),
            "The factory must create a gizmo for a valid definition."
        );
        Assert.IsTrue(first.IsValid, "A created gizmo must yield a valid instance handle.");
        Assert.AreEqual(1, manager.HandleSetBuildCount, "The first create must build the handle set once.");

        // Cache: an equal definition reuses the cached handle set without a second build.
        Assert.IsTrue(
            manager.TryCreateGizmo(definition, out GizmoInstanceHandle second),
            "The factory must create a second gizmo for the same definition."
        );
        Assert.IsTrue(second.IsValid, "The second created gizmo must yield a valid instance handle.");
        Assert.AreEqual(
            1,
            manager.HandleSetBuildCount,
            "An equal definition must reuse the cached handle set without an additional build (Requirement 5.4)."
        );

        // Resolve: the pick-resolution surface runs (a non-gizmo / untracked decode resolves to
        // nothing) purely with rendering-layer types and never throws.
        bool resolvedNonGizmo = manager.TryResolvePick(
            isGizmo: false,
            owningEntityId: 42u,
            handle: new GizmoHandleId(GizmoMode.Translate, GizmoAxis.X),
            out GizmoPickResolution _
        );
        Assert.IsFalse(resolvedNonGizmo, "A non-gizmo decode must resolve to nothing.");

        bool resolvedById = manager.TryResolveHandle(entityId: 0u, out GizmoHandleId _);
        Assert.IsFalse(resolvedById, "An untracked entity id must resolve to no handle.");
    }
}
