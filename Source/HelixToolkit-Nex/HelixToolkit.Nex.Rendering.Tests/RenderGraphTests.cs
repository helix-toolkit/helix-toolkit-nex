using HelixToolkit.Nex.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Unit tests for <see cref="RenderGraph.Compile"/> verifying that render passes
/// are topologically sorted in the correct dependency order.
/// </summary>
[TestClass]
public class RenderGraphTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a minimal <see cref="IServiceProvider"/> so the graph can be
    /// instantiated without a real DI container.
    /// </summary>
    private static IServiceProvider BuildServices() =>
        new ServiceCollection().BuildServiceProvider();

    /// <summary>
    /// Adds a pass to <paramref name="graph"/> and returns <paramref name="graph"/>
    /// for fluent chaining.
    /// </summary>
    private static RenderGraph AddPass(
        RenderGraph graph,
        string name,
        IList<RenderResource> inputs,
        IList<RenderResource> outputs
    ) => graph.AddPass(name, inputs, outputs, _ => { });

    /// <summary>
    /// Runs <see cref="RenderGraph.Compile"/> and returns the ordered list of
    /// pass names via the public <see cref="RenderGraph.SortedPasses"/> property.
    /// </summary>
    private static List<string> GetSortedPassNames(RenderGraph graph)
    {
        graph.Compile();
        return graph.SortedPasses.Select(n => n.PassName).ToList();
    }

    /// <summary>
    /// Returns true when <paramref name="before"/> appears earlier than
    /// <paramref name="after"/> in <paramref name="order"/>.
    /// </summary>
    private static bool Precedes(List<string> order, string before, string after) =>
        order.IndexOf(before) < order.IndexOf(after);

    // -----------------------------------------------------------------------
    // Empty / single-node cases
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_EmptyGraph_ProducesEmptySortedList()
    {
        var graph = new RenderGraph(BuildServices());

        graph.Compile();

        var names = GetSortedPassNames(graph);
        Assert.AreEqual(0, names.Count, "Empty graph should produce zero sorted passes.");
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_SinglePass_NoInputsNoOutputs_IsIncluded()
    {
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "OnlyPass", [], []);

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(1, names.Count);
        Assert.AreEqual("OnlyPass", names[0]);
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_SinglePass_WithOutputs_IsIncluded()
    {
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "Producer", [], [new RenderResource("TexA", ResourceType.Texture)]);

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(1, names.Count);
        Assert.AreEqual("Producer", names[0]);
    }

    // -----------------------------------------------------------------------
    // Two-pass linear dependency
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_TwoPasses_ProducerBeforeConsumer()
    {
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "Consumer", [new RenderResource("TexA", ResourceType.Texture)], []);
        AddPass(graph, "Producer", [], [new RenderResource("TexA", ResourceType.Texture)]);

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(2, names.Count);
        Assert.IsTrue(
            Precedes(names, "Producer", "Consumer"),
            $"Expected Producer before Consumer, got: {string.Join(" -> ", names)}"
        );
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_TwoPasses_AlreadyInOrder_RemainsCorrect()
    {
        var graph = new RenderGraph(BuildServices());
        // Add in natural order — sort must still be correct regardless.
        AddPass(graph, "Producer", [], [new RenderResource("TexA", ResourceType.Texture)]);
        AddPass(graph, "Consumer", [new RenderResource("TexA", ResourceType.Texture)], []);

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(2, names.Count);
        Assert.IsTrue(Precedes(names, "Producer", "Consumer"));
    }

    // -----------------------------------------------------------------------
    // Three-pass linear chain
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_ThreePassLinearChain_CorrectOrder()
    {
        // PassA -> PassB -> PassC
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "PassC", [new RenderResource("TexB", ResourceType.Texture)], []);
        AddPass(graph, "PassA", [], [new RenderResource("TexA", ResourceType.Texture)]);
        AddPass(
            graph,
            "PassB",
            [new RenderResource("TexA", ResourceType.Texture)],
            [new RenderResource("TexB", ResourceType.Texture)]
        );

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(3, names.Count);
        Assert.IsTrue(Precedes(names, "PassA", "PassB"), "PassA must precede PassB");
        Assert.IsTrue(Precedes(names, "PassB", "PassC"), "PassB must precede PassC");
    }

    // -----------------------------------------------------------------------
    // Diamond (fan-out then fan-in)
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_DiamondDependency_RootBeforeBranches_BranchesBeforeMerge()
    {
        //         Root
        //        /    \
        //    BranchA  BranchB
        //        \    /
        //         Merge
        var graph = new RenderGraph(BuildServices());
        AddPass(
            graph,
            "Root",
            [],
            [
                new RenderResource("TexA", ResourceType.Texture),
                new RenderResource("TexB", ResourceType.Texture),
            ]
        );
        AddPass(
            graph,
            "BranchA",
            [new RenderResource("TexA", ResourceType.Texture)],
            [new RenderResource("TexC", ResourceType.Texture)]
        );
        AddPass(
            graph,
            "BranchB",
            [new RenderResource("TexB", ResourceType.Texture)],
            [new RenderResource("TexD", ResourceType.Texture)]
        );
        AddPass(
            graph,
            "Merge",
            [
                new RenderResource("TexC", ResourceType.Texture),
                new RenderResource("TexD", ResourceType.Texture),
            ],
            []
        );

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(4, names.Count);
        Assert.IsTrue(Precedes(names, "Root", "BranchA"), "Root before BranchA");
        Assert.IsTrue(Precedes(names, "Root", "BranchB"), "Root before BranchB");
        Assert.IsTrue(Precedes(names, "BranchA", "Merge"), "BranchA before Merge");
        Assert.IsTrue(Precedes(names, "BranchB", "Merge"), "BranchB before Merge");
    }

    // -----------------------------------------------------------------------
    // Independent passes (no shared resources)
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_TwoIndependentPasses_BothPresent()
    {
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "PassX", [], [new RenderResource("BufX", ResourceType.Buffer)]);
        AddPass(graph, "PassY", [], [new RenderResource("BufY", ResourceType.Buffer)]);

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(2, names.Count);
        CollectionAssert.Contains(names, "PassX");
        CollectionAssert.Contains(names, "PassY");
    }

    // -----------------------------------------------------------------------
    // External / unregistered resource inputs (no internal producer)
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_PassWithExternalInput_TreatedAsRootPass()
    {
        // "SwapchainTex" has no producer pass — it is an external resource.
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "Presenter", [new RenderResource("SwapchainTex", ResourceType.Texture)], []);

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(1, names.Count);
        Assert.AreEqual("Presenter", names[0]);
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_MixedInternalAndExternalInputs_OrderCorrect()
    {
        // PrepPass outputs TexDepth, FinalPass needs TexDepth + external Swapchain.
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "PrepPass", [], [new RenderResource("TexDepth", ResourceType.Texture)]);
        AddPass(
            graph,
            "FinalPass",
            [
                new RenderResource("TexDepth", ResourceType.Texture),
                new RenderResource("Swapchain", ResourceType.Texture), // external
            ],
            []
        );

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(2, names.Count);
        Assert.IsTrue(Precedes(names, "PrepPass", "FinalPass"));
    }

    // -----------------------------------------------------------------------
    // Buffer resources (not just textures)
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_BufferDependency_ProducerBeforeConsumer()
    {
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "WriteBuffer", [], [new RenderResource("LightGrid", ResourceType.Buffer)]);
        AddPass(graph, "ReadBuffer", [new RenderResource("LightGrid", ResourceType.Buffer)], []);

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(2, names.Count);
        Assert.IsTrue(Precedes(names, "WriteBuffer", "ReadBuffer"));
    }

    // -----------------------------------------------------------------------
    // Cycle detection
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    [ExpectedException(typeof(InvalidOperationException))]
    public void Compile_DirectCycle_ThrowsInvalidOperationException()
    {
        // PassA -> PassB -> PassA (2-node cycle)
        var graph = new RenderGraph(BuildServices());
        AddPass(
            graph,
            "PassA",
            [new RenderResource("TexB", ResourceType.Texture)],
            [new RenderResource("TexA", ResourceType.Texture)]
        );
        AddPass(
            graph,
            "PassB",
            [new RenderResource("TexA", ResourceType.Texture)],
            [new RenderResource("TexB", ResourceType.Texture)]
        );

        graph.Compile(); // must throw
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_DirectCycle_ExceptionMessageContainsCyclicPassNames()
    {
        var graph = new RenderGraph(BuildServices());
        AddPass(
            graph,
            "CyclePassA",
            [new RenderResource("TexB", ResourceType.Texture)],
            [new RenderResource("TexA", ResourceType.Texture)]
        );
        AddPass(
            graph,
            "CyclePassB",
            [new RenderResource("TexA", ResourceType.Texture)],
            [new RenderResource("TexB", ResourceType.Texture)]
        );

        var ex = Assert.ThrowsException<InvalidOperationException>(() => graph.Compile());
        Assert.IsTrue(
            ex.Message.Contains("CyclePassA") || ex.Message.Contains("CyclePassB"),
            $"Expected cyclic pass names in exception message, got: {ex.Message}"
        );
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    [ExpectedException(typeof(InvalidOperationException))]
    public void Compile_ThreeNodeCycle_ThrowsInvalidOperationException()
    {
        // A -> B -> C -> A
        var graph = new RenderGraph(BuildServices());
        AddPass(
            graph,
            "A",
            [new RenderResource("TexC", ResourceType.Texture)],
            [new RenderResource("TexA", ResourceType.Texture)]
        );
        AddPass(
            graph,
            "B",
            [new RenderResource("TexA", ResourceType.Texture)],
            [new RenderResource("TexB", ResourceType.Texture)]
        );
        AddPass(
            graph,
            "C",
            [new RenderResource("TexB", ResourceType.Texture)],
            [new RenderResource("TexC", ResourceType.Texture)]
        );

        graph.Compile();
    }

    // -----------------------------------------------------------------------
    // IsDirty flag management
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void IsDirty_InitiallyTrue()
    {
        var graph = new RenderGraph(BuildServices());
        Assert.IsTrue(graph.IsDirty, "A newly created graph must be dirty.");
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void IsDirty_FalseAfterCompile()
    {
        var graph = new RenderGraph(BuildServices());
        graph.Compile();
        Assert.IsFalse(graph.IsDirty, "Graph should not be dirty after Compile().");
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void IsDirty_TrueAfterAddPass()
    {
        var graph = new RenderGraph(BuildServices());
        graph.Compile();

        AddPass(graph, "NewPass", [], []);

        Assert.IsTrue(graph.IsDirty, "Adding a pass must mark the graph dirty.");
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void IsDirty_TrueAfterRemovePass()
    {
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "Pass1", [], []);
        graph.Compile();

        graph.RemovePass("Pass1");

        Assert.IsTrue(graph.IsDirty, "Removing a pass must mark the graph dirty.");
    }

    // -----------------------------------------------------------------------
    // Duplicate pass / resource registration
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    [ExpectedException(typeof(InvalidOperationException))]
    public void AddPass_DuplicateName_ThrowsInvalidOperationException()
    {
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "DupPass", [], []);
        AddPass(graph, "DupPass", [], []); // must throw
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void AddTexture_DuplicateName_NoThrows()
    {
        var graph = new RenderGraph(BuildServices());
        graph.AddTexture("TexA", null);
        graph.AddTexture("TexA", null); // must not throw
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void AddBuffer_DuplicateName_NoThrows()
    {
        var graph = new RenderGraph(BuildServices());
        graph.AddBuffer("BufA", null);
        graph.AddBuffer("BufA", null); // must not throw
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    [ExpectedException(typeof(InvalidOperationException))]
    public void AddTexture_DuplicateName_ThrowsInvalidOperation()
    {
        var graph = new RenderGraph(BuildServices());
        graph.AddTexture(
            "TexA",
            (res) =>
            {
                return Graphics.TextureResource.Null;
            }
        );
        graph.AddTexture(
            "TexA",
            (res) =>
            {
                return Graphics.TextureResource.Null;
            }
        ); // must throw
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    [ExpectedException(typeof(InvalidOperationException))]
    public void AddBuffer_DuplicateName_ThrowsInvalidOperation()
    {
        var graph = new RenderGraph(BuildServices());
        graph.AddBuffer(
            "BufA",
            (res) =>
            {
                return Graphics.BufferResource.Null;
            }
        );
        graph.AddBuffer(
            "BufA",
            (res) =>
            {
                return Graphics.BufferResource.Null;
            }
        ); // must throw
    }

    // -----------------------------------------------------------------------
    // RemovePass and recompile
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void RemovePass_ThenCompile_RemovedPassAbsent()
    {
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "Keep", [], []);
        AddPass(graph, "Remove", [], []);

        graph.RemovePass("Remove");
        var names = GetSortedPassNames(graph);

        CollectionAssert.Contains(names, "Keep");
        CollectionAssert.DoesNotContain(names, "Remove");
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void RemovePass_BreaksDependencyChain_RemainingPassesStillSort()
    {
        // Before removal: PrepPass -> OpaquePass -> PostPass
        // Remove OpaquePass; PrepPass and PostPass are now independent roots.
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "PrepPass", [], [new RenderResource("TexA", ResourceType.Texture)]);
        AddPass(
            graph,
            "OpaquePass",
            [new RenderResource("TexA", ResourceType.Texture)],
            [new RenderResource("TexB", ResourceType.Texture)]
        );
        AddPass(graph, "PostPass", [new RenderResource("TexB", ResourceType.Texture)], []);

        graph.RemovePass("OpaquePass");
        var names = GetSortedPassNames(graph);

        Assert.AreEqual(2, names.Count);
        CollectionAssert.Contains(names, "PrepPass");
        CollectionAssert.Contains(names, "PostPass");
        CollectionAssert.DoesNotContain(names, "OpaquePass");
    }

    // -----------------------------------------------------------------------
    // Multiple recompilations
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_CalledMultipleTimes_ProducesConsistentOrder()
    {
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "Depth", [], [new RenderResource("TexDepth", ResourceType.Texture)]);
        AddPass(
            graph,
            "LightCull",
            [new RenderResource("TexDepth", ResourceType.Texture)],
            [new RenderResource("LightGrid", ResourceType.Buffer)]
        );
        AddPass(
            graph,
            "Opaque",
            [
                new RenderResource("TexDepth", ResourceType.Texture),
                new RenderResource("LightGrid", ResourceType.Buffer),
            ],
            []
        );

        var first = GetSortedPassNames(graph);

        // Force recompile by adding then removing a dummy pass
        AddPass(graph, "Dummy", [], []);
        graph.RemovePass("Dummy");

        var second = GetSortedPassNames(graph);

        CollectionAssert.AreEquivalent(first, second);
        Assert.IsTrue(Precedes(second, "Depth", "LightCull"));
        Assert.IsTrue(Precedes(second, "LightCull", "Opaque"));
        Assert.IsTrue(Precedes(second, "Depth", "Opaque"));
    }

    // -----------------------------------------------------------------------
    // Typical Forward+ pipeline topology
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_ForwardPlusPipeline_CorrectTopologicalOrder()
    {
        // Typical Forward+ pipeline:
        //   Prepare   -> produces FPConstants, TexDepth, TexMeshId, TexColor
        //   DepthPass -> consumes FPConstants; produces TexDepth, TexMeshId
        //   LightCull -> consumes TexDepth;    produces LightGrid, LightIndex
        //   Opaque    -> consumes TexDepth + FPConstants + LightGrid -> TexColor
        //   ToneMap   -> consumes TexColor -> FinalOutput

        var graph = new RenderGraph(BuildServices());
        AddPass(
            graph,
            "Prepare",
            [],
            [
                new RenderResource(SystemBufferNames.ForwardPlusConstants, ResourceType.Buffer),
                new RenderResource(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new RenderResource(SystemBufferNames.TextureMeshId, ResourceType.Texture),
                new RenderResource(SystemBufferNames.TextureColorF16, ResourceType.Texture),
            ]
        );
        AddPass(
            graph,
            "DepthPass",
            [
                new RenderResource(SystemBufferNames.ForwardPlusConstants, ResourceType.Buffer),
                new RenderResource(SystemBufferNames.BufferMeshDrawOpaque, ResourceType.Buffer),
            ],
            [
                new RenderResource(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new RenderResource(SystemBufferNames.TextureMeshId, ResourceType.Texture),
            ]
        );
        AddPass(
            graph,
            "LightCull",
            [new RenderResource(SystemBufferNames.TextureDepthF32, ResourceType.Texture)],
            [
                new RenderResource(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                new RenderResource(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
            ]
        );
        AddPass(
            graph,
            "Opaque",
            [
                new RenderResource(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new RenderResource(SystemBufferNames.ForwardPlusConstants, ResourceType.Buffer),
                new RenderResource(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
            ],
            [new RenderResource(SystemBufferNames.TextureColorF16, ResourceType.Texture)]
        );
        AddPass(
            graph,
            "ToneMap",
            [new RenderResource(SystemBufferNames.TextureColorF16, ResourceType.Texture)],
            [new RenderResource(SystemBufferNames.FinalOutputTexture, ResourceType.Texture)]
        );

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(5, names.Count, "All 5 passes must be present.");

        // Prepare must come before passes that consume its outputs
        Assert.IsTrue(Precedes(names, "Prepare", "DepthPass"), "Prepare -> DepthPass");
        Assert.IsTrue(Precedes(names, "Prepare", "Opaque"), "Prepare -> Opaque");

        // DepthPass before LightCull and Opaque
        Assert.IsTrue(Precedes(names, "DepthPass", "LightCull"), "DepthPass -> LightCull");
        Assert.IsTrue(Precedes(names, "DepthPass", "Opaque"), "DepthPass -> Opaque");

        // LightCull before Opaque
        Assert.IsTrue(Precedes(names, "LightCull", "Opaque"), "LightCull -> Opaque");

        // Opaque before ToneMap
        Assert.IsTrue(Precedes(names, "Opaque", "ToneMap"), "Opaque -> ToneMap");
    }

    // -----------------------------------------------------------------------
    // Duplicate input resources on a single pass (dedup edge test)
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_PassListsSameInputTwice_NoDuplicateEdge_OrderStillCorrect()
    {
        // A consumer lists the same resource name twice in its inputs.
        // The graph must not count the edge twice (which would corrupt in-degree).
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "Producer", [], [new RenderResource("TexShared", ResourceType.Texture)]);
        AddPass(
            graph,
            "Consumer",
            [
                new RenderResource("TexShared", ResourceType.Texture),
                new RenderResource("TexShared", ResourceType.Texture), // duplicate
            ],
            []
        );

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(2, names.Count);
        Assert.IsTrue(Precedes(names, "Producer", "Consumer"));
    }

    // -----------------------------------------------------------------------
    // Shared output resource (multiple producers for same resource)
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_MultipleProducersOfSameResource_ConsumerAfterBothProducers()
    {
        // ProducerA and ProducerB both write "TexShared".
        // Consumer reads "TexShared" — it must come after both producers.
        var graph = new RenderGraph(BuildServices());
        AddPass(graph, "ProducerA", [], [new RenderResource("TexShared", ResourceType.Texture)]);
        AddPass(graph, "ProducerB", [], [new RenderResource("TexShared", ResourceType.Texture)]);
        AddPass(graph, "Consumer", [new RenderResource("TexShared", ResourceType.Texture)], []);

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(3, names.Count);
        Assert.IsTrue(Precedes(names, "ProducerA", "Consumer"), "ProducerA before Consumer");
        Assert.IsTrue(Precedes(names, "ProducerB", "Consumer"), "ProducerB before Consumer");
    }

    // -----------------------------------------------------------------------
    // Self-loop (a pass that lists itself as both producer and consumer)
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    [ExpectedException(typeof(InvalidOperationException))]
    public void Compile_SelfLoop_ThrowsInvalidOperationException()
    {
        var graph = new RenderGraph(BuildServices());
        // PassA outputs TexA and also inputs TexA — a self-dependency.
        AddPass(
            graph,
            "PassA",
            [new RenderResource("TexA", ResourceType.Texture)],
            [new RenderResource("TexA", ResourceType.Texture)]
        );

        graph.Compile(); // must throw due to cycle
    }

    // -----------------------------------------------------------------------
    // Apply extension method
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Apply_AddsPassesViaConfigurator_CompileSucceeds()
    {
        var graph = new RenderGraph(BuildServices());

        graph.Apply(g =>
            AddPass(g, "ConfiguredPass", [], [new RenderResource("TexOut", ResourceType.Texture)])
        );

        var names = GetSortedPassNames(graph);
        CollectionAssert.Contains(names, "ConfiguredPass");
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Apply_MultipleFluent_AllPassesPresent()
    {
        var graph = new RenderGraph(BuildServices());

        graph
            .Apply(g =>
                AddPass(g, "Stage1", [], [new RenderResource("Tex1", ResourceType.Texture)])
            )
            .Apply(g =>
                AddPass(g, "Stage2", [new RenderResource("Tex1", ResourceType.Texture)], [])
            );

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(2, names.Count);
        Assert.IsTrue(Precedes(names, "Stage1", "Stage2"));
    }
}
