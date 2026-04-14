namespace HelixToolkit.Nex.Graphics;

public enum KeyedMutexSyncType
{
    None,
    D3D11SharedFence, // Synchronize with D3D11 shared fences via KMT handles
}

public struct KeyedMutexSyncInfo
{
    public KeyedMutexSyncType SyncType;
    public ulong AcquireKey;
    public ulong AcquireSyncHandle; // KMT handle for synchronization (e.g. shared fence handle)
    public ulong ReleaseKey;
    public ulong ReleaseSyncHandle; // KMT handle for synchronization (e.g. shared fence handle)
    public uint Timeout;
}
