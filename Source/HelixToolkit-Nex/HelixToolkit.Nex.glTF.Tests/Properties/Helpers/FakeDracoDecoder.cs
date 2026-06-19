using System.Threading;
using HelixToolkit.Nex.glTF.Internal.Draco;

namespace HelixToolkit.Nex.glTF.Tests.Properties.Helpers;

/// <summary>
/// A deterministic, in-memory <see cref="IDracoDecoder"/> for converter-level property and unit
/// tests. It never touches the native Draco library; instead it returns a caller-supplied
/// <see cref="DracoDecodeOutcome"/> (a success carrying an FsCheck-generated <see cref="DecodedMesh"/>,
/// or a typed failure outcome) and records how it was invoked.
/// </summary>
/// <remarks>
/// <para>Construction:</para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="FakeDracoDecoder(DracoDecodeOutcome, bool)"/> — always return the same fixed outcome.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="FakeDracoDecoder(Func{byte[], IReadOnlyDictionary{string, int}, DracoDecodeOutcome}, bool)"/>
/// — compute the outcome from the (copied) compressed bytes and attribute map on each call, which
/// lets a property test derive the <see cref="DecodedMesh"/> from the generated extension input.
/// </description>
/// </item>
/// </list>
/// <para>Observation:</para>
/// <list type="bullet">
/// <item><description><see cref="InvocationCount"/> — number of times <see cref="Decode"/> ran (thread-safe).</description></item>
/// <item><description><see cref="LastThreadId"/> — the managed thread id of the most recent <see cref="Decode"/> call, or -1 if never called. Used to assert async offload (Requirement 7.2).</description></item>
/// <item><description><see cref="LastCompressed"/> / <see cref="LastAttributeMap"/> — a copy of the arguments from the most recent call.</description></item>
/// </list>
/// </remarks>
internal sealed class FakeDracoDecoder : IDracoDecoder
{
    private readonly Func<byte[], IReadOnlyDictionary<string, int>, DracoDecodeOutcome> _decode;
    private int _invocationCount;
    private int _lastThreadId = -1;

    /// <summary>
    /// Initializes a fake decoder that always returns the same fixed <paramref name="outcome"/>.
    /// </summary>
    /// <param name="outcome">The outcome to return from every <see cref="Decode"/> call.</param>
    /// <param name="isAvailable">The value reported by <see cref="IsAvailable"/>; defaults to <see langword="true"/>.</param>
    public FakeDracoDecoder(DracoDecodeOutcome outcome, bool isAvailable = true)
        : this((_, _) => outcome, isAvailable) { }

    /// <summary>
    /// Initializes a fake decoder that computes its outcome per call from the compressed bytes and
    /// attribute map.
    /// </summary>
    /// <param name="decode">
    /// A pure function mapping (copied compressed bytes, attribute map) to the outcome to return.
    /// </param>
    /// <param name="isAvailable">The value reported by <see cref="IsAvailable"/>; defaults to <see langword="true"/>.</param>
    public FakeDracoDecoder(
        Func<byte[], IReadOnlyDictionary<string, int>, DracoDecodeOutcome> decode,
        bool isAvailable = true
    )
    {
        _decode = decode ?? throw new ArgumentNullException(nameof(decode));
        IsAvailable = isAvailable;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the (fake) native library is available. Set this to
    /// <see langword="false"/> to exercise the decoder-unavailable fallback path (Requirement 6).
    /// </summary>
    public bool IsAvailable { get; set; }

    /// <summary>Gets the number of times <see cref="Decode"/> has been invoked.</summary>
    public int InvocationCount => Volatile.Read(ref _invocationCount);

    /// <summary>
    /// Gets the managed thread id of the most recent <see cref="Decode"/> call, or -1 if it has
    /// never been called.
    /// </summary>
    public int LastThreadId => Volatile.Read(ref _lastThreadId);

    /// <summary>Gets a copy of the compressed bytes passed to the most recent <see cref="Decode"/> call.</summary>
    public byte[]? LastCompressed { get; private set; }

    /// <summary>Gets the attribute map passed to the most recent <see cref="Decode"/> call.</summary>
    public IReadOnlyDictionary<string, int>? LastAttributeMap { get; private set; }

    /// <inheritdoc />
    public DracoDecodeOutcome Decode(
        ReadOnlySpan<byte> compressed,
        IReadOnlyDictionary<string, int> attributeMap
    )
    {
        Interlocked.Increment(ref _invocationCount);
        Volatile.Write(ref _lastThreadId, Environment.CurrentManagedThreadId);

        // Copy out of the span so it can be observed and captured after the call returns.
        byte[] copy = compressed.ToArray();
        LastCompressed = copy;
        LastAttributeMap = attributeMap;

        return _decode(copy, attributeMap);
    }
}
