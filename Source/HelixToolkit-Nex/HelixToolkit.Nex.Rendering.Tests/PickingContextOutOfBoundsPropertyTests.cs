using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TextureHandle = HelixToolkit.Nex.Handle<HelixToolkit.Nex.Graphics.Texture>;

namespace HelixToolkit.Nex.Rendering.Tests;

/// <summary>
/// Property-based tests for <see cref="PickingContext.SendCommand"/> handling of out-of-bounds
/// picking coordinates and a missing/empty entity-id texture.
/// </summary>
[TestClass]
public class PickingContextOutOfBoundsPropertyTests
{
    private static readonly Config DefaultConfig = Config.Default.WithMaxTest(200);

    /// <summary>How the frame's entity-id texture is provisioned for a generated scenario.</summary>
    public enum EntityTextureMode
    {
        /// <summary>A real texture of the generated dimensions is registered.</summary>
        Present,

        /// <summary>No entity-id texture key is present in the resource set.</summary>
        Missing,

        /// <summary>The entity-id key is present but maps to an empty (null) texture handle.</summary>
        Empty,
    }

    /// <summary>
    /// A generated frame: the entity-id texture provisioning mode, the texture dimensions used when
    /// <see cref="EntityTextureMode.Present"/>, and the sequence of picking requests submitted before
    /// recording. Each request carries whether its coordinate lies inside the texture bounds.
    /// </summary>
    public sealed record PickScenario(
        EntityTextureMode Mode,
        int Width,
        int Height,
        (Vector2 Coord, bool InBounds)[] Requests
    );

    /// <summary>Generates a coordinate strictly inside the <paramref name="w"/>×<paramref name="h"/> texture.</summary>
    private static Gen<Vector2> InBoundsCoordGen(int w, int h) =>
        from x in Gen.Choose(0, w - 1)
        from y in Gen.Choose(0, h - 1)
        select new Vector2(x, y);

    /// <summary>
    /// Generates a coordinate that is NOT recorded by <see cref="PickingContext.SendCommand"/>:
    /// either past the width, past the height, or negative (which also fails the validity check).
    /// </summary>
    private static Gen<Vector2> OutOfBoundsCoordGen(int w, int h) =>
        from cat in Gen.Choose(0, 2)
        from a in Gen.Choose(0, 64)
        from b in Gen.Choose(0, 64)
        select cat switch
        {
            0 => new Vector2(w + a, b), // x >= width
            1 => new Vector2(b, h + a), // y >= height
            _ => new Vector2(-1 - a, b), // x < 0 (invalid coordinate)
        };

    /// <summary>Generates one picking request tagged with whether its coordinate is in-bounds.</summary>
    private static Gen<(Vector2 Coord, bool InBounds)> RequestGen(int w, int h) =>
        from flag in Gen.Choose(0, 1)
        let inBounds = flag == 1
        from coord in inBounds ? InBoundsCoordGen(w, h) : OutOfBoundsCoordGen(w, h)
        select (coord, inBounds);

    /// <summary>
    /// Generates a full frame scenario: a texture mode, texture dimensions, and up to
    /// <see cref="GraphicsSettings.MaxRequestsPerFrame"/> requests mixing in-bounds and
    /// out-of-bounds coordinates.
    /// </summary>
    private static Arbitrary<PickScenario> Scenarios() =>
        (
            from modeSel in Gen.Choose(0, 2)
            from w in Gen.Choose(1, 512)
            from h in Gen.Choose(1, 512)
            from n in Gen.Choose(0, (int)GraphicsSettings.MaxRequestsPerFrame)
            from reqs in Gen.ArrayOf(RequestGen(w, h), n)
            select new PickScenario(
                modeSel switch
                {
                    0 => EntityTextureMode.Present,
                    1 => EntityTextureMode.Missing,
                    _ => EntityTextureMode.Empty,
                },
                w,
                h,
                reqs
            )
        ).ToArbitrary();

    private static RenderContext CreateRenderContext(MockContext mock)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IContext>(mock);
        var provider = services.BuildServiceProvider();
        return new RenderContext(provider);
    }

    // Feature: multi-request-picking, Property 6: Out-of-bounds and missing-texture requests deliver no result
    /// <summary>
    /// Property 6: Out-of-bounds and missing-texture requests deliver no result.
    /// <para>
    /// For any frame, requests whose coordinate is outside the entity-id texture dimensions have no
    /// copy recorded and no readback registered; and if the entity-id texture is missing or empty,
    /// no copies are recorded for any of the frame's accepted requests. The list of copied Request
    /// Ids returned by <see cref="PickingContext.SendCommand"/> is the observable proof: it is empty
    /// for a missing/empty texture, and contains exactly the in-bounds requests otherwise.
    /// </para>
    /// **Validates: Requirements 4.2, 4.3**
    /// </summary>
    [TestMethod]
    public void OutOfBoundsAndMissingTexture_DeliverNoResult()
    {
        Prop.ForAll(
                Scenarios(),
                scenario =>
                {
                    using var mock = new MockContext();
                    mock.Initialize();
                    using var rc = CreateRenderContext(mock);

                    // Submit every request. All are within per-frame capacity, so none is rejected.
                    var submittedIds = new List<uint>(scenario.Requests.Length);
                    foreach (var (coord, _) in scenario.Requests)
                    {
                        var id = rc.SendPicking(coord);
                        if (id == PickingContext.InvalidRequestId)
                        {
                            return false;
                        }
                        submittedIds.Add(id);
                    }

                    // Provision the entity-id texture according to the scenario mode.
                    switch (scenario.Mode)
                    {
                        case EntityTextureMode.Present:
                            var tex = ((IContext)mock).CreateTexture2D(
                                Format.RG_F32,
                                (uint)scenario.Width,
                                (uint)scenario.Height,
                                TextureUsageBits.Attachment | TextureUsageBits.Sampled,
                                StorageType.Device,
                                debugName: SystemBufferNames.TextureEntityId
                            );
                            rc.ResourceSet.Textures[SystemBufferNames.TextureEntityId] = tex;
                            break;
                        case EntityTextureMode.Empty:
                            rc.ResourceSet.Textures[SystemBufferNames.TextureEntityId] =
                                TextureHandle.Null;
                            break;
                        case EntityTextureMode.Missing:
                            // Leave the entity-id texture key absent from the resource set.
                            break;
                    }

                    var copied = rc.PickingContext.SendCommand(
                        mock.AcquireCommandBuffer(),
                        rc,
                        frameSlot: 0
                    );

                    if (scenario.Mode != EntityTextureMode.Present)
                    {
                        // Req 4.3: missing/empty entity-id texture records no copies for the frame.
                        return copied.Count == 0;
                    }

                    // Req 4.2: only in-bounds requests are copied; out-of-bounds ones are skipped
                    // (no copy, no readback). Copies are recorded in submission (slot) order.
                    var expected = new List<uint>();
                    for (var i = 0; i < scenario.Requests.Length; i++)
                    {
                        if (scenario.Requests[i].InBounds)
                        {
                            expected.Add(submittedIds[i]);
                        }
                    }
                    return copied.SequenceEqual(expected);
                }
            )
            .Check(DefaultConfig);
    }
}
