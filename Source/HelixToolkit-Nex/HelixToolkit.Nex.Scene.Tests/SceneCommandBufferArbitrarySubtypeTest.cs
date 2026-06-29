using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Tests for Property 9 (feature: engine-node-command-buffer): Any <see cref="Node"/> subtype is
/// supported without Scene-layer changes.
///
/// For any <see cref="Node"/> subtype — including a test-defined subtype unknown to the Scene
/// layer — recording a factory that constructs it and then flushing materializes an instance of
/// that subtype, demonstrating the buffer accepts arbitrary subtypes without referencing any
/// concrete custom type.
///
/// Validates: Requirements 4.1, 4.4
///
/// Both these tests and the scene command buffer use the single consolidated
/// <c>HelixToolkit.Nex.ResultCode</c> enum, so comparisons are made directly against its members.
/// </summary>
[TestClass]
public class SceneCommandBufferArbitrarySubtypeTest
{
    // --- Test-local Node subtypes, unknown to the Scene layer ---
    // Each subtype carries distinct subtype-specific extra state of a different shape so the
    // materialized instance can be checked both for its exact runtime type and for the state the
    // factory captured at record time.

    /// <summary>A subtype carrying an integer payload.</summary>
    private sealed class IntPayloadNode : Node
    {
        public int Value { get; }

        public IntPayloadNode(World world, int value)
            : base(world)
        {
            Value = value;
        }
    }

    /// <summary>A subtype carrying a string payload.</summary>
    private sealed class StringPayloadNode : Node
    {
        public string Label { get; }

        public StringPayloadNode(World world, string label)
            : base(world)
        {
            Label = label;
        }
    }

