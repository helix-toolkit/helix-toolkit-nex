using System.Diagnostics;

namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// A reference counted graphics resource for holding a handle.
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class Resource<T> : IDisposable
{
    private IContext? _ctx = null;
    protected Handle<T> _handle;

    public Resource()
    {
        _handle = new Handle<T>();
    }

    public Resource(IContext ctx, in Handle<T> handle)
    {
        _ctx = ctx;
        _handle = handle;
    }

    public bool Valid => _handle.Valid && _ctx != null;

    public bool Empty => _handle.Empty || _ctx == null;

    public void Reset()
    {
        if (Valid)
        {
            Debug.Assert(_ctx != null, "Context is null");
            OnDestroyHandle(_ctx);
        }
        _ctx = null;
        _handle = new Handle<T>();
    }

    protected abstract void OnDestroyHandle(IContext ctx);

    public uint32_t Gen => _handle.Gen;

    public uint32_t Index => _handle.Index;

    public nint IndexAsVoid()
    {
        return _handle.IndexAsVoid();
    }

    public static implicit operator Handle<T>(Resource<T> holder)
    {
        return holder == null ? new Handle<T>() : holder._handle;
    }

    #region Reference Counter and Disposable Pattern
    private int _referenceCount = 1;

    private bool _disposedValue;

    public Resource<T> AddReference()
    {
        if (!_disposedValue)
        {
            Interlocked.Increment(ref _referenceCount);
            return this;
        }
        else
        {
            throw new ObjectDisposedException(nameof(Resource<T>));
        }
    }

    private bool Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (Interlocked.Decrement(ref _referenceCount) > 0)
            {
                return false; // Still referenced, do not dispose
            }
            if (disposing)
            {
                Reset();
            }
            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
            return true;
        }
        return false;
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~Resource()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        if (Dispose(disposing: true))
        {
            GC.SuppressFinalize(this);
        }
    }
    #endregion
}
