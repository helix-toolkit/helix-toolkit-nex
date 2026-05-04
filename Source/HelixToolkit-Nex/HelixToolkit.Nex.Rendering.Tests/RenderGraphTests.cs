using HelixToolkit.Nex.Rendering.ComputeNodes;
using HelixToolkit.Nex.Rendering.RenderNodes;
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
        RenderStage stage,
        string name,
        IList<RenderResource> inputs,
        IList<RenderResource> outputs
    ) => graph.AddPass(stage, name, inputs, outputs);

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
        var graph = new RenderGraph();

        graph.Compile();

        var names = GetSortedPassNames(graph);
        Assert.AreEqual(0, names.Count, "Empty graph should produce zero sorted passes.");
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_SinglePass_NoInputsNoOutputs_IsIncluded()
    {
        var graph = new RenderGraph();
        AddPass(graph, RenderStage.Opaque, "OnlyPass", [], []);

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(1, names.Count);
        Assert.AreEqual("OnlyPass", names[0]);
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_SinglePass_WithOutputs_IsIncluded()
    {
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "Producer",
            [],
            [new RenderResource("TexA", ResourceType.Texture)]
        );

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
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "Consumer",
            [new RenderResource("TexA", ResourceType.Texture)],
            []
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "Producer",
            [],
            [new RenderResource("TexA", ResourceType.Texture)]
        );

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
        var graph = new RenderGraph();
        // Add in natural order — sort must still be correct regardless.
        AddPass(
            graph,
            RenderStage.Opaque,
            "Producer",
            [],
            [new RenderResource("TexA", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "Consumer",
            [new RenderResource("TexA", ResourceType.Texture)],
            []
        );

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
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "PassC",
            [new RenderResource("TexB", ResourceType.Texture)],
            []
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "PassA",
            [],
            [new RenderResource("TexA", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
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
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "Root",
            [],
            [
                new RenderResource("TexA", ResourceType.Texture),
                new RenderResource("TexB", ResourceType.Texture),
            ]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "BranchA",
            [new RenderResource("TexA", ResourceType.Texture)],
            [new RenderResource("TexC", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "BranchB",
            [new RenderResource("TexB", ResourceType.Texture)],
            [new RenderResource("TexD", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
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
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "PassX",
            [],
            [new RenderResource("BufX", ResourceType.Buffer)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "PassY",
            [],
            [new RenderResource("BufY", ResourceType.Buffer)]
        );

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
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "Presenter",
            [new RenderResource("SwapchainTex", ResourceType.Texture)],
            []
        );

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(1, names.Count);
        Assert.AreEqual("Presenter", names[0]);
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_MixedInternalAndExternalInputs_OrderCorrect()
    {
        // PrepPass outputs TexDepth, FinalPass needs TexDepth + external Swapchain.
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "PrepPass",
            [],
            [new RenderResource("TexDepth", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
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
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "WriteBuffer",
            [],
            [new RenderResource("LightGrid", ResourceType.Buffer)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "ReadBuffer",
            [new RenderResource("LightGrid", ResourceType.Buffer)],
            []
        );

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
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "PassA",
            [new RenderResource("TexB", ResourceType.Texture)],
            [new RenderResource("TexA", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
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
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "CyclePassA",
            [new RenderResource("TexB", ResourceType.Texture)],
            [new RenderResource("TexA", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
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
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "A",
            [new RenderResource("TexC", ResourceType.Texture)],
            [new RenderResource("TexA", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "B",
            [new RenderResource("TexA", ResourceType.Texture)],
            [new RenderResource("TexB", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
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
        var graph = new RenderGraph();
        Assert.IsTrue(graph.IsDirty, "A newly created graph must be dirty.");
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void IsDirty_FalseAfterCompile()
    {
        var graph = new RenderGraph();
        graph.Compile();
        Assert.IsFalse(graph.IsDirty, "Graph should not be dirty after Compile().");
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void IsDirty_TrueAfterAddPass()
    {
        var graph = new RenderGraph();
        graph.Compile();

        AddPass(graph, RenderStage.Opaque, "NewPass", [], []);

        Assert.IsTrue(graph.IsDirty, "Adding a pass must mark the graph dirty.");
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void IsDirty_TrueAfterRemovePass()
    {
        var graph = new RenderGraph();
        AddPass(graph, RenderStage.Opaque, "Pass1", [], []);
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
        var graph = new RenderGraph();
        AddPass(graph, RenderStage.Opaque, "DupPass", [], []);
        AddPass(graph, RenderStage.Opaque, "DupPass", [], []); // must throw
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void AddTexture_DuplicateName_NoThrows()
    {
        var graph = new RenderGraph();
        graph.AddTexture("TexA", null);
        graph.AddTexture("TexA", null); // must not throw
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void AddBuffer_DuplicateName_NoThrows()
    {
        var graph = new RenderGraph();
        graph.AddBuffer("BufA", null);
        graph.AddBuffer("BufA", null); // must not throw
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    [ExpectedException(typeof(InvalidOperationException))]
    public void AddTexture_DuplicateName_ThrowsInvalidOperation()
    {
        var graph = new RenderGraph();
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
        var graph = new RenderGraph();
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
        var graph = new RenderGraph();
        AddPass(graph, RenderStage.Opaque, "Keep", [], []);
        AddPass(graph, RenderStage.Opaque, "Remove", [], []);

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
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "PrepPass",
            [],
            [new RenderResource("TexA", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "OpaquePass",
            [new RenderResource("TexA", ResourceType.Texture)],
            [new RenderResource("TexB", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "PostPass",
            [new RenderResource("TexB", ResourceType.Texture)],
            []
        );

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
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Prepare,
            "Depth",
            [],
            [new RenderResource("TexDepth", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Prepare,
            "LightCull",
            [new RenderResource("TexDepth", ResourceType.Texture)],
            [new RenderResource("LightGrid", ResourceType.Buffer)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "Opaque",
            [
                new RenderResource("TexDepth", ResourceType.Texture),
                new RenderResource("LightGrid", ResourceType.Buffer),
            ],
            []
        );

        var first = GetSortedPassNames(graph);

        // Force recompile by adding then removing a dummy pass
        AddPass(graph, RenderStage.Opaque, "Dummy", [], []);
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

        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Prepare,
            "Prepare",
            [],
            [
                new RenderResource(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new RenderResource(SystemBufferNames.TextureEntityId, ResourceType.Texture),
                new RenderResource(SystemBufferNames.TextureColorF16A, ResourceType.Texture),
            ]
        );
        AddPass(
            graph,
            RenderStage.Prepare,
            "DepthPass",
            [new RenderResource(SystemBufferNames.BufferMeshDrawOpaque, ResourceType.Buffer)],
            [
                new RenderResource(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new RenderResource(SystemBufferNames.TextureEntityId, ResourceType.Texture),
            ]
        );
        AddPass(
            graph,
            RenderStage.Prepare,
            "LightCull",
            [new RenderResource(SystemBufferNames.TextureDepthF32, ResourceType.Texture)],
            [
                new RenderResource(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
                new RenderResource(SystemBufferNames.BufferLightIndex, ResourceType.Buffer),
            ]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "Opaque",
            [
                new RenderResource(SystemBufferNames.TextureDepthF32, ResourceType.Texture),
                new RenderResource(SystemBufferNames.BufferLightGrid, ResourceType.Buffer),
            ],
            [new RenderResource(SystemBufferNames.TextureColorF16A, ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.ToneMap,
            "ToneMap",
            [new RenderResource(SystemBufferNames.TextureColorF16A, ResourceType.Texture)],
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
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "Producer",
            [],
            [new RenderResource("TexShared", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
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
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "ProducerA",
            [],
            [new RenderResource("TexShared", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "ProducerB",
            [],
            [new RenderResource("TexShared", ResourceType.Texture)]
        );
        AddPass(
            graph,
            RenderStage.Opaque,
            "Consumer",
            [new RenderResource("TexShared", ResourceType.Texture)],
            []
        );

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
        var graph = new RenderGraph();
        // PassA outputs TexA and also inputs TexA — a self-dependency.
        AddPass(
            graph,
            RenderStage.Opaque,
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
        var graph = new RenderGraph();

        graph.Apply(g =>
            AddPass(
                g,
                RenderStage.Opaque,
                "ConfiguredPass",
                [],
                [new RenderResource("TexOut", ResourceType.Texture)]
            )
        );

        var names = GetSortedPassNames(graph);
        CollectionAssert.Contains(names, "ConfiguredPass");
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Apply_MultipleFluent_AllPassesPresent()
    {
        var graph = new RenderGraph();

        graph
            .Apply(g =>
                AddPass(
                    g,
                    RenderStage.Opaque,
                    "Stage1",
                    [],
                    [new RenderResource("Tex1", ResourceType.Texture)]
                )
            )
            .Apply(g =>
                AddPass(
                    g,
                    RenderStage.Opaque,
                    "Stage2",
                    [new RenderResource("Tex1", ResourceType.Texture)],
                    []
                )
            );

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(2, names.Count);
        Assert.IsTrue(Precedes(names, "Stage1", "Stage2"));
    }

    // -----------------------------------------------------------------------
    // Explicit ordering via 'after' parameter
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_ExplicitAfter_EnforcesOrderWithNoSharedResource()
    {
        // PassB has no resource dependency on PassA, but declares after: ["PassA"].
        var graph = new RenderGraph();
        AddPass(graph, RenderStage.Opaque, "PassA", [], []);
        graph.AddPass(RenderStage.Opaque, "PassB", [], [], after: ["PassA"]);

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(2, names.Count);
        Assert.IsTrue(Precedes(names, "PassA", "PassB"), "PassA must precede PassB via 'after'");
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_ExplicitAfter_MultiplePassesWithSamePingPongResource_CorrectOrder()
    {
        // Three passes all read TextureColorF16Target and write TextureColorF16Sample
        // (or vice versa). Without 'after', the sort order between them is undefined.
        // With 'after', the declared chain A -> B -> C is enforced.
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "Producer",
            [],
            [new RenderResource("TexColor", ResourceType.Texture)]
        );
        graph.AddPass(
            RenderStage.PostProcess,
            "EffectA",
            [new RenderResource("TexColor", ResourceType.Texture)],
            [new RenderResource("TexPing", ResourceType.Texture)]
        );
        graph.AddPass(
            RenderStage.PostProcess,
            "EffectB",
            [new RenderResource("TexColor", ResourceType.Texture)],
            [new RenderResource("TexPing", ResourceType.Texture)],
            after: ["EffectA"]
        );
        graph.AddPass(
            RenderStage.PostProcess,
            "EffectC",
            [new RenderResource("TexColor", ResourceType.Texture)],
            [new RenderResource("TexPing", ResourceType.Texture)],
            after: ["EffectB"]
        );

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(4, names.Count);
        Assert.IsTrue(Precedes(names, "Producer", "EffectA"), "Producer -> EffectA");
        Assert.IsTrue(Precedes(names, "EffectA", "EffectB"), "EffectA -> EffectB via 'after'");
        Assert.IsTrue(Precedes(names, "EffectB", "EffectC"), "EffectB -> EffectC via 'after'");
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_ExplicitAfter_RedundantWithResourceEdge_NoDuplicateInDegree()
    {
        // PassB already depends on PassA via a resource edge AND declares after: ["PassA"].
        // The dedup set must prevent double-counting the in-degree.
        var graph = new RenderGraph();
        AddPass(
            graph,
            RenderStage.Opaque,
            "PassA",
            [],
            [new RenderResource("TexA", ResourceType.Texture)]
        );
        graph.AddPass(
            RenderStage.Opaque,
            "PassB",
            [new RenderResource("TexA", ResourceType.Texture)],
            [],
            after: ["PassA"]
        );

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(2, names.Count);
        Assert.IsTrue(Precedes(names, "PassA", "PassB"));
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_ExplicitAfter_UnknownPassName_DoesNotThrow_GraphStillCompiles()
    {
        // Referencing a non-existent pass in 'after' should log a warning and be ignored.
        var graph = new RenderGraph();
        graph.AddPass(RenderStage.Opaque, "PassA", [], [], after: ["NonExistentPass"]);

        var names = GetSortedPassNames(graph);

        Assert.AreEqual(1, names.Count);
        Assert.AreEqual("PassA", names[0]);
    }

    [TestMethod]
    [TestCategory("RenderGraph")]
    [ExpectedException(typeof(InvalidOperationException))]
    public void Compile_ExplicitAfter_CreatesCycle_ThrowsInvalidOperationException()
    {
        // PassA after PassB, PassB after PassA — explicit cycle.
        var graph = new RenderGraph();
        graph.AddPass(RenderStage.Opaque, "PassA", [], [], after: ["PassB"]);
        graph.AddPass(RenderStage.Opaque, "PassB", [], [], after: ["PassA"]);

        graph.Compile(); // must throw
    }

    // -----------------------------------------------------------------------
    // Full default pipeline: real node AddToGraph registrations
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies that the full default render pipeline — using the real
    /// <see cref="RenderNode.AddToGraph"/> implementations — compiles without
    /// a circular dependency and produces the correct stage-based ordering.
    /// <para>
    /// No GPU context is required because <c>AddToGraph</c> only registers
    /// resource metadata and passes; it never allocates GPU objects.
    /// </para>
    /// </summary>
    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_DefaultPipeline_CorrectStageOrder()
    {
        var graph = new RenderGraph();

        new PrepareNode().AddToGraph(graph);
        new DepthPassNode().AddToGraph(graph);
        new FrustumCullNode().AddToGraph(graph);
        new PointCullNode().AddToGraph(graph);
        new ForwardPlusLightCullingNode().AddToGraph(graph);
        new ForwardPlusOpaqueNode().AddToGraph(graph);
        new PointRenderNode().AddToGraph(graph);
        new ForwardPlusTransparentNode().AddToGraph(graph);
        new WBOITCompositeNode().AddToGraph(graph);
        new PostEffectsNode().AddToGraph(graph);
        new ToneMappingNode().AddToGraph(graph);

        // Must not throw — no circular dependency.
        graph.Compile();

        var names = graph.SortedPasses.Select(p => p.PassName).ToList();

        Assert.AreEqual(
            11,
            names.Count,
            $"Expected 11 passes, got {names.Count}: {string.Join(", ", names)}"
        );

        // --- Stage: Prepare ---
        Assert.IsTrue(
            Precedes(names, nameof(PrepareNode), nameof(DepthPassNode)),
            "PrepareNode before DepthPassNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(FrustumCullNode), nameof(ForwardPlusOpaqueNode)),
            "FrustumCullNode before ForwardPlusOpaqueNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(PointCullNode), nameof(PointRenderNode)),
            "PointCullNode before PointRenderNode"
        );

        // --- Stage: Opaque ---
        Assert.IsTrue(
            Precedes(names, nameof(DepthPassNode), nameof(ForwardPlusLightCullingNode)),
            "DepthPassNode before ForwardPlusLightCullingNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(DepthPassNode), nameof(ForwardPlusOpaqueNode)),
            "DepthPassNode before ForwardPlusOpaqueNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(ForwardPlusLightCullingNode), nameof(ForwardPlusOpaqueNode)),
            "ForwardPlusLightCullingNode before ForwardPlusOpaqueNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(ForwardPlusOpaqueNode), nameof(PointRenderNode)),
            "ForwardPlusOpaqueNode before PointRenderNode"
        );

        // --- Stage: Transparent ---
        Assert.IsTrue(
            Precedes(names, nameof(PointRenderNode), nameof(ForwardPlusTransparentNode)),
            "PointRenderNode before ForwardPlusTransparentNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(ForwardPlusOpaqueNode), nameof(ForwardPlusTransparentNode)),
            "ForwardPlusOpaqueNode before ForwardPlusTransparentNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(ForwardPlusTransparentNode), nameof(WBOITCompositeNode)),
            "ForwardPlusTransparentNode before WBOITCompositeNode"
        );

        // --- Stage: PostProcess → ToneMap ---
        Assert.IsTrue(
            Precedes(names, nameof(WBOITCompositeNode), nameof(PostEffectsNode)),
            "WBOITCompositeNode before PostEffectsNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(PostEffectsNode), nameof(ToneMappingNode)),
            "PostEffectsNode before ToneMappingNode"
        );
    }

    /// <summary>
    /// Helper that asserts all canonical stage-ordering invariants for the
    /// default pipeline, regardless of the node insertion order.
    /// </summary>
    private static void AssertDefaultPipelineOrder(List<string> names)
    {
        Assert.AreEqual(
            11,
            names.Count,
            $"Expected 11 passes, got {names.Count}: {string.Join(", ", names)}"
        );

        // Prepare → Opaque
        Assert.IsTrue(
            Precedes(names, nameof(PrepareNode), nameof(DepthPassNode)),
            "PrepareNode before DepthPassNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(FrustumCullNode), nameof(ForwardPlusOpaqueNode)),
            "FrustumCullNode before ForwardPlusOpaqueNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(PointCullNode), nameof(PointRenderNode)),
            "PointCullNode before PointRenderNode"
        );

        // Opaque ordering
        Assert.IsTrue(
            Precedes(names, nameof(DepthPassNode), nameof(ForwardPlusLightCullingNode)),
            "DepthPassNode before ForwardPlusLightCullingNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(DepthPassNode), nameof(ForwardPlusOpaqueNode)),
            "DepthPassNode before ForwardPlusOpaqueNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(ForwardPlusLightCullingNode), nameof(ForwardPlusOpaqueNode)),
            "ForwardPlusLightCullingNode before ForwardPlusOpaqueNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(ForwardPlusOpaqueNode), nameof(PointRenderNode)),
            "ForwardPlusOpaqueNode before PointRenderNode"
        );

        // Opaque → Transparent
        Assert.IsTrue(
            Precedes(names, nameof(PointRenderNode), nameof(ForwardPlusTransparentNode)),
            "PointRenderNode before ForwardPlusTransparentNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(ForwardPlusOpaqueNode), nameof(ForwardPlusTransparentNode)),
            "ForwardPlusOpaqueNode before ForwardPlusTransparentNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(ForwardPlusTransparentNode), nameof(WBOITCompositeNode)),
            "ForwardPlusTransparentNode before WBOITCompositeNode"
        );

        // Transparent → PostProcess → ToneMap
        Assert.IsTrue(
            Precedes(names, nameof(WBOITCompositeNode), nameof(PostEffectsNode)),
            "WBOITCompositeNode before PostEffectsNode"
        );
        Assert.IsTrue(
            Precedes(names, nameof(PostEffectsNode), nameof(ToneMappingNode)),
            "PostEffectsNode before ToneMappingNode"
        );
    }

    private static List<string> CompileNodes(IEnumerable<RenderNode> nodes)
    {
        var graph = new RenderGraph();
        foreach (var node in nodes)
            node.AddToGraph(graph);
        graph.Compile();
        return graph.SortedPasses.Select(p => p.PassName).ToList();
    }

    // -----------------------------------------------------------------------
    // Default pipeline — reversed insertion order
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_DefaultPipeline_ReversedInsertionOrder_CorrectStageOrder()
    {
        // Nodes added in exactly the reverse of the canonical order.
        var names = CompileNodes([
            new ToneMappingNode(),
            new PostEffectsNode(),
            new WBOITCompositeNode(),
            new ForwardPlusTransparentNode(),
            new PointRenderNode(),
            new ForwardPlusOpaqueNode(),
            new ForwardPlusLightCullingNode(),
            new PointCullNode(),
            new FrustumCullNode(),
            new DepthPassNode(),
            new PrepareNode(),
        ]);

        AssertDefaultPipelineOrder(names);
    }

    // -----------------------------------------------------------------------
    // Default pipeline — transparent nodes before opaque nodes
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_DefaultPipeline_TransparentBeforeOpaqueInsertion_CorrectStageOrder()
    {
        // Transparent-stage nodes are added before opaque-stage nodes.
        var names = CompileNodes([
            new ForwardPlusTransparentNode(),
            new WBOITCompositeNode(),
            new PrepareNode(),
            new DepthPassNode(),
            new FrustumCullNode(),
            new PointCullNode(),
            new ForwardPlusLightCullingNode(),
            new ForwardPlusOpaqueNode(),
            new PointRenderNode(),
            new PostEffectsNode(),
            new ToneMappingNode(),
        ]);

        AssertDefaultPipelineOrder(names);
    }

    // -----------------------------------------------------------------------
    // Default pipeline — post-process nodes first
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_DefaultPipeline_PostProcessNodesFirst_CorrectStageOrder()
    {
        // ToneMappingNode and PostEffectsNode added before everything else.
        var names = CompileNodes([
            new ToneMappingNode(),
            new PostEffectsNode(),
            new PrepareNode(),
            new FrustumCullNode(),
            new PointCullNode(),
            new DepthPassNode(),
            new ForwardPlusLightCullingNode(),
            new ForwardPlusOpaqueNode(),
            new PointRenderNode(),
            new ForwardPlusTransparentNode(),
            new WBOITCompositeNode(),
        ]);

        AssertDefaultPipelineOrder(names);
    }

    // -----------------------------------------------------------------------
    // Default pipeline — interleaved stages
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_DefaultPipeline_InterleavedStageInsertion_CorrectStageOrder()
    {
        // Nodes from different stages interleaved: one from each stage in turn.
        var names = CompileNodes([
            new ToneMappingNode(), // ToneMap
            new ForwardPlusOpaqueNode(), // Opaque
            new PostEffectsNode(), // PostProcess
            new PrepareNode(), // Prepare
            new WBOITCompositeNode(), // Transparent
            new DepthPassNode(), // Opaque
            new ForwardPlusTransparentNode(), // Transparent
            new FrustumCullNode(), // Prepare
            new ForwardPlusLightCullingNode(), // Opaque
            new PointCullNode(), // Prepare
            new PointRenderNode(), // Opaque
        ]);

        AssertDefaultPipelineOrder(names);
    }

    // -----------------------------------------------------------------------
    // Default pipeline — opaque-last insertion
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("RenderGraph")]
    public void Compile_DefaultPipeline_OpaqueNodesLast_CorrectStageOrder()
    {
        // All opaque-stage nodes added after transparent and post-process nodes.
        var names = CompileNodes([
            new PrepareNode(),
            new FrustumCullNode(),
            new PointCullNode(),
            new ForwardPlusTransparentNode(),
            new WBOITCompositeNode(),
            new PostEffectsNode(),
            new ToneMappingNode(),
            new DepthPassNode(),
            new ForwardPlusLightCullingNode(),
            new ForwardPlusOpaqueNode(),
            new PointRenderNode(),
        ]);

        AssertDefaultPipelineOrder(names);
    }
}
