using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Property-based tests for <see cref="PickingContext"/> per-frame capacity reset behavior.
/// </summary>
[TestClass]
public class PickingContextCapacityResetPropertyTests
{
    private static readonly Config DefaultConfig = Config.Default.WithMaxTest(200);

    /// <summary>The compile-time per-frame picking capacity (number of Request Slots).</summary>
    private const int MaxRequests = (int)GraphicsSettings.MaxRequestsPerFrame;

    /// <summary>
    /// Builds a <see cref="RenderContext"/> backed by a <see cref="MockContext"/>. Its
    /// <see cref="RenderContext.ResourceSet"/> has no entity-id texture, so
    /// <see cref="PickingContext.SendCommand"/> takes the missing-texture branch that still clears
    /// the pending set and resets the per-frame capacity — exactly the record-completion behavior
    /// this property exercises.
    /// </summary>
    private static RenderContext CreateRenderContext()
    {
        var mockContext = new MockContext();
        mockContext.Initialize();
        var services = new ServiceCollection();
        services.AddSingleton<IContext>(mockContext);
        var provider = services.BuildServiceProvider();
        return new RenderContext(provider);
    }

    /// <summary>An arbitrary in-bounds-looking screen coordinate (acceptance ignores the value).</summary>
    private static Gen<Vector2> CoordGen() =>
        from x in Gen.Choose(0, 1000)
        from y in Gen.Choose(0, 1000)
        select new Vector2(x, y);

    /// <summary>
    /// Generates a sequence of 1..8 frames, each with a per-frame request count in
    /// <c>[0, MaxRequestsPerFrame]</c> (covering empty, partial, and completely full frames).
    /// </summary>
    private static Arbitrary<int[]> FrameFillSequences() =>
        Gen.Choose(1, 8)
            .SelectMany(frames => Gen.ArrayOf(Gen.Choose(0, MaxRequests), frames))
            .ToArbitrary();

    // Feature: multi-request-picking, Property 4: Per-frame capacity resets each frame
    /// <summary>
    /// Property 4: Per-frame capacity resets each frame.
    /// <para>
    /// For any sequence of frames, after <see cref="PickingContext.SendCommand"/> completes a
    /// frame's recording the accepted-request count returns to zero, so a completely full frame
    /// does not reduce the number of Request Slots (<see cref="GraphicsSettings.MaxRequestsPerFrame"/>)
    /// available in the next frame: the next frame can again accept a full
    /// <c>MaxRequestsPerFrame</c> requests, and only the request beyond that limit is rejected.
    /// </para>
    /// **Validates: Requirements 6.1, 6.2**
    /// </summary>
    [TestMethod]
    public void SendCommand_ResetsPerFrameCapacity_NextFrameAcceptsFullCapacity()
    {
        Prop.ForAll(
                FrameFillSequences(),
                CoordGen().ToArbitrary(),
                (int[] frameFills, Vector2 coord) =>
                {
                    using var renderContext = CreateRenderContext();
                    var picking = renderContext.PickingContext;

                    for (var frame = 0; frame < frameFills.Length; frame++)
                    {
                        var commandBuffer = renderContext.Context.AcquireCommandBuffer();
                        var frameSlot = frame % (int)GraphicsSettings.MaxFrameInFlight;

                        // Fill the current frame with a random number of accepted requests.
                        var fill = frameFills[frame];
                        for (var i = 0; i < fill; i++)
                        {
                            // Req 6.1: every request within capacity is accepted this frame.
                            if (picking.SetPendingSubmit(coord) == PickingContext.InvalidRequestId)
                            {
                                return false;
                            }
                        }

                        // Recording completes the frame: pending coords cleared, capacity reset.
                        picking.SendCommand(commandBuffer, renderContext, frameSlot, new FastList<uint>());

                        // Req 6.1 / 6.2: the next frame must again offer a full MaxRequestsPerFrame
                        // Request Slots, regardless of how full the previous frame was.
                        var nextSlot = (frame + 1) % (int)GraphicsSettings.MaxFrameInFlight;
                        var nextBuffer = renderContext.Context.AcquireCommandBuffer();
                        var acceptedIds = new List<uint>(MaxRequests);
                        for (var i = 0; i < MaxRequests; i++)
                        {
                            var id = picking.SetPendingSubmit(coord);
                            if (id == PickingContext.InvalidRequestId)
                            {
                                return false; // capacity was not fully restored
                            }
                            // Slots must restart at 0 for the fresh frame.
                            if (picking.GetRequestSlot(id) != i)
                            {
                                return false;
                            }
                            acceptedIds.Add(id);
                        }

                        // The request beyond MaxRequestsPerFrame is rejected with the sentinel.
                        if (picking.SetPendingSubmit(coord) != PickingContext.InvalidRequestId)
                        {
                            return false;
                        }

                        // All MaxRequestsPerFrame ids in this probe frame are unique.
                        if (acceptedIds.Distinct().Count() != acceptedIds.Count)
                        {
                            return false;
                        }

                        // Reset again so the loop's next iteration starts from a clean frame.
                        picking.SendCommand(nextBuffer, renderContext, nextSlot, new FastList<uint>());
                    }

                    return true;
                }
            )
            .Check(DefaultConfig);
    }
}
