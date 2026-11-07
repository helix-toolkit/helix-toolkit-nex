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
