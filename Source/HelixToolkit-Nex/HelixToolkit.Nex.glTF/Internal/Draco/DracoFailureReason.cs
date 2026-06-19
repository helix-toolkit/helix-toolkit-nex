namespace HelixToolkit.Nex.glTF.Internal.Draco;

/// <summary>
/// Identifies why a Draco decode operation failed.
/// </summary>
internal enum DracoFailureReason
{
    /// <summary>
    /// No failure; used for successful outcomes.
    /// </summary>
    None,

    /// <summary>
    /// The Draco bitstream could not be decoded because it is corrupt or
    /// unsupported (Requirement 2.7).
    /// </summary>
    BitstreamDecodeFailed,

    /// <summary>
    /// A Draco attribute id mapped by the extension's <c>attributes</c> map is
    /// absent from the decoded bitstream (Requirement 2.5).
    /// </summary>
    AttributeIdMissing,
}
