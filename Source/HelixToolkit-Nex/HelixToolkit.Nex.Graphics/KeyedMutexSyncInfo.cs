namespace HelixToolkit.Nex.Graphics;

public enum KeyedMutexSyncType
{
    None,
    D3D11SharedFence, // Synchronize with D3D11 shared fences via KMT handles
    ExternalSemaphore, // Serialize with exported Vulkan binary semaphores (Linux external-memory path)
}

public struct KeyedMutexSyncInfo
{
    public KeyedMutexSyncType SyncType;
    public ulong AcquireKey;
    public ulong AcquireSyncHandle; // KMT handle for synchronization (e.g. shared fence handle)
    public ulong ReleaseKey;
    public ulong ReleaseSyncHandle; // KMT handle for synchronization (e.g. shared fence handle)
    public uint Timeout;

    // Used only when SyncType == ExternalSemaphore (Linux). Raw VkSemaphore handles for the binary
    // semaphores that serialize the engine write against the compositor read of the shared image:
    //   WaitSemaphoreHandle   — the engine waits on this before overwriting the shared image; the
    //                           compositor signals it once it has finished reading the previous frame.
    //   SignalSemaphoreHandle — the engine signals this once it has finished writing the frame; the
    //                           compositor waits on it before reading.
    // Zero means "no wait/signal" for that slot. These are platform-neutral ulong handles (like the
    // keyed-mutex KMT handles above) so this struct carries no Vulkan type dependency.
    public ulong WaitSemaphoreHandle;
    public ulong SignalSemaphoreHandle;
}
