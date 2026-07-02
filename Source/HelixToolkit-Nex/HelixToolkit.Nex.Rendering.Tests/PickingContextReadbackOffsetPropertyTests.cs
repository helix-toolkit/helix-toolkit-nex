using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using Microsoft.Extensions.DependencyInjection;
using BufferHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Buffer>;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Property-based tests for <see cref="PickingContext.ReadResult"/> resolving each accepted
/// request to its own recorded <c>(Frame Slot, Byte Offset)</c>.
/// </summary>
[TestClass]
public class PickingContextReadbackOffsetPropertyTests
{
    private static readonly Config DefaultConfig = Config.Default.WithMaxTest(200);

    /// <summary>The number of bytes each accepted request occupies in its staging buffer (one RG_F32 pixel).</summary>
    private const int CellSizeBytes = sizeof(ulong);

    /// <summary>
    /// Deterministic canned value that <see cref="OffsetKeyedContext"/> returns for a readback at
    /// <paramref name="byteOffset"/>. It is a pure function of the requested byte offset, so the
    /// value a request reads back uniquely identifies the exact offset the read used. A distinctive
    /// high marker keeps it from colliding with small offset values.
    /// </summary>
    private static ulong ExpectedValueForOffset(uint byteOffset) =>
        0xF00D_0000_0000_0000UL | byteOffset;

    /// <summary>
    /// A fake <see cref="IContext"/> whose <see cref="GetBufferSubData"/> ignores the buffer contents
    /// and instead writes <see cref="ExpectedValueForOffset(uint)"/> for the requested byte offset.
    /// This lets a property assert that <see cref="PickingContext.ReadResult"/> read from a request's
    /// own recorded byte offset (never another request's cell), without a live GPU: the value read
    /// back is a pure function of the offset the read used.
    /// </summary>
    private sealed class OffsetKeyedContext : MockContext
    {
        public override ResultCode GetBufferSubData(
            in BufferHandle handle,
            uint offset,
            uint size,
            nint data
        )
        {
            if (data == nint.Zero || size < sizeof(ulong))
            {
                return ResultCode.ArgumentError;
            }
            System.Runtime.InteropServices.Marshal.WriteInt64(
                data,
                unchecked((long)ExpectedValueForOffset(offset))
            );
            return ResultCode.Ok;
        }
    }

    /// <summary>
    /// A generated frame: the entity-id texture dimensions (large enough that every generated
    /// coordinate is in-bounds) and a sequence of in-bounds picking requests recorded in that frame,
    /// plus the Frame Slot the frame records into.
    /// </summary>
    public sealed record ReadbackScenario(int Width, int Height, Vector2[] Coords, int FrameSlot);

    /// <summary>Generates a full readback scenario: texture size, in-bounds coordinates, and a Frame Slot.</summary>
    private static Arbitrary<ReadbackScenario> Scenarios() =>
        (
            from w in Gen.Choose(1, 512)
            from h in Gen.Choose(1, 512)
            from n in Gen.Choose(0, (int)GraphicsSettings.MaxRequestsPerFrame)
            from coords in Gen.ArrayOf(
                from x in Gen.Choose(0, w - 1)
                from y in Gen.Choose(0, h - 1)
                select new Vector2(x, y),
                n
            )
            from frameSlot in Gen.Choose(0, (int)GraphicsSettings.MaxFrameInFlight - 1)
            select new ReadbackScenario(w, h, coords, frameSlot)
        ).ToArbitrary();

    private static RenderContext CreateRenderContext(MockContext mock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IContext>(mock);
        var provider = services.BuildServiceProvider();
        return new RenderContext(provider);
    }

    // Feature: multi-request-picking, Property 2: Readback reads each request's own offset
    /// <summary>
    /// Property 2: Readback reads each request's own offset.
    /// <para>
    /// For any set of accepted, in-bounds requests recorded in a frame,
    /// <see cref="PickingContext.ReadResult"/> resolves to the same <c>(Frame Slot, Byte Offset)</c>
    /// that <see cref="PickingContext.SendCommand"/> used to record that request's copy — so each
    /// request reads exactly the 8 bytes written for it and never another request's cell.
    /// </para>
    /// <para>
    /// The fake <see cref="OffsetKeyedContext"/> returns a value that is a pure function of the byte
    /// offset a read uses. Because the requests are recorded in submission (slot) order, request
    /// <c>i</c> owns Byte Offset <c>i * sizeof(ulong)</c>; the property asserts that
    /// <c>ReadResult(id_i)</c> returns exactly the value keyed to that offset. Any swap between
    /// requests' cells would return a different offset's value and fail.
    /// </para>
    /// **Validates: Requirements 2.3, 3.1, 3.2**
    /// </summary>
    [TestMethod]
    public void ReadResult_ReadsEachRequestsOwnByteOffset()
    {
        Prop.ForAll(
                Scenarios(),
                scenario =>
                {
                    using var mock = new OffsetKeyedContext();
                    mock.Initialize();
                    using var rc = CreateRenderContext(mock);

                    // Submit every request. All are within per-frame capacity, so none is rejected.
                    var submittedIds = new List<uint>(scenario.Coords.Length);
                    foreach (var coord in scenario.Coords)
                    {
                        var id = rc.SendPicking(coord);
                        if (id == PickingContext.InvalidRequestId)
                        {
                            return false;
                        }
                        submittedIds.Add(id);
                    }

                    // Provision an entity-id texture big enough that every coordinate is in-bounds.
                    var tex = ((IContext)mock).CreateTexture2D(
                        Format.RG_F32,
                        (uint)scenario.Width,
                        (uint)scenario.Height,
                        TextureUsageBits.Attachment | TextureUsageBits.Sampled,
                        StorageType.Device,
                        debugName: SystemBufferNames.TextureEntityId
                    );
                    rc.ResourceSet.Textures[SystemBufferNames.TextureEntityId] = tex;

                    // Record the copies into the generated Frame Slot's staging buffer.
                    var copied = rc.PickingContext.SendCommand(
                        mock.AcquireCommandBuffer(),
                        rc,
                        scenario.FrameSlot
                    );

                    // Every in-bounds request is recorded, in submission (slot) order.
                    if (!copied.SequenceEqual(submittedIds))
                    {
                        return false;
                    }

                    // Req 2.3 / 3.1 / 3.2: each request reads exactly the 8 bytes at its own Byte
                    // Offset (RequestSlot * sizeof(ulong)), never another request's cell.
                    for (var i = 0; i < copied.Count; i++)
                    {
                        var expectedOffset = (uint)(i * CellSizeBytes);
                        var value = rc.PickingContext.ReadResult(copied[i]);
                        if (value != ExpectedValueForOffset(expectedOffset))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            )
            .Check(DefaultConfig);
    }
}
