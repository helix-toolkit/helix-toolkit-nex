namespace HelixToolkit.Nex.glTF.Internal.Draco;

/// <summary>
/// Describes the result of a Draco decode operation: either success carrying a
/// <see cref="DecodedMesh"/>, or a typed failure carrying a
/// <see cref="DracoFailureReason"/> and, for attribute failures, the affected
/// semantic.
/// </summary>
/// <param name="Success">Whether decoding succeeded.</param>
/// <param name="Mesh">The decoded mesh when <paramref name="Success"/> is <see langword="true"/>; otherwise <see langword="null"/>.</param>
/// <param name="Reason">The failure reason when <paramref name="Success"/> is <see langword="false"/>; otherwise <see cref="DracoFailureReason.None"/>.</param>
/// <param name="Semantic">The affected attribute semantic for <see cref="DracoFailureReason.AttributeIdMissing"/>; otherwise <see langword="null"/>.</param>
internal readonly record struct DracoDecodeOutcome(
    bool Success,
    DecodedMesh? Mesh,
    DracoFailureReason Reason,
    string? Semantic
)
{
    /// <summary>
    /// Creates a successful outcome carrying the decoded <paramref name="mesh"/>.
    /// </summary>
    /// <param name="mesh">The decoded mesh.</param>
    /// <returns>A successful <see cref="DracoDecodeOutcome"/>.</returns>
    public static DracoDecodeOutcome Ok(DecodedMesh mesh) =>
        new(true, mesh, DracoFailureReason.None, null);

    /// <summary>
    /// Creates a failed outcome carrying the failure <paramref name="reason"/> and
    /// an optional affected <paramref name="semantic"/>.
    /// </summary>
    /// <param name="reason">The failure reason.</param>
    /// <param name="semantic">The affected attribute semantic, for <see cref="DracoFailureReason.AttributeIdMissing"/>.</param>
    /// <returns>A failed <see cref="DracoDecodeOutcome"/>.</returns>
    public static DracoDecodeOutcome Failed(DracoFailureReason reason, string? semantic = null) =>
        new(false, null, reason, semantic);
}
