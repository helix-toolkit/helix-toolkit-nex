using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using Microsoft.Extensions.DependencyInjection;
using BufferHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Buffer>;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Property-based tests asserting that the single-request picking path is unchanged: a lone request
/// issued in a frame (and across successive single-request frames) is accepted, assigned Request
/// Slot 0 (Byte Offset 0), recorded, and read back from its own offset exactly once.
/// </summary>
[TestClass]
public class PickingContextSingleRequestPathPropertyTests
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
    /// and instead writes <see cref="ExpectedValueForOffset(uint)"/> for the requested byte offset,
    /// letting a property assert that the single request read from Byte Offset 0 without a live GPU.
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
    /// A generated run: an entity-id texture size large enough that every coordinate is in-bounds,
    /// and one in-bounds coordinate per successive single-request frame (1..8 frames, one request
    /// each).
    /// </summary>
    public sealed record SingleRequestScenario(int Width, int Height, Vector2[] FrameCoords);

    /// <summary>Generates a run of successive single-request frames, each with one in-bounds coordinate.</summary>
    private static Arbitrary<SingleRequestScenario> Scenarios() =>
        (
            from w in Gen.Choose(1, 512)
            from h in Gen.Choose(1, 512)
            from frames in Gen.Choose(1, 8)
            from coords in Gen.ArrayOf(
                from x in Gen.Choose(0, w - 1)
                from y in Gen.Choose(0, h - 1)
                select new Vector2(x, y),
                frames
            )
            select new SingleRequestScenario(w, h, coords)
        ).ToArbitrary();

    private static RenderContext CreateRenderContext(MockContext mock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IContext>(mock);
        var provider = services.BuildServiceProvider();
        return new RenderContext(provider);
    }

    // Feature: multi-request-picking, Property 7: Single-request path is unchanged
    /// <summary>
    /// Property 7: Single-request path is unchanged.
    /// <para>
    /// For any single picking request issued in a frame (and across successive single-request
    /// frames), the request is accepted (never the sentinel), assigned Request Slot 0 (Byte Offset
    /// 0), recorded (it is the sole entry in the copied-request list), and its result is delivered
    /// from its own offset exactly once — matching pre-feature observable behavior.
    /// </para>
    /// <para>
    /// The fake <see cref="OffsetKeyedContext"/> returns a value keyed to the byte offset a read
    /// uses, so reading <see cref="ExpectedValueForOffset(uint)"/> for offset 0 proves the sole
    /// request occupied Byte Offset 0. "Exactly once" is proven by the second
    /// <see cref="PickingContext.ReadResult"/> throwing, since a delivered entry is removed.
    /// </para>
    /// **Validates: Requirements 4.5, 7.2, 7.3**
    /// </summary>
    [TestMethod]
    public void SingleRequestPath_AcceptedAtSlotZero_DeliveredExactlyOnce()
    {
        Prop.ForAll(
                Scenarios(),
                scenario =>
                {
                    using var mock = new OffsetKeyedContext();
                    mock.Initialize();
                    using var rc = CreateRenderContext(mock);

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

                    // Each frame issues exactly ONE picking request, mirroring the pre-feature
                    // single-request-per-frame usage across successive frames.
                    for (var frame = 0; frame < scenario.FrameCoords.Length; frame++)
                    {
                        var coord = scenario.FrameCoords[frame];

                        // Req 7.2/7.3: the sole request is accepted (never the sentinel).
                        var id = rc.SendPicking(coord);
                        if (id == PickingContext.InvalidRequestId)
                        {
                            return false;
                        }

                        // Assigned Request Slot 0 (Byte Offset 0) — checked before SendCommand
                        // resets the per-frame slot bookkeeping.
                        if (rc.PickingContext.GetRequestSlot(id) != 0)
                        {
                            return false;
                        }

                        // Recorded: the sole request is the only copied id this frame.
                        var frameSlot = frame % (int)GraphicsSettings.MaxFrameInFlight;
                        var copied = new FastList<uint>();
                        rc.PickingContext.SendCommand(
                            mock.AcquireCommandBuffer(),
                            rc,
                            frameSlot,
                            copied
                        );
                        if (copied.Count != 1 || copied[0] != id)
                        {
                            return false;
                        }

                        // Req 4.5 / 7.2: the result is read from the request's own Byte Offset 0.
                        var value = rc.PickingContext.ReadResult(id);
                        if (value != ExpectedValueForOffset((uint)(0 * CellSizeBytes)))
                        {
                            return false;
                        }

                        // Req 4.5: delivered exactly once — a second read has no registered entry.
                        try
                        {
                            rc.PickingContext.ReadResult(id);
                            return false; // second delivery must not succeed
                        }
                        catch (InvalidOperationException)
                        {
                            // Expected: the entry was removed after its single delivery.
                        }
                    }

                    return true;
                }
            )
            .Check(DefaultConfig);
    }
}
