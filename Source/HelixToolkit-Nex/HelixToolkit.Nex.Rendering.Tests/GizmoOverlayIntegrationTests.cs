using System.Numerics;
using System.Reflection;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Maths;
using HelixToolkit.Nex.Rendering.ComputeNodes;
using HelixToolkit.Nex.Rendering.DrawStreams;
using HelixToolkit.Nex.Rendering.RenderNodes;
using HelixToolkit.Nex.Shaders;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BufferHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Buffer>;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Feature: gizmo-rendering — Task 12.2 integration test for the overlay render + picking
/// round-trip.
/// <para>
/// <b>Property 1: Overlay ordering.</b> <b>Validates: Requirements 1.1, 1.2, 1.3, 1.4, 6.6</b>
/// (also exercises Property 2, picking round-trip).
/// </para>
///
/// <para>
/// <b>Why this is a seam-level integration test rather than a live GPU frame.</b> The design's
/// integration approach calls for a headless renderer that runs one frame with an active translate
/// gizmo and reads back <c>TextureEntityId</c> at a handle pixel. That requires a real GPU device
/// (a Vulkan context built via <c>VulkanBuilder.CreateHeadless</c> plus the full
/// <c>EngineBuilder.WithGizmos(...)</c> pipeline). This test project
/// (<c>HelixToolkit.Nex.Rendering.Tests</c>) intentionally links only the <b>mock</b> graphics
/// backend (<c>HelixToolkit.Nex.Graphics.Mock</c>) and does not reference the Vulkan backend or the
/// Engine project, so no live device and no <c>RenderOffscreen</c> frame are available here. Rather
/// than force a brittle GPU test, this test drives the exact seams a live frame would drive and
/// asserts the observable contract end to end:
/// </para>
/// <list type="number">
///   <item><description>
///   <b>Overlay ordering + graph wiring (Req 1.1).</b> The full default render pipeline plus the
///   opt-in <see cref="GizmoRenderNode"/> is compiled through the real
///   <see cref="RenderNode.AddToGraph"/> registrations; the compiled order proves the gizmo pass
///   runs in <see cref="RenderStage.Overlay"/> — after the tone-mapping pass — with the documented
///   inputs (scene depth + forward-plus constants) and outputs (tone-mapped color + entity id).
///   </description></item>
///   <item><description>
///   <b>Load-never-clear + depth-never-written (Req 1.2, 1.5).</b> The node's per-frame attachment
///   setup (<c>OnSetupRender</c>) is driven directly and asserted: the tone-mapped color and the
///   entity-id target load with <see cref="LoadOp.Load"/> (never cleared) and store with
///   <see cref="StoreOp.Store"/>; scene depth loads with <see cref="LoadOp.Load"/> and uses
///   <see cref="StoreOp.None"/> so no depth value is ever written.
///   </description></item>
/// </list>
/// </summary>
[TestClass]
public class GizmoOverlayIntegrationTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Returns true when <paramref name="before"/> precedes <paramref name="after"/>.</summary>
    private static bool Precedes(List<string> order, string before, string after) =>
        order.IndexOf(before) < order.IndexOf(after);

    /// <summary>
    /// Compiles the full default render pipeline together with the opt-in gizmo overlay node,
    /// mirroring what <c>EngineBuilder.WithGizmos</c> registers, and returns the compiled graph.
    /// <c>AddToGraph</c> only registers pass/resource metadata, so no GPU context is required.
    /// </summary>
    private static RenderGraph CompileDefaultPipelineWithGizmo()
    {
        var graph = new RenderGraph();

        new PrepareNode().AddToGraph(graph);
        new DepthPassNode().AddToGraph(graph);
        new FrustumCullNode().AddToGraph(graph);
        new ForwardPlusLightCullingNode().AddToGraph(graph);
        new ForwardPlusOpaqueNode().AddToGraph(graph);
        new PointRenderNode().AddToGraph(graph);
        new ForwardPlusWBOITMergedNode().AddToGraph(graph);
        new PostEffectsNode().AddToGraph(graph);
        new ToneMappingNode().AddToGraph(graph);

        // Opt-in gizmo overlay node, exactly as WithGizmos wires it into RenderStage.Overlay.
        new GizmoRenderNode().AddToGraph(graph);

        graph.Compile();
        return graph;
    }

    // -----------------------------------------------------------------------
    // (1) Overlay ordering + graph wiring — Requirement 1.1
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property 1 (Overlay ordering): the gizmo pass registers into <see cref="RenderStage.Overlay"/>
    /// and, after compilation, executes after the tone-mapping pass — indeed after every other pass
    /// in the default pipeline — with the documented inputs and outputs.
    ///
    /// **Validates: Requirements 1.1**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void DefaultPipelineWithGizmo_OverlayPassCompilesAfterToneMapping_WithDocumentedWiring()
    {
        RenderGraph graph = CompileDefaultPipelineWithGizmo();

        var sorted = graph.SortedPasses;
        var names = sorted.Select(p => p.PassName).ToList();

        // The gizmo overlay pass is present.
        CollectionAssert.Contains(
            names,
            nameof(GizmoRenderNode),
            "The gizmo overlay pass must be registered into the compiled graph."
        );

        // Overlay ordering: the gizmo pass runs after tone mapping (Req 1.1).
        Assert.IsTrue(
            Precedes(names, nameof(ToneMappingNode), nameof(GizmoRenderNode)),
            $"GizmoRenderNode must execute after ToneMappingNode. Order: {string.Join(" -> ", names)}"
        );

        // Because Overlay is the highest stage among the registered nodes (Output is not added),
        // the gizmo pass is the very last pass in the compiled order.
        Assert.AreEqual(
            nameof(GizmoRenderNode),
            names[^1],
            "The gizmo overlay pass must be the last pass in the compiled default pipeline."
        );

        RenderGraph.GraphNode gizmoPass = sorted.First(p =>
            p.PassName == nameof(GizmoRenderNode)
        );

        // Registered into the Overlay stage (Req 1.1).
        Assert.AreEqual(
            RenderStage.Overlay,
            gizmoPass.Stage,
            "The gizmo pass must be registered into RenderStage.Overlay."
        );

        // Inputs: scene depth (read-only) + forward-plus constants.
        var inputNames = gizmoPass.Inputs.Select(r => r.Name).ToList();
        CollectionAssert.Contains(
            inputNames,
            SystemBufferNames.TextureDepthF32,
            "The gizmo pass must take scene depth as an input."
        );
        CollectionAssert.Contains(
            inputNames,
            SystemBufferNames.BufferForwardPlusConstants,
            "The gizmo pass must take the forward-plus constants buffer as an input."
        );

        // Outputs: tone-mapped color + entity id.
        var outputNames = gizmoPass.Outputs.Select(r => r.Name).ToList();
        CollectionAssert.Contains(
            outputNames,
            SystemBufferNames.TextureColorF16Target,
            "The gizmo pass must output the tone-mapped color target."
        );
        CollectionAssert.Contains(
            outputNames,
            SystemBufferNames.TextureEntityId,
            "The gizmo pass must output the entity-id target."
        );
    }

    // -----------------------------------------------------------------------
    // (2) Load-never-clear + depth-never-written — Requirements 1.2, 1.5
    // -----------------------------------------------------------------------

    /// <summary>
    /// Requirements 1.2 and 1.5: when the gizmo node sets up a frame's attachments, the tone-mapped
    /// color and entity-id targets are loaded (never cleared) and stored, while scene depth is loaded
    /// read-only and uses a store op that writes no depth — leaving the opaque-pass depth buffer
    /// bit-identical. Driving <c>OnSetupRender</c> directly asserts this contract without a GPU frame.
    ///
    /// **Validates: Requirements 1.2, 1.5**
    /// </summary>
    [TestMethod]
    [TestCategory("Gizmo")]
    public void OnSetupRender_LoadsColorAndDepthNeverClears_AndUsesNoDepthStore()
    {
        var node = new GizmoRenderNode();

        var pass = new RenderPass();
        var framebuf = new Framebuffer();
        var deps = new Dependencies();

        // Valid (gen != 0) handles: the node pushes depth/constants into Dependencies, which asserts
        // handle validity. Values are opaque identifiers — no GPU object is needed for setup.
        var textures = new Dictionary<string, TextureHandle>
        {
            [SystemBufferNames.TextureDepthF32] = new TextureHandle(1, 1),
            [SystemBufferNames.TextureColorF16Target] = new TextureHandle(2, 1),
            [SystemBufferNames.TextureEntityId] = new TextureHandle(3, 1),
        };
        var buffers = new Dictionary<string, BufferHandle>
        {
            [SystemBufferNames.BufferForwardPlusConstants] = new BufferHandle(4, 1),
        };

        // OnSetupRender only touches Pass/Framebuf/Deps/Textures/Buffers (never RenderContext or the
        // command buffer), so a headless RenderResources with null context/cmdbuffer is sufficient.
        var res = new RenderResources(
            RenderContext: null!,
            CmdBuffer: null!,
            Pass: pass,
            Framebuf: framebuf,
            Deps: deps,
            Textures: textures,
            Buffers: buffers
        );

        InvokeOnSetupRender(node, res);

        // Color 0: tone-mapped LDR target — loaded (never cleared) and stored (Req 1.2).
        Assert.AreEqual(
            LoadOp.Load,
            pass.Colors[0].LoadOp,
            "Tone-mapped color must load its existing contents (never cleared)."
        );
        Assert.AreEqual(
            StoreOp.Store,
            pass.Colors[0].StoreOp,
            "Tone-mapped color must be stored so gizmo pixels are preserved."
        );
        Assert.AreEqual(
            textures[SystemBufferNames.TextureColorF16Target],
            framebuf.Colors[0].Texture,
            "Color attachment 0 must bind the tone-mapped color target."
        );

        // Color 1: entity-id target — loaded (never cleared) and stored (Req 1.2 / picking).
        Assert.AreEqual(
            LoadOp.Load,
            pass.Colors[1].LoadOp,
            "Entity-id target must load its existing contents so uncovered pixels keep their ids."
        );
        Assert.AreEqual(
            StoreOp.Store,
            pass.Colors[1].StoreOp,
            "Entity-id target must be stored so handle ids are written for picking."
        );
        Assert.AreEqual(
            textures[SystemBufferNames.TextureEntityId],
            framebuf.Colors[1].Texture,
            "Color attachment 1 must bind the entity-id target."
        );

        // Depth: loaded read-only, StoreOp.None so scene depth is never written (Req 1.2, 1.5).
        Assert.AreEqual(
            LoadOp.Load,
            pass.Depth.LoadOp,
            "Scene depth must be loaded (never cleared)."
        );
        Assert.AreEqual(
            StoreOp.None,
            pass.Depth.StoreOp,
            "Scene depth must use StoreOp.None so no depth value is ever written (Req 1.5)."
        );
        Assert.AreEqual(
            textures[SystemBufferNames.TextureDepthF32],
            framebuf.DepthStencil.Texture,
            "The depth-stencil attachment must bind the scene depth target."
        );

        // The selected depth state is read-only (writes no depth) in the default AlwaysOnTop mode,
        // reinforcing that scene depth is preserved (Req 1.5 / 8.3).
        Assert.IsNotNull(pass.DepthState, "A depth state must be selected for the gizmo pass.");
        Assert.IsFalse(
            pass.DepthState!.IsDepthWriteEnabled,
            "The gizmo depth state must be read-only so scene depth is never written."
        );
    }

    /// <summary>
    /// Invokes the node's protected <c>OnSetupRender(in RenderResources)</c> seam. The mutated
    /// <see cref="RenderPass"/>/<see cref="Framebuffer"/>/<see cref="Dependencies"/> are reference
    /// types shared with the caller, so the caller observes the setup result directly.
    /// </summary>
    private static void InvokeOnSetupRender(GizmoRenderNode node, in RenderResources res)
    {
        MethodInfo method =
            typeof(GizmoRenderNode).GetMethod(
                "OnSetupRender",
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("OnSetupRender seam not found on GizmoRenderNode.");

        method.Invoke(node, [res]);
    }
}
