namespace HelixToolkit.Nex.Trace;

/// <summary>
/// Configuration settings for tracer instances.
/// </summary>
public class TracerConfiguration
{
    /// <summary>
    /// Gets or sets whether tracing is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the initial capacity for trace entries.
    /// </summary>
    public int InitialCapacity { get; set; } = 1024;

    /// <summary>
    /// Gets or sets the maximum number of trace entries to keep.
    /// 0 means unlimited.
    /// </summary>
    public int MaxEntries { get; set; } = 0;

    /// <summary>
    /// Gets or sets whether to auto-flush trace entries when the max is reached.
    /// If false, oldest entries are removed when max is reached.
    /// </summary>
    public bool AutoFlush { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to include thread IDs in trace entries.
    /// </summary>
    public bool IncludeThreadId { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to use high-resolution timestamps.
    /// </summary>
    public bool UseHighResolutionTimestamps { get; set; } = true;

    /// <summary>
    /// Creates a copy of this configuration.
    /// </summary>
    public TracerConfiguration Clone()
    {
        return new TracerConfiguration
        {
            Enabled = Enabled,
            InitialCapacity = InitialCapacity,
            MaxEntries = MaxEntries,
            AutoFlush = AutoFlush,
            IncludeThreadId = IncludeThreadId,
            UseHighResolutionTimestamps = UseHighResolutionTimestamps,
        };
    }
}
