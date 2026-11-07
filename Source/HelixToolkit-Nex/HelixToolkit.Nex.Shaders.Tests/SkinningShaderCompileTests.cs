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
