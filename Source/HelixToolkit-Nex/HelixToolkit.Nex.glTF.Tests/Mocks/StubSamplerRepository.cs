using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Repository;

namespace HelixToolkit.Nex.glTF.Tests.Mocks;

// Feature: consolidate-gltf-test-mocks (Task 3.1)
//
// Shared, reusable test double for ISamplerRepository. This consolidates the several drifted
// `private sealed class StubSamplerRepository` variants that were inlined across the test project
// into a single configurable type. The behavior of each variant is preserved exactly via the
// StubSamplerRepositoryMode selector (see the preservation oracle in
// MockVariantPreservationPropertyTests for the golden behavior each mode reproduces):
//
//   - Minimal           : Count=0, GetOrCreate=>SamplerRef.Null, Remove=>false, TryGet out=null =>false,
//                         CleanupExpired=>0, empty RepositoryStatistics, no-op Clear/Dispose.
//   - RemoveTracking    : as Minimal, but Remove(key) appends to RemovedKeys and returns true.
//   - MockContextBacked : wraps a real SamplerRepository over an initialized MockContext, delegates
//                         every member, and on Dispose disposes the inner repository then the context.
//   - Instance          : GetOrCreate(key, desc) => new SamplerRef(key, this, SamplerResource.Null);
//                         Remove=>false (use the shared static Instance field).

/// <summary>Selects the behavioral variant reproduced by <see cref="StubSamplerRepository"/>.</summary>
internal enum StubSamplerRepositoryMode
{
    /// <summary>All members return sentinels; <c>Remove</c> returns <c>false</c>.</summary>
    Minimal,

    /// <summary><c>Remove</c> records the key in <see cref="StubSamplerRepository.RemovedKeys"/> and returns <c>true</c>.</summary>
    RemoveTracking,

    /// <summary>Delegates to a real <see cref="SamplerRepository"/> over an initialized <see cref="MockContext"/>.</summary>
    MockContextBacked,

    /// <summary><c>GetOrCreate</c> creates real <see cref="SamplerRef"/> values from its key.</summary>
    Instance,
}

/// <summary>
/// Shared configurable <see cref="ISamplerRepository"/> stub. The <see cref="StubSamplerRepositoryMode"/>
/// passed to the constructor selects which inlined variant's observable behavior is reproduced.
/// </summary>
internal sealed class StubSamplerRepository : ISamplerRepository
{
    /// <summary>
    /// Shared singleton in <see cref="StubSamplerRepositoryMode.Instance"/> mode, replacing the
    /// per-file <c>static readonly StubSamplerRepository Instance</c> ref-creating variant.
    /// </summary>
    public static readonly StubSamplerRepository Instance = new(StubSamplerRepositoryMode.Instance);

    private readonly StubSamplerRepositoryMode _mode;
    private readonly MockContext? _context;
    private readonly SamplerRepository? _inner;

    /// <summary>
    /// Creates a stub in the given <paramref name="mode"/>. Defaults to
    /// <see cref="StubSamplerRepositoryMode.Minimal"/> so existing minimal call sites need no argument.
    /// In <see cref="StubSamplerRepositoryMode.MockContextBacked"/> mode an initialized
    /// <see cref="MockContext"/> and a real <see cref="SamplerRepository"/> are constructed.
    /// </summary>
    public StubSamplerRepository(StubSamplerRepositoryMode mode = StubSamplerRepositoryMode.Minimal)
    {
        _mode = mode;
        if (mode == StubSamplerRepositoryMode.MockContextBacked)
        {
            _context = new MockContext();
            _context.Initialize();
            _inner = new SamplerRepository(_context);
        }
    }

    public int Count => _inner?.Count ?? 0;

    /// <summary>Keys passed to <see cref="Remove"/> in <see cref="StubSamplerRepositoryMode.RemoveTracking"/> mode.</summary>
    public List<string> RemovedKeys { get; } = [];

    public SamplerRef GetOrCreate(string key, SamplerStateDesc desc) =>
        _mode switch
        {
            StubSamplerRepositoryMode.MockContextBacked => _inner!.GetOrCreate(key, desc),
            StubSamplerRepositoryMode.Instance => new SamplerRef(key, this, SamplerResource.Null),
            _ => SamplerRef.Null,
        };

    public bool Remove(string key)
    {
        switch (_mode)
        {
            case StubSamplerRepositoryMode.MockContextBacked:
                return _inner!.Remove(key);
            case StubSamplerRepositoryMode.RemoveTracking:
                RemovedKeys.Add(key);
                return true;
            default:
                return false;
        }
    }

    public bool TryGet(string cacheKey, out SamplerModuleCacheEntry? entry)
    {
        if (_mode == StubSamplerRepositoryMode.MockContextBacked)
        {
            return _inner!.TryGet(cacheKey, out entry);
        }

        entry = null;
        return false;
    }

    public void Clear()
    {
        if (_mode == StubSamplerRepositoryMode.MockContextBacked)
        {
            _inner!.Clear();
        }
    }

    public int CleanupExpired() =>
        _mode == StubSamplerRepositoryMode.MockContextBacked ? _inner!.CleanupExpired() : 0;

    public RepositoryStatistics GetStatistics() =>
        _mode == StubSamplerRepositoryMode.MockContextBacked
            ? _inner!.GetStatistics()
            : new()
            {
                TotalEntries = 0,
                MaxEntries = 0,
                TotalHits = 0,
                TotalMisses = 0,
            };

    public void Dispose()
    {
        if (_mode == StubSamplerRepositoryMode.MockContextBacked)
        {
            _inner!.Dispose();
            _context!.Dispose();
        }
    }
}
