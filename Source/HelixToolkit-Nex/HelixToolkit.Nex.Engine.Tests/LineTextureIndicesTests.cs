using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine.Data;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Rendering.Components;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Regression tests for bindless texture and sampler indices on rendered lines.
/// <para>
/// The line shaders and generated <c>LineDraw</c> structure already expose texture and sampler
/// fields, but the public <see cref="LineDrawInfo"/> and <see cref="LineNode"/> interfaces
/// previously offered no way to configure them. Consequently, the GPU draw record always received
/// zero for both indices.
/// </para>
/// <para>
/// These tests verify the complete configuration path from the public scene-node interface,
/// through the ECS component, into the CPU-side draw record that is uploaded to the GPU.
/// </para>
/// </summary>
[TestClass]
public sealed class LineTextureIndicesTests
{
    /// <summary>
    /// Verifies that non-zero texture and sampler indices configured through
    /// <see cref="LineNode"/> are preserved by its underlying <see cref="LineDrawInfo"/> and copied
    /// to the corresponding fields of the generated <c>LineDraw</c>.
    /// <para>
    /// The test uses a real <see cref="LineDrawStream"/> backed by the mock graphics context so it
    /// exercises the same draw-record construction and upload preparation path as rendering,
    /// without requiring a physical GPU.
    /// </para>
    /// </summary>
    [TestMethod]
    public void LineNode_TextureIndices_ArePropagatedToGpuDrawData()
    {
        const uint textureIndex = 17;
        const uint samplerIndex = 29;

        using var context = new MockContext();
        Assert.AreEqual(ResultCode.Ok, context.Initialize());

        using World world = World.CreateWorld();
        using var geometryManager = new GeometryManager(context);
        using var drawStream = new LineDrawStream(
            context,
            world,
            DrawStreamType.Line,
            DrawStreamName.StaticHitable
        );

        var geometry = new Geometry(
            [new Vector4(0, 0, 0, 1), new Vector4(1, 0, 0, 1)],
            Topology.Line
        );
        Assert.IsTrue(geometryManager.Add(geometry).Valid);

        var node = new LineNode(world, "TexturedLine")
        {
            Geometry = geometry,
            TextureIndex = textureIndex,
            SamplerIndex = samplerIndex,
        };

        var component = node.Entity.Get<LineDrawInfo>();
        Assert.AreEqual(textureIndex, component.TextureIndex);
        Assert.AreEqual(samplerIndex, component.SamplerIndex);

        Assert.AreEqual(ResultCode.Ok, drawStream.Initialize());
        drawStream.EntityAdded(node.Entity);
        Assert.IsTrue(drawStream.Update());

        var (draw, slotIndex) = drawStream.GetDraw(node.Entity);
        Assert.IsTrue(slotIndex >= 0);
        Assert.AreEqual(textureIndex, draw.TextureId);
        Assert.AreEqual(samplerIndex, draw.SamplerId);
    }
}
