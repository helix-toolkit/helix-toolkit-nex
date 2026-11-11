using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine.Data;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Scene;
using HelixToolkit.Nex.Shaders;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Tests for <see cref="SkinningData"/>, which collects skinned meshes and prepares the skinning
/// compute dispatches, using the mock graphics context.
/// </summary>
[TestClass]
public sealed class SkinningDataTests
{
    private MockContext _context = null!;
    private World _world = null!;
    private GeometryManager _geometryManager = null!;
    private SkinningData _skinningData = null!;

    [TestInitialize]
    public void Setup()
    {
        _context = new MockContext();
        Assert.AreEqual(ResultCode.Ok, _context.Initialize());
        _world = World.CreateWorld();
        _geometryManager = new GeometryManager(_context);
        _skinningData = new SkinningData(_context, _world);
        Assert.AreEqual(ResultCode.Ok, _skinningData.Initialize());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _skinningData.Dispose();
        _geometryManager.Dispose();
        _world.Dispose();
        _context.Dispose();
    }

    private Geometry CreateGeometry(bool skinned)
    {
        var geometry = new Geometry(
            [new Vector4(0, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(0, 1, 0, 1)],
            [new VertexProperties(Vector3.UnitZ), new VertexProperties(Vector3.UnitZ), new VertexProperties(Vector3.UnitZ)],
            [0u, 1u, 2u]
        );
        if (skinned)
        {
            for (int i = 0; i < geometry.Vertices.Count; i++)
            {
                geometry.JointInfluences.Add(
                    new GpuJointInfluence
                    {
                        Joint0 = 0,
                        Joint1 = 1,
                        Weights = new Vector4(0.5f, 0.5f, 0, 0),
                    }
                );
            }
        }
        Assert.IsTrue(_geometryManager.Add(geometry).Valid);
        return geometry;
    }

    private Skinning CreateSkinning()
    {
        var joint0 = new Node(_world, "Joint0");
        var joint1 = new Node(_world, "Joint1");
        var skeleton = new Skeleton([Matrix4x4.Identity, Matrix4x4.Identity], "TestSkeleton");
        return new Skinning(skeleton, [joint0.Entity, joint1.Entity]);
    }

    [TestMethod]
    public void SkinnedMesh_ProducesDispatch_AndCreatesBuffers()
    {
        var geometry = CreateGeometry(skinned: true);
        var skinning = CreateSkinning();
        _ = new MeshNode(_world, "Skinned") { Geometry = geometry, Skinning = skinning };

        Assert.IsTrue(_skinningData.Update());

        Assert.AreEqual(1, _skinningData.Dispatches.Length);
        Assert.AreSame(skinning, _skinningData.Dispatches[0].Skinning);
        Assert.AreSame(geometry, _skinningData.Dispatches[0].Geometry);
        Assert.IsTrue(skinning.IsReady);
        Assert.AreEqual(geometry.Vertices.Count, skinning.VertexCount);
        Assert.IsTrue(skinning.JointMatrixBuffer.Valid);
        Assert.IsTrue(skinning.PaletteBuffer.Valid);
        Assert.IsTrue(skinning.SkinnedVertexBuffer.Valid);
        Assert.IsTrue(skinning.SkinnedVertexPropsBuffer.Valid);
        Assert.IsTrue(skinning.Skeleton.InverseBindBuffer.Valid);
    }

    [TestMethod]
    public void SkinPosition_FollowsJointNodes_RelativeToMeshNode()
    {
        var joint0 = new Node(_world, "Joint0");
        var joint1 = new Node(_world, "Joint1");
        // Joint 1 is bound at x = 1 and posed rotated 90 degrees about Z at the same location.
        joint1.Transform.Translation = new Vector3(1, 0, 0);
        joint1.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        var skeleton = new Skeleton([Matrix4x4.Identity, Matrix4x4.CreateTranslation(-1, 0, 0)]);
        var skinning = new Skinning(skeleton, [joint0.Entity, joint1.Entity]);
        var mesh = new MeshNode(_world, "Skinned")
        {
            Geometry = CreateGeometry(true),
            Skinning = skinning,
        };
        // The mesh node transform must not affect the skinned pose (glTF semantics):
        // the joint matrices are expressed relative to the mesh node.
        mesh.Transform.Translation = new Vector3(5, 0, 0);
        _world.SortSceneNodes();
        _world.UpdateTransforms();

        Assert.IsTrue(_skinningData.Update());

        var bound = new GpuJointInfluence { Joint0 = 1, Weights = Vector4.UnitX };
        var local = skinning.SkinPosition(new Vector3(2, 0, 0), bound);
        var world = Vector3.Transform(local, mesh.Entity.Get<WorldTransform>().Value);
        Assert.IsTrue(Vector3.Distance(new Vector3(1, 1, 0), world) < 1e-5f, $"Got {world}");

        var unbound = new GpuJointInfluence { Joint0 = 0, Weights = Vector4.UnitX };
        world = Vector3.Transform(
            skinning.SkinPosition(new Vector3(0.5f, 0, 0), unbound),
            mesh.Entity.Get<WorldTransform>().Value
        );
        Assert.IsTrue(Vector3.Distance(new Vector3(0.5f, 0, 0), world) < 1e-5f, $"Got {world}");
    }

    [TestMethod]
    public void MeshWithoutJointInfluences_IsNotDispatched()
    {
        var geometry = CreateGeometry(skinned: false);
        _ = new MeshNode(_world, "NotSkinnable") { Geometry = geometry, Skinning = CreateSkinning() };

        Assert.IsTrue(_skinningData.Update());

        Assert.AreEqual(0, _skinningData.Dispatches.Length);
    }

    [TestMethod]
    public void SharedSkinning_IsDispatchedOnce()
    {
        var skinning = CreateSkinning();
        _ = new MeshNode(_world, "A") { Geometry = CreateGeometry(true), Skinning = skinning };
        _ = new MeshNode(_world, "B") { Geometry = CreateGeometry(true), Skinning = skinning };

        Assert.IsTrue(_skinningData.Update());

        Assert.AreEqual(1, _skinningData.Dispatches.Length);
    }

    [TestMethod]
    public void RemovingSkinning_StopsDispatch()
    {
        var node = new MeshNode(_world, "Skinned")
        {
            Geometry = CreateGeometry(true),
            Skinning = CreateSkinning(),
        };
        Assert.IsTrue(_skinningData.Update());
        Assert.AreEqual(1, _skinningData.Dispatches.Length);

        node.Skinning = null;
        Assert.IsTrue(_skinningData.Update());

        Assert.AreEqual(0, _skinningData.Dispatches.Length);
    }

    [TestMethod]
    public void DisabledNode_IsNotDispatched()
    {
        var node = new MeshNode(_world, "Skinned")
        {
            Geometry = CreateGeometry(true),
            Skinning = CreateSkinning(),
        };
        node.Enabled = false;

        Assert.IsTrue(_skinningData.Update());

        Assert.AreEqual(0, _skinningData.Dispatches.Length);
    }

    [TestMethod]
    public void RepeatedUpdate_WithoutTransformChange_DoesNotAdvanceJointRing()
    {
        // Every viewport rendering the world updates it, but the joint-matrix ring buffer must
        // advance at most once per engine frame (frames are paced once per engine frame).
        var joint0 = new Node(_world, "Joint0");
        var joint1 = new Node(_world, "Joint1");
        var skeleton = new Skeleton([Matrix4x4.Identity, Matrix4x4.Identity]);
        var skinning = new Skinning(skeleton, [joint0.Entity, joint1.Entity]);
        _ = new MeshNode(_world, "Skinned") { Geometry = CreateGeometry(true), Skinning = skinning };
        _world.SortSceneNodes();
        _world.UpdateTransforms();

        Assert.IsTrue(_skinningData.Update());
        var uploaded = skinning.JointMatrixBuffer;

        Assert.IsTrue(_skinningData.Update());
        Assert.AreSame(uploaded, skinning.JointMatrixBuffer);
        Assert.AreEqual(1, _skinningData.Dispatches.Length, "Still dispatched without an upload.");

        joint1.Transform.Translation = new Vector3(0, 2, 0);
        joint1.NotifyTransformChanged();
        _world.UpdateTransforms();

        Assert.IsTrue(_skinningData.Update());
        Assert.AreNotSame(uploaded, skinning.JointMatrixBuffer);
        var moved = skinning.SkinPosition(
            Vector3.Zero,
            new GpuJointInfluence { Joint0 = 1, Weights = Vector4.UnitX }
        );
        Assert.IsTrue(Vector3.Distance(new Vector3(0, 2, 0), moved) < 1e-5f, $"Got {moved}");
    }

    [TestMethod]
    public void SkinnedMesh_IsNeverCullable()
    {
        var info = new MeshDrawInfo(cullable: true);
        Assert.IsTrue(info.IsCullable);
        Assert.IsFalse(info.IsSkinned);

        info.Skinning = CreateSkinning();
        Assert.IsTrue(info.Cullable);
        Assert.IsFalse(info.IsCullable);
        Assert.IsTrue(info.IsSkinned);
    }

    private Geometry CreateMorphGeometry(bool skinned)
    {
        var geometry = new Geometry(
            [new Vector4(0, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(0, 1, 0, 1)],
            [new VertexProperties(Vector3.UnitZ), new VertexProperties(Vector3.UnitZ), new VertexProperties(Vector3.UnitZ)],
            [0u, 1u, 2u]
        );
        var up = new MorphTarget("Up");
        var side = new MorphTarget("Side");
        for (int i = 0; i < geometry.Vertices.Count; i++)
        {
            up.PositionDeltas.Add(new Vector3(0, 0, 1));
            side.PositionDeltas.Add(new Vector3(i, 0, 0));
            if (skinned)
            {
                geometry.JointInfluences.Add(new GpuJointInfluence { Joint0 = 0, Weights = Vector4.UnitX });
            }
        }
        geometry.MorphTargets.Add(up);
        geometry.MorphTargets.Add(side);
        Assert.IsTrue(_geometryManager.Add(geometry).Valid);
        Assert.IsTrue(geometry.MorphDeltaBuffer.Valid);
        return geometry;
    }

    [TestMethod]
    public void MorphedMesh_ProducesDispatch_AndOwnsOutput()
    {
        var geometry = CreateMorphGeometry(skinned: false);
        var morphing = new Morphing(new MorphWeights([0.5f, 0f]));
        var node = new MeshNode(_world, "Morphed") { Geometry = geometry, Morphing = morphing };

        Assert.IsTrue(_skinningData.Update());

        Assert.AreEqual(1, _skinningData.Dispatches.Length);
        var dispatch = _skinningData.Dispatches[0];
        Assert.IsNull(dispatch.Skinning);
        Assert.AreSame(morphing, dispatch.Morphing);
        Assert.AreEqual(geometry.Vertices.Count, dispatch.VertexCount);
        Assert.IsTrue(morphing.MorphedVertexBuffer.Valid);
        Assert.IsTrue(morphing.MorphedVertexPropsBuffer.Valid);
        Assert.AreEqual(1, morphing.ActiveWeightCount, "Zero weights are not uploaded.");
        var info = node.Entity.Get<MeshDrawInfo>();
        Assert.AreSame(morphing.MorphedVertexBuffer, info.DeformedVertexBuffer);
        Assert.AreSame(morphing.MorphedVertexPropsBuffer, info.DeformedVertexPropsBuffer);
    }

    [TestMethod]
    public void SkinnedAndMorphedMesh_SkinningOwnsOutput()
    {
        var geometry = CreateMorphGeometry(skinned: true);
        var morphing = new Morphing(new MorphWeights([1f, 1f]));
        var skinning = CreateSkinning();
        var node = new MeshNode(_world, "Both")
        {
            Geometry = geometry,
            Skinning = skinning,
            Morphing = morphing,
        };

        Assert.IsTrue(_skinningData.Update());

        Assert.AreEqual(1, _skinningData.Dispatches.Length);
        Assert.AreSame(skinning, _skinningData.Dispatches[0].Skinning);
        Assert.AreSame(morphing, _skinningData.Dispatches[0].Morphing);
        Assert.IsFalse(morphing.MorphedVertexBuffer.Valid, "The skinning owns the deformed output.");
        Assert.AreEqual(2, morphing.ActiveWeightCount);
        Assert.AreSame(skinning.SkinnedVertexBuffer, node.Entity.Get<MeshDrawInfo>().DeformedVertexBuffer);

        // Removing the skinning hands the output over to the morphing.
        node.Skinning = null;
        Assert.IsTrue(_skinningData.Update());
        Assert.IsTrue(morphing.MorphedVertexBuffer.Valid);
        Assert.AreSame(morphing.MorphedVertexBuffer, node.Entity.Get<MeshDrawInfo>().DeformedVertexBuffer);
    }

    [TestMethod]
    public void SkinnedAndMorphedMesh_LosingJointInfluences_DrawsMorphedOutput()
    {
        var morphing = new Morphing(new MorphWeights([1f, 0f]));
        var skinning = CreateSkinning();
        var node = new MeshNode(_world, "Both")
        {
            Geometry = CreateMorphGeometry(skinned: true),
            Skinning = skinning,
            Morphing = morphing,
        };
        Assert.IsTrue(_skinningData.Update());
        Assert.IsTrue(skinning.SkinnedVertexBuffer.Valid);

        // The new geometry has morph targets but no joint influences: the skinning no longer
        // applies, so its stale output must not be drawn.
        node.Geometry = CreateMorphGeometry(skinned: false);
        Assert.IsTrue(_skinningData.Update());

        Assert.IsNull(_skinningData.Dispatches[0].Skinning);
        Assert.IsFalse(skinning.SkinnedVertexBuffer.Valid);
        Assert.AreSame(morphing.MorphedVertexBuffer, node.Entity.Get<MeshDrawInfo>().DeformedVertexBuffer);
        Assert.IsTrue(morphing.MorphedVertexBuffer.Valid);
    }

    [TestMethod]
    public void MorphedMesh_LosingMorphTargets_DrawsBaseVertices()
    {
        var morphing = new Morphing(new MorphWeights([1f, 0f]));
        var node = new MeshNode(_world, "Morphed") { Geometry = CreateMorphGeometry(false), Morphing = morphing };
        Assert.IsTrue(_skinningData.Update());
        Assert.IsTrue(morphing.MorphedVertexBuffer.Valid);

        node.Geometry = CreateGeometry(skinned: false);
        Assert.IsTrue(_skinningData.Update());

        Assert.AreEqual(0, _skinningData.Dispatches.Length);
        Assert.IsFalse(node.Entity.Get<MeshDrawInfo>().DeformedVertexBuffer.Valid, "Falls back to the base vertices.");
    }

    [TestMethod]
    public void MorphWeights_AreUploadedOnlyWhenChanged()
    {
        var weights = new MorphWeights(2);
        var morphing = new Morphing(weights);
        _ = new MeshNode(_world, "Morphed") { Geometry = CreateMorphGeometry(false), Morphing = morphing };

        Assert.IsTrue(_skinningData.Update());
        Assert.AreEqual(0, morphing.ActiveWeightCount);
        Assert.AreEqual(1, _skinningData.Dispatches.Length, "Dispatched with all weights zero.");
        var uploaded = morphing.WeightBuffer;

        Assert.IsTrue(_skinningData.Update());
        Assert.AreSame(uploaded, morphing.WeightBuffer, "Unchanged weights do not advance the ring.");

        weights[1] = 0.25f;
        Assert.IsTrue(_skinningData.Update());
        Assert.AreNotSame(uploaded, morphing.WeightBuffer);
        Assert.AreEqual(1, morphing.ActiveWeightCount);
    }

    [TestMethod]
    public void MorphPosition_AppliesUploadedWeights()
    {
        var geometry = CreateMorphGeometry(false);
        var weights = new MorphWeights([0.5f, 2f]);
        var morphing = new Morphing(weights);
        _ = new MeshNode(_world, "Morphed") { Geometry = geometry, Morphing = morphing };

        Assert.IsTrue(_skinningData.Update());

        // Vertex 2: base (0, 1, 0) + 0.5 * (0, 0, 1) + 2 * (2, 0, 0).
        var morphed = morphing.MorphPosition(geometry, 2, new Vector3(0, 1, 0));
        Assert.IsTrue(Vector3.Distance(new Vector3(4, 1, 0.5f), morphed) < 1e-5f, $"Got {morphed}");
    }

    [TestMethod]
    public void MorphedMesh_IsNeverCullable()
    {
        var info = new MeshDrawInfo(cullable: true) { Morphing = new Morphing(new MorphWeights(1)) };
        Assert.IsTrue(info.IsMorphed);
        Assert.IsTrue(info.IsDeformed);
        Assert.IsFalse(info.IsCullable);
    }

    [TestMethod]
    public void Skinning_RejectsJointCountMismatch()
    {
        var skeleton = new Skeleton([Matrix4x4.Identity, Matrix4x4.Identity]);
        Assert.ThrowsException<ArgumentException>(() => new Skinning(skeleton, [Entity.Null]));
    }

    [TestMethod]
    public void Skeleton_RejectsEmptyJoints()
    {
        Assert.ThrowsException<ArgumentException>(() => new Skeleton([]));
    }
}
