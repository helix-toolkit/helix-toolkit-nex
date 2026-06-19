using HelixToolkit.Nex.glTF.Internal.Draco;
using HelixToolkit.Nex.glTF.Tests.Properties.Helpers;

namespace HelixToolkit.Nex.glTF.Tests;

/// <summary>
/// Shared defaults for constructing <c>MeshConverter</c> instances in tests that do not exercise
/// the Draco path. The decoder is reported as unavailable and returns a failure outcome, so for
/// non-Draco primitives it is never invoked and behavior matches the pre-Draco converter exactly.
/// </summary>
internal static class MeshConverterTestDefaults
{
    /// <summary>Gets a fresh default importer configuration.</summary>
    public static ImporterConfig Config => ImporterConfig.Default;

    /// <summary>
    /// Gets a fresh, unavailable fake Draco decoder. Because it reports
    /// <see cref="IDracoDecoder.IsAvailable"/> as <see langword="false"/> and only declaring
    /// primitives route through it, non-Draco tests never invoke it.
    /// </summary>
    public static IDracoDecoder Decoder =>
        new FakeDracoDecoder(
            DracoDecodeOutcome.Failed(DracoFailureReason.BitstreamDecodeFailed),
            isAvailable: false
        );
}
