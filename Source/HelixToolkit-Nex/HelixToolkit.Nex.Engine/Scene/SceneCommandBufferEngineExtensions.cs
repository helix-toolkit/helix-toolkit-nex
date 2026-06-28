using HelixToolkit.Nex.Engine.Components;
using HelixToolkit.Nex.Engine.Scene;
using HelixToolkit.Nex.Lights;
using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Scene;

/// <summary>
/// Engine-layer convenience methods for recording deferred creation of the Engine's
/// custom <see cref="Node"/> subtypes (<see cref="MeshNode"/>, <see cref="LineNode"/>,
/// the light nodes, <see cref="BillboardNode"/>, <see cref="PointCloudNode"/>) into a
/// <see cref="SceneCommandBuffer"/>.
/// <para>
/// These mirror the immediate <c>World</c> extension methods in
/// <see cref="Extensions"/> (<c>CreateMeshNode</c>, <c>CreateLineNode</c>, …) but defer
/// construction: each method records a factory that runs the real subtype constructor on
/// the owning thread during <see cref="SceneCommandBuffer.Flush(World)"/> instead of
/// building the node immediately.
/// </para>
/// <para>
/// Every method is implemented purely through the public
/// <see cref="SceneCommandBuffer.RecordCreateNode{T}(System.Func{World, T})"/> API, so the
/// Scene layer needs no Engine-specific entry point and never references a concrete custom
/// node type. Component values are taken <b>by value</b> so the captured closure holds an
/// independent copy; a later mutation of the caller's variable cannot affect the flushed
/// result.
/// </para>
/// </summary>
public static class SceneCommandBufferEngineExtensions
{
    /// <summary>
    /// Records deferred creation of a <see cref="MeshNode"/> with the given name.
    /// </summary>
    public static TypedDeferredNode<MeshNode> RecordCreateMeshNode(
        this SceneCommandBuffer scb,
        string name
    ) => scb.RecordCreateNode(world => new MeshNode(world, name));

    /// <summary>
    /// Records deferred creation of a <see cref="MeshNode"/> with the given name and
    /// mesh draw info. The <paramref name="component"/> is captured by value.
    /// </summary>
    public static TypedDeferredNode<MeshNode> RecordCreateMeshNode(
        this SceneCommandBuffer scb,
        string name,
        MeshDrawInfo component
    ) => scb.RecordCreateNode(world => new MeshNode(world, name, component));

    /// <summary>
    /// Records deferred creation of a <see cref="LineNode"/> with the given name.
    /// </summary>
    public static TypedDeferredNode<LineNode> RecordCreateLineNode(
        this SceneCommandBuffer scb,
        string name
    ) => scb.RecordCreateNode(world => new LineNode(world, name));

    /// <summary>
    /// Records deferred creation of a <see cref="LineNode"/> with the given name and
    /// line draw info. The <paramref name="component"/> is captured by value.
    /// </summary>
    public static TypedDeferredNode<LineNode> RecordCreateLineNode(
        this SceneCommandBuffer scb,
        string name,
        LineDrawInfo component
    ) => scb.RecordCreateNode(world => new LineNode(world, name, component));

    /// <summary>
    /// Records deferred creation of a <see cref="DirectionalLightNode"/> with the given name.
    /// </summary>
    public static TypedDeferredNode<DirectionalLightNode> RecordCreateDirectionalLight(
        this SceneCommandBuffer scb,
        string name = "DirectionalLight"
    ) => scb.RecordCreateNode(world => new DirectionalLightNode(world, name));

    /// <summary>
    /// Records deferred creation of a <see cref="DirectionalLightNode"/> with the given
    /// name and light info. The <paramref name="component"/> is captured by value.
    /// </summary>
    public static TypedDeferredNode<DirectionalLightNode> RecordCreateDirectionalLight(
        this SceneCommandBuffer scb,
        string name,
        DirectionalLightInfo component
    ) => scb.RecordCreateNode(world => new DirectionalLightNode(world, name, component));

    /// <summary>
    /// Records deferred creation of a <see cref="PointLightNode"/> with the given name.
    /// </summary>
    public static TypedDeferredNode<PointLightNode> RecordCreatePointLight(
        this SceneCommandBuffer scb,
        string name = "PointLight"
    ) => scb.RecordCreateNode(world => new PointLightNode(world, name));

    /// <summary>
    /// Records deferred creation of a <see cref="SpotLightNode"/> with the given name.
    /// </summary>
    public static TypedDeferredNode<SpotLightNode> RecordCreateSpotLight(
        this SceneCommandBuffer scb,
        string name = "SpotLight"
    ) => scb.RecordCreateNode(world => new SpotLightNode(world, name));

    /// <summary>
    /// Records deferred creation of a <see cref="BillboardNode"/> with the given name.
    /// </summary>
    public static TypedDeferredNode<BillboardNode> RecordCreateBillboardNode(
        this SceneCommandBuffer scb,
        string name
    ) => scb.RecordCreateNode(world => new BillboardNode(world, name));

    /// <summary>
    /// Records deferred creation of a <see cref="BillboardNode"/> with the given name and
    /// billboard draw info. The <paramref name="component"/> is captured by value.
    /// </summary>
    public static TypedDeferredNode<BillboardNode> RecordCreateBillboardNode(
        this SceneCommandBuffer scb,
        string name,
        BillboardDrawInfo component
    ) => scb.RecordCreateNode(world => new BillboardNode(world, name, component));

    /// <summary>
    /// Records deferred creation of a <see cref="PointCloudNode"/> with the given name.
    /// </summary>
    public static TypedDeferredNode<PointCloudNode> RecordCreatePointCloudNode(
        this SceneCommandBuffer scb,
        string name
    ) => scb.RecordCreateNode(world => new PointCloudNode(world, name));

    /// <summary>
    /// Records deferred creation of a <see cref="PointCloudNode"/> with the given name and
    /// point draw info. The <paramref name="component"/> is captured by value.
    /// </summary>
    public static TypedDeferredNode<PointCloudNode> RecordCreatePointCloudNode(
        this SceneCommandBuffer scb,
        string name,
        PointDrawInfo component
    ) => scb.RecordCreateNode(world => new PointCloudNode(world, name, component));
}
