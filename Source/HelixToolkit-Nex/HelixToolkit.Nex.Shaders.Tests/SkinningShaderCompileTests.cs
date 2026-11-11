using System.Numerics;
using System.Runtime.InteropServices;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Vulkan;
using HelixToolkit.Nex.Material;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Shaders.Tests;

/// <summary>
/// Compiles the bone skinning shaders and validates the GPU skinning results against a CPU reference.
/// </summary>
[TestClass]
[TestCategory("GPURequired")]
[TestCategory("Integration")]
public class SkinningShaderCompileTests
{
    private static IContext? _context;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        var config = new VulkanContextConfig
        {
            TerminateOnValidationError = true,
            EnableValidation = false,
        };
        _context = VulkanBuilder.CreateHeadless(config);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _context?.Dispose();
    }

    private static ComputePipelineResource CreatePipeline(GpuSkinning.Pass pass)
    {
        var source = GpuSkinning.GenerateComputeShader(pass);
        using var module = _context!.CreateShaderModuleGlsl(source, ShaderStage.Compute, $"Skin{pass}");
        Assert.IsTrue(module.Valid, "Shader module should be valid");
        return _context.CreateComputePipeline(new ComputePipelineDesc { ComputeShader = module });
    }

    [TestMethod]
    [DataRow(GpuSkinning.Pass.Palette)]
    [DataRow(GpuSkinning.Pass.Vertices)]
    public void SkinningComputeShader_CompilesAndCreatesPipeline(GpuSkinning.Pass pass)
    {
        using var pipeline = CreatePipeline(pass);
        Assert.IsTrue(pipeline.Valid, "Compute pipeline should be valid");
    }

    /// <summary>
    /// The mesh vertex shader reads skinned vertices through <c>MeshDraw</c>; make sure it still compiles.
    /// </summary>
    [TestMethod]
    public void MeshVertexShader_WithSkinnedVertexFetch_Compiles()
    {
        using var result = new PBRMaterialShaderBuilder().BuildMaterialPipeline(_context!, "SkinnedMesh");

        Assert.IsTrue(result.Success, string.Join("\n", result.Errors));
        Assert.IsTrue(result.VertexShader.Valid);
    }

    /// <summary>
    /// Runs the palette and vertex skinning passes on host-visible buffers and compares the output
    /// with linear blend skinning computed on the CPU using the engine's row-vector convention.
    /// </summary>
    [TestMethod]
    public void SkinningPasses_MatchCpuLinearBlendSkinning()
    {
        var ctx = _context!;
        // Joint 1 sits at x = 1 in the bind pose and is rotated 90 degrees about Z.
        Matrix4x4[] inverseBinds = [Matrix4x4.Identity, Matrix4x4.CreateTranslation(-1, 0, 0)];
        Matrix4x4[] joints =
        [
            Matrix4x4.CreateTranslation(0, 0.5f, 0),
            Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(1, 0, 0),
        ];
        Vector4[] positions =
        [
            new(0, 0, 0, 1),
            new(2, 0, 0, 1),
            new(1.5f, 0, 0, 1),
        ];
        GpuJointInfluence[] influences =
        [
            new() { Joint0 = 0, Weights = new Vector4(1, 0, 0, 0) },
            new() { Joint0 = 1, Weights = new Vector4(1, 0, 0, 0) },
            new() { Joint0 = 0, Joint1 = 1, Weights = new Vector4(0.5f, 0.5f, 0, 0) },
        ];
        var props = new GpuVertexProps[positions.Length];
        for (int i = 0; i < props.Length; i++)
        {
            props[i].Normal = Vector3.UnitX;
            props[i].TexCoord = new Vector2(i, 1);
            props[i].Tangent = new Vector4(0, 0, 1, -1);
        }

        using var jointBuffer = CreateBuffer(ctx, joints);
        using var inverseBindBuffer = CreateBuffer(ctx, inverseBinds);
        using var paletteBuffer = CreateBuffer(ctx, new Matrix4x4[joints.Length]);
        using var positionBuffer = CreateBuffer(ctx, positions);
        using var propsBuffer = CreateBuffer(ctx, props);
        using var influenceBuffer = CreateBuffer(ctx, influences);
        using var dstPositionBuffer = CreateBuffer(ctx, new Vector4[positions.Length]);
        using var dstPropsBuffer = CreateBuffer(ctx, new GpuVertexProps[positions.Length]);
        using var palettePipeline = CreatePipeline(GpuSkinning.Pass.Palette);
        using var verticesPipeline = CreatePipeline(GpuSkinning.Pass.Vertices);

        var cmd = ctx.AcquireCommandBuffer();
        cmd.BindComputePipeline(palettePipeline);
        cmd.PushConstants(
            new SkinPalettePC
            {
                JointMatrixAddress = jointBuffer.Buffer.GpuAddress,
                InverseBindAddress = inverseBindBuffer.Buffer.GpuAddress,
                PaletteAddress = paletteBuffer.Buffer.GpuAddress,
                JointCount = (uint)joints.Length,
            }
        );
        cmd.DispatchThreadGroups(new Dimensions(1, 1, 1), Dependencies.Empty);
        cmd.Barrier(paletteBuffer, BarrierPreset.ComputeWriteToShaderRW, force: true);
        cmd.BindComputePipeline(verticesPipeline);
        cmd.PushConstants(
            new SkinVertexPC
            {
                PaletteAddress = paletteBuffer.Buffer.GpuAddress,
                SrcVertexAddress = positionBuffer.Buffer.GpuAddress,
                SrcPropsAddress = propsBuffer.Buffer.GpuAddress,
                InfluenceAddress = influenceBuffer.Buffer.GpuAddress,
                DstVertexAddress = dstPositionBuffer.Buffer.GpuAddress,
                DstPropsAddress = dstPropsBuffer.Buffer.GpuAddress,
                VertexCount = (uint)positions.Length,
                JointCount = (uint)joints.Length,
            }
        );
        cmd.DispatchThreadGroups(new Dimensions(1, 1, 1), Dependencies.Empty);
        ctx.Wait(ctx.Submit(cmd, TextureHandle.Null));

        var skinnedPositions = Read<Vector4>(ctx, dstPositionBuffer, positions.Length);
        var skinnedProps = Read<GpuVertexProps>(ctx, dstPropsBuffer, positions.Length);

        for (int i = 0; i < positions.Length; i++)
        {
            var w = influences[i].Weights;
            var m =
                inverseBinds[influences[i].Joint0] * joints[influences[i].Joint0] * w.X
                + inverseBinds[influences[i].Joint1] * joints[influences[i].Joint1] * w.Y;
            var expectedPosition = Vector4.Transform(positions[i], m);
            var expectedNormal = Vector3.Normalize(Vector3.TransformNormal(props[i].Normal, m));
            var expectedTangent = Vector3.Normalize(
                Vector3.TransformNormal(new Vector3(0, 0, 1), m)
            );

            AssertNear(expectedPosition, skinnedPositions[i], $"position {i}");
            AssertNear(new Vector4(expectedNormal, 0), new Vector4(skinnedProps[i].Normal, 0), $"normal {i}");
            AssertNear(
                new Vector4(expectedTangent, -1),
                skinnedProps[i].Tangent,
                $"tangent {i}"
            );
            Assert.AreEqual(props[i].TexCoord, skinnedProps[i].TexCoord, $"texcoord {i}");
        }
        // Sanity check of the convention: the vertex bound to joint 1 rotates to (1, 1, 0).
        AssertNear(new Vector4(1, 1, 0, 1), skinnedPositions[1], "rotated vertex");
    }

    /// <summary>
    /// Runs the vertex deformation pass with morph targets, without and with skinning, and compares
    /// the output with the CPU reference: <c>skin(base + sum(w * delta))</c>.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DeformationPass_MorphTargets_MatchCpuReference(bool skinned)
    {
        var ctx = _context!;
        Vector4[] positions = [new(0, 0, 0, 1), new(1, 0, 0, 1), new(0, 1, 0, 1)];
        var props = new GpuVertexProps[positions.Length];
        for (int i = 0; i < props.Length; i++)
        {
            props[i].Normal = Vector3.UnitZ;
            props[i].TexCoord = new Vector2(i, 0);
            props[i].Tangent = new Vector4(1, 0, 0, 1);
        }
        // Two targets, target-major: target 0 moves along Y and tilts the normal, target 1 along Z.
        var deltas = new GpuMorphDelta[2 * positions.Length];
        for (int v = 0; v < positions.Length; v++)
        {
            deltas[v] = new GpuMorphDelta { Position = new Vector3(0, 1, 0), Normal = new Vector3(1, 0, 0) };
            deltas[positions.Length + v] = new GpuMorphDelta { Position = new Vector3(0, 0, v + 1) };
        }
        GpuMorphWeight[] weights = [new() { Target = 0, Weight = 0.5f }, new() { Target = 1, Weight = 2f }];
        var skin = Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(3, 0, 0);
        var influences = new GpuJointInfluence[positions.Length];
        for (int i = 0; i < influences.Length; i++)
        {
            influences[i] = new GpuJointInfluence { Joint0 = 0, Weights = Vector4.UnitX };
        }

        using var positionBuffer = CreateBuffer(ctx, positions);
        using var propsBuffer = CreateBuffer(ctx, props);
        using var deltaBuffer = CreateBuffer(ctx, deltas);
        using var weightBuffer = CreateBuffer(ctx, weights);
        using var paletteBuffer = CreateBuffer(ctx, new[] { skin });
        using var influenceBuffer = CreateBuffer(ctx, influences);
        using var dstPositionBuffer = CreateBuffer(ctx, new Vector4[positions.Length]);
        using var dstPropsBuffer = CreateBuffer(ctx, new GpuVertexProps[positions.Length]);
        using var verticesPipeline = CreatePipeline(GpuSkinning.Pass.Vertices);

        var cmd = ctx.AcquireCommandBuffer();
        cmd.BindComputePipeline(verticesPipeline);
        cmd.PushConstants(
            new SkinVertexPC
            {
                PaletteAddress = skinned ? paletteBuffer.Buffer.GpuAddress : 0,
                SrcVertexAddress = positionBuffer.Buffer.GpuAddress,
                SrcPropsAddress = propsBuffer.Buffer.GpuAddress,
                InfluenceAddress = skinned ? influenceBuffer.Buffer.GpuAddress : 0,
                DstVertexAddress = dstPositionBuffer.Buffer.GpuAddress,
                DstPropsAddress = dstPropsBuffer.Buffer.GpuAddress,
                MorphDeltaAddress = deltaBuffer.Buffer.GpuAddress,
                MorphWeightAddress = weightBuffer.Buffer.GpuAddress,
                VertexCount = (uint)positions.Length,
                JointCount = skinned ? 1u : 0u,
                MorphWeightCount = (uint)weights.Length,
            }
        );
        cmd.DispatchThreadGroups(new Dimensions(1, 1, 1), Dependencies.Empty);
        ctx.Wait(ctx.Submit(cmd, TextureHandle.Null));

        var outPositions = Read<Vector4>(ctx, dstPositionBuffer, positions.Length);
        var outProps = Read<GpuVertexProps>(ctx, dstPropsBuffer, positions.Length);
        var m = skinned ? skin : Matrix4x4.Identity;
        for (int v = 0; v < positions.Length; v++)
        {
            var p = positions[v];
            var morphed = new Vector3(p.X, p.Y, p.Z) + 0.5f * deltas[v].Position + 2f * deltas[positions.Length + v].Position;
            var normal = props[v].Normal + 0.5f * deltas[v].Normal;
            var expectedPosition = new Vector4(Vector3.Transform(morphed, m), 1);
            var expectedNormal = Vector3.Normalize(Vector3.TransformNormal(normal, m));
            var expectedTangent = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, m));

            AssertNear(expectedPosition, outPositions[v], $"position {v}");
            AssertNear(new Vector4(expectedNormal, 0), new Vector4(outProps[v].Normal, 0), $"normal {v}");
            AssertNear(new Vector4(expectedTangent, 1), outProps[v].Tangent, $"tangent {v}");
            Assert.AreEqual(props[v].TexCoord, outProps[v].TexCoord, $"texcoord {v}");
        }
    }

    /// <summary>
    /// Generated flat normals are recomputed from the morphed triangle when the flat-normal flag is
    /// set, and kept otherwise.
    /// </summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void DeformationPass_FlatNormals_FollowMorphedTriangle(bool flatNormals)
    {
        var ctx = _context!;
        Vector4[] positions = [new(0, 0, 0, 1), new(1, 0, 0, 1), new(0, 1, 0, 1)];
        var props = new GpuVertexProps[positions.Length];
        for (int i = 0; i < props.Length; i++)
        {
            props[i].Normal = Vector3.UnitZ;
            props[i].Tangent = new Vector4(1, 0, 0, 1);
        }
        // Lift the third corner along +Z: the face normal tilts to normalize(0, -1, 1).
        var deltas = new GpuMorphDelta[positions.Length];
        deltas[2].Position = Vector3.UnitZ;
        GpuMorphWeight[] weights = [new() { Target = 0, Weight = 1f }];

        using var positionBuffer = CreateBuffer(ctx, positions);
        using var propsBuffer = CreateBuffer(ctx, props);
        using var deltaBuffer = CreateBuffer(ctx, deltas);
        using var weightBuffer = CreateBuffer(ctx, weights);
        using var dstPositionBuffer = CreateBuffer(ctx, new Vector4[positions.Length]);
        using var dstPropsBuffer = CreateBuffer(ctx, new GpuVertexProps[positions.Length]);
        using var verticesPipeline = CreatePipeline(GpuSkinning.Pass.Vertices);

        var cmd = ctx.AcquireCommandBuffer();
        cmd.BindComputePipeline(verticesPipeline);
        cmd.PushConstants(
            new SkinVertexPC
            {
                SrcVertexAddress = positionBuffer.Buffer.GpuAddress,
                SrcPropsAddress = propsBuffer.Buffer.GpuAddress,
                DstVertexAddress = dstPositionBuffer.Buffer.GpuAddress,
                DstPropsAddress = dstPropsBuffer.Buffer.GpuAddress,
                MorphDeltaAddress = deltaBuffer.Buffer.GpuAddress,
                MorphWeightAddress = weightBuffer.Buffer.GpuAddress,
                VertexCount = (uint)positions.Length,
                MorphWeightCount = (uint)weights.Length,
                Flags = flatNormals ? GpuSkinning.FlagFlatNormals : 0,
            }
        );
        cmd.DispatchThreadGroups(new Dimensions(1, 1, 1), Dependencies.Empty);
        ctx.Wait(ctx.Submit(cmd, TextureHandle.Null));

        var outProps = Read<GpuVertexProps>(ctx, dstPropsBuffer, positions.Length);
        var expected = flatNormals ? Vector3.Normalize(new Vector3(0, -1, 1)) : Vector3.UnitZ;
        for (int v = 0; v < positions.Length; v++)
        {
            AssertNear(new Vector4(expected, 0), new Vector4(outProps[v].Normal, 0), $"normal {v}");
        }
    }

    /// <summary>
    /// Generated tangents follow the morphed surface: with flat normals the face tangent is
    /// recomputed from the morphed triangle and its texture coordinates; otherwise the tangent is
    /// re-orthogonalized against the morphed normal. Authored tangents are kept.
    /// </summary>
    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public void DeformationPass_GeneratedTangents_FollowMorphedSurface(bool flatNormals, bool generated)
    {
        var ctx = _context!;
        Vector4[] positions = [new(0, 0, 0, 1), new(1, 0, 0, 1), new(0, 1, 0, 1)];
        Vector2[] uvs = [new(0, 0), new(1, 0), new(0, 1)];
        var props = new GpuVertexProps[positions.Length];
        for (int i = 0; i < props.Length; i++)
        {
            props[i].Normal = Vector3.UnitZ;
            props[i].TexCoord = uvs[i];
            props[i].Tangent = new Vector4(1, 0, 0, 1);
        }
        var deltas = new GpuMorphDelta[positions.Length];
        if (flatNormals)
        {
            // Lift the second corner along +Z: the face tangent (along +U) tilts to (1, 0, 1).
            deltas[1].Position = Vector3.UnitZ;
        }
        else
        {
            // Tilt the normals to (1, 0, 1) without moving the vertices.
            for (int i = 0; i < deltas.Length; i++)
            {
                deltas[i].Normal = Vector3.UnitX;
            }
        }
        GpuMorphWeight[] weights = [new() { Target = 0, Weight = 1f }];
        var flags = (flatNormals ? GpuSkinning.FlagFlatNormals : 0) | (generated ? GpuSkinning.FlagGeneratedTangents : 0);

        using var positionBuffer = CreateBuffer(ctx, positions);
        using var propsBuffer = CreateBuffer(ctx, props);
        using var deltaBuffer = CreateBuffer(ctx, deltas);
        using var weightBuffer = CreateBuffer(ctx, weights);
        using var dstPositionBuffer = CreateBuffer(ctx, new Vector4[positions.Length]);
        using var dstPropsBuffer = CreateBuffer(ctx, new GpuVertexProps[positions.Length]);
        using var verticesPipeline = CreatePipeline(GpuSkinning.Pass.Vertices);

        var cmd = ctx.AcquireCommandBuffer();
        cmd.BindComputePipeline(verticesPipeline);
        cmd.PushConstants(
            new SkinVertexPC
            {
                SrcVertexAddress = positionBuffer.Buffer.GpuAddress,
                SrcPropsAddress = propsBuffer.Buffer.GpuAddress,
                DstVertexAddress = dstPositionBuffer.Buffer.GpuAddress,
                DstPropsAddress = dstPropsBuffer.Buffer.GpuAddress,
                MorphDeltaAddress = deltaBuffer.Buffer.GpuAddress,
                MorphWeightAddress = weightBuffer.Buffer.GpuAddress,
                VertexCount = (uint)positions.Length,
                MorphWeightCount = (uint)weights.Length,
                Flags = flags,
            }
        );
        cmd.DispatchThreadGroups(new Dimensions(1, 1, 1), Dependencies.Empty);
        ctx.Wait(ctx.Submit(cmd, TextureHandle.Null));

        var outProps = Read<GpuVertexProps>(ctx, dstPropsBuffer, positions.Length);
        // Flat: face tangent of the lifted triangle. Smooth: X re-orthogonalized against
        // normalize(1, 0, 1). Authored: unchanged.
        var expected = !generated
            ? new Vector4(1, 0, 0, 1)
            : flatNormals
                ? new Vector4(Vector3.Normalize(new Vector3(1, 0, 1)), 1)
                : new Vector4(Vector3.Normalize(new Vector3(1, 0, -1)), 1);
        for (int v = 0; v < positions.Length; v++)
        {
            AssertNear(expected, outProps[v].Tangent, $"tangent {v}");
            Assert.IsTrue(
                MathF.Abs(Vector3.Dot(outProps[v].Normal, new Vector3(outProps[v].Tangent.X, outProps[v].Tangent.Y, outProps[v].Tangent.Z))) < 1e-4f
                    || !generated,
                $"tangent {v} is perpendicular to the normal"
            );
        }
    }

    private static ElementBuffer<T> CreateBuffer<T>(IContext ctx, T[] data)
        where T : unmanaged
    {
        var buffer = new ElementBuffer<T>(
            ctx,
            data.Length,
            BufferUsageBits.Storage,
            hostVisible: true
        );
        Assert.AreEqual(ResultCode.Ok, buffer.Upload(data, 0, data.Length, 0));
        return buffer;
    }

    private static T[] Read<T>(IContext ctx, ElementBuffer<T> buffer, int count)
        where T : unmanaged
    {
        var result = new T[count];
        var handle = GCHandle.Alloc(result, GCHandleType.Pinned);
        try
        {
            Assert.AreEqual(
                ResultCode.Ok,
                ctx.GetBufferSubData(
                    buffer.Buffer,
                    0,
                    (uint)(count * Marshal.SizeOf<T>()),
                    handle.AddrOfPinnedObject()
                )
            );
        }
        finally
        {
            handle.Free();
        }
        return result;
    }

    private static void AssertNear(Vector4 expected, Vector4 actual, string label)
    {
        Assert.IsTrue(
            Vector4.Distance(expected, actual) < 1e-4f,
            $"{label}: expected {expected}, actual {actual}"
        );
    }
}