    /// <summary>A subtype carrying a value-type (struct) payload.</summary>
    private readonly struct Payload
    {
        public Payload(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

    private sealed class StructPayloadNode : Node
    {
        public Payload Data { get; }

        public StructPayloadNode(World world, Payload data)
            : base(world)
        {
            Data = data;
        }
    }

    /// <summary>
    /// A further-derived subtype (two levels below <see cref="Node"/>) to show the buffer materializes
    /// the exact concrete type, not just an assignable base.
    /// </summary>
    private class DerivedNode : Node
    {
        public int Generation { get; }

        public DerivedNode(World world, int generation)
            : base(world)
        {
            Generation = generation;
        }
    }

    private sealed class FurtherDerivedNode : DerivedNode
    {
        public string Marker { get; }

        public FurtherDerivedNode(World world, int generation, string marker)
            : base(world, generation)
        {
            Marker = marker;
        }
    }

    // Identifies which subtype a given slot should materialize, plus the state it should carry.
    private enum SubtypeKind
    {
        Int = 0,
        String = 1,
        Struct = 2,
        FurtherDerived = 3,
    }

    [TestMethod]
    public void Property9_ArbitraryNodeSubtype_IsMaterializedAsThatSubtype()
    {
        // Feature: engine-node-command-buffer, Property 9
        // For any Node subtype — including a test-defined subtype unknown to the Scene layer —
        // recording a factory that constructs it and then flushing materializes an instance of
        // that subtype.
        // Validates: Requirements 4.1, 4.4

        // A program is described by a custom-node count n (>= 1) and a seed that deterministically
        // derives, for each slot, which of the four test-local subtypes to construct, its
        // subtype-specific state, and how many base node-creation commands to interleave before
        // it. Interleaving base nodes exercises arbitrary subtypes in a mixed buffer.
        var gen =
            from n in Gen.Choose(1, 25)
            from seed in Gen.Choose(int.MinValue, int.MaxValue)
            select (n, seed);

        Prop.ForAll(
                Arb.From(gen),
                ((int n, int seed) t) =>
                {
                    var world = World.CreateWorld();
                    try
                    {
                        var scb = new SceneCommandBuffer();
                        var rng = new Random(t.seed);
                        var n = t.n;

                        var handles = new DeferredNode[n];
                        var expectedKind = new SubtypeKind[n];
                        var expectedInt = new int[n];
                        var expectedString = new string[n];
                        var expectedStruct = new Payload[n];
                        var expectedGeneration = new int[n];
                        var expectedMarker = new string[n];

                        for (var i = 0; i < n; i++)
                        {
                            // Interleave 0..2 base node-creation commands before each custom one.
                            var interleave = rng.Next(0, 3);
                            for (var b = 0; b < interleave; b++)
                            {
                                scb.RecordCreateNode();
                            }

                            var kind = (SubtypeKind)rng.Next(0, 4);
                            expectedKind[i] = kind;

                            ResultCode code;
                            switch (kind)
                            {
                                case SubtypeKind.Int:
                                {
                                    var value = rng.Next();
                                    expectedInt[i] = value;
                                    code = scb.TryRecordCreateNode<IntPayloadNode>(
                                        w => new IntPayloadNode(w, value),
                                        out var handle
                                    );
                                    handles[i] = handle;
                                    break;
                                }
                                case SubtypeKind.String:
                                {
                                    var label = "s" + rng.Next();
                                    expectedString[i] = label;
                                    code = scb.TryRecordCreateNode<StringPayloadNode>(
                                        w => new StringPayloadNode(w, label),
                                        out var handle
                                    );
                                    handles[i] = handle;
                                    break;
                                }
                                case SubtypeKind.Struct:
                                {
                                    var payload = new Payload(rng.NextDouble(), rng.NextDouble());
                                    expectedStruct[i] = payload;
                                    code = scb.TryRecordCreateNode<StructPayloadNode>(
                                        w => new StructPayloadNode(w, payload),
                                        out var handle
                                    );
                                    handles[i] = handle;
                                    break;
                                }
                                default: // FurtherDerived
                                {
                                    var generation = rng.Next();
                                    var marker = "m" + rng.Next();
                                    expectedGeneration[i] = generation;
                                    expectedMarker[i] = marker;
                                    code = scb.TryRecordCreateNode<FurtherDerivedNode>(
                                        w => new FurtherDerivedNode(w, generation, marker),
                                        out var handle
                                    );
                                    handles[i] = handle;
                                    break;
                                }
                            }

                            if (code != ResultCode.Ok)
                            {
                                return false;
                            }
                        }

                        var flush = scb.Flush(world);
                        if (!flush.Success)
                        {
                            return false;
                        }

                        for (var i = 0; i < n; i++)
                        {
                            if (!scb.MaterializedNodes.TryGetValue(handles[i], out var node))
                            {
                                return false;
                            }

                            switch (expectedKind[i])
                            {
                                case SubtypeKind.Int:
                                    // Exact runtime type (GetType) and is-check, plus state.
                                    if (node.GetType() != typeof(IntPayloadNode))
                                    {
                                        return false;
                                    }
                                    if (node is not IntPayloadNode ip || ip.Value != expectedInt[i])
                                    {
                                        return false;
                                    }
                                    break;
                                case SubtypeKind.String:
                                    if (node.GetType() != typeof(StringPayloadNode))
                                    {
                                        return false;
                                    }
                                    if (
                                        node is not StringPayloadNode sp
                                        || sp.Label != expectedString[i]
                                    )
                                    {
                                        return false;
                                    }
                                    break;
                                case SubtypeKind.Struct:
                                    if (node.GetType() != typeof(StructPayloadNode))
                                    {
                                        return false;
                                    }
                                    if (
                                        node is not StructPayloadNode stp
                                        || Math.Abs(stp.Data.X - expectedStruct[i].X) > 1e-6
                                        || Math.Abs(stp.Data.Y - expectedStruct[i].Y) > 1e-6
                                    )
                                    {
                                        return false;
                                    }
                                    break;
                                default: // FurtherDerived
                                    // Exact concrete type, not the assignable base DerivedNode.
                                    if (node.GetType() != typeof(FurtherDerivedNode))
                                    {
                                        return false;
                                    }
                                    if (
                                        node is not FurtherDerivedNode fd
                                        || fd.Generation != expectedGeneration[i]
                                        || fd.Marker != expectedMarker[i]
                                    )
                                    {
                                        return false;
                                    }
                                    // It is assignable to its base, but the runtime type is exact.
                                    if (node is not DerivedNode)
                                    {
                                        return false;
                                    }
                                    break;
                            }
                        }

                        return true;
                    }
                    finally
                    {
                        world.Dispose();
                    }
                }
            )
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void ArbitrarySubtype_TypedRetrievalReturnsExactConcreteType()
    {
        // Feature: engine-node-command-buffer, Property 9 (concrete example)
        // A factory constructing a test-local subtype yields a materialized node whose exact
        // runtime type is that subtype, carrying the subtype-specific state captured at record
        // time.
        // Validates: Requirements 4.1, 4.4
        var world = World.CreateWorld();
        try
        {
            var scb = new SceneCommandBuffer();

            var hInt = scb.RecordCreateNode<IntPayloadNode>(w => new IntPayloadNode(w, 123));
            var hStr = scb.RecordCreateNode<StringPayloadNode>(w => new StringPayloadNode(
                w,
                "hello"
            ));
            var hStruct = scb.RecordCreateNode<StructPayloadNode>(w => new StructPayloadNode(
                w,
                new Payload(1.5, -2.5)
            ));
            var hDerived = scb.RecordCreateNode<FurtherDerivedNode>(w => new FurtherDerivedNode(
                w,
                9,
                "mark"
            ));

            var flush = scb.Flush(world);
            Assert.IsTrue(flush.Success);

            Assert.AreEqual(ResultCode.Ok, scb.TryGetMaterializedNode(hInt, out var intNode));
            Assert.IsNotNull(intNode);
            Assert.AreEqual(typeof(IntPayloadNode), intNode.GetType());
            Assert.AreEqual(123, intNode.Value);

            Assert.AreEqual(ResultCode.Ok, scb.TryGetMaterializedNode(hStr, out var strNode));
            Assert.IsNotNull(strNode);
            Assert.AreEqual(typeof(StringPayloadNode), strNode.GetType());
            Assert.AreEqual("hello", strNode.Label);

            Assert.AreEqual(ResultCode.Ok, scb.TryGetMaterializedNode(hStruct, out var structNode));
            Assert.IsNotNull(structNode);
            Assert.AreEqual(typeof(StructPayloadNode), structNode.GetType());
            Assert.AreEqual(1.5, structNode.Data.X);
            Assert.AreEqual(-2.5, structNode.Data.Y);

            Assert.AreEqual(
                ResultCode.Ok,
                scb.TryGetMaterializedNode(hDerived, out var derivedNode)
            );
            Assert.IsNotNull(derivedNode);
            // Exact concrete type, even though it derives from DerivedNode which derives from Node.
            Assert.AreEqual(typeof(FurtherDerivedNode), derivedNode.GetType());
            Assert.IsInstanceOfType(derivedNode, typeof(DerivedNode));
            Assert.AreEqual(9, derivedNode.Generation);
            Assert.AreEqual("mark", derivedNode.Marker);
        }
        finally
        {
            world.Dispose();
        }
    }
}
