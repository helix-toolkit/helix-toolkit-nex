using System.Diagnostics;

namespace HelixToolkit.Nex.Graphics;
/// <summary>
/// A reference counted graphics resource for holding a handle.
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class Resource<T> : IDisposable
{
    IContext? ctx = null;
    protected Handle<T> handle;


    public Resource()
    {
        handle = new Handle<T>();
    }

    public Resource(IContext ctx, in Handle<T> handle)
    {
        this.ctx = ctx;
        this.handle = handle;
    }

    public bool Valid => handle.Valid && ctx != null;

    public bool Empty => handle.Empty || ctx == null;

    public void Reset()
    {
        if (Valid)
        {
            Debug.Assert(ctx != null, "Context is null");
            OnDestroyHandle(ctx);
        }
        ctx = null;
        handle = new Handle<T>();
    }

    protected abstract void OnDestroyHandle(IContext ctx);

    public uint32_t Gen => handle.Gen;

    public uint32_t Index => handle.Index;

    public nint IndexAsVoid()
    {
        return handle.IndexAsVoid();
    }

    public static implicit operator Handle<T>(Resource<T> holder)
    {
        return holder == null ? new Handle<T>() : holder.handle;
    }

    #region Reference Counter and Disposable Pattern
    private int referenceCount = 1;

    private bool disposedValue;

    public Resource<T> AddReference()
    {
        if (!disposedValue)
        {
            Interlocked.Increment(ref referenceCount);
            return this;
        }
        else
        {
            throw new ObjectDisposedException(nameof(Resource<T>));
        }
    }

    private bool Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (Interlocked.Decrement(ref referenceCount) > 0)
            {
                return false; // Still referenced, do not dispose
            }
            if (disposing)
            {
                Reset();
            }
            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
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




