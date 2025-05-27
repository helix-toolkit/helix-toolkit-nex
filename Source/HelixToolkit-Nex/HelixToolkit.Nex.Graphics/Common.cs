using System.Diagnostics;

namespace HelixToolkit.Nex.Graphics;

public abstract class Holder<T> : IDisposable
{
    IContext? ctx_ = null;
    protected Handle<T> handle_;
    private bool disposedValue;

    public Holder()
    {
        handle_ = new Handle<T>();
    }

    public Holder(IContext ctx, in Handle<T> handle)
    {
        ctx_ = ctx;
        handle_ = handle;
    }

    public bool Valid => handle_.Valid;

    public bool Empty => handle_.Empty;

    public void Reset()
    {
        if (Valid)
        {
            Debug.Assert(ctx_ != null, "Context is null");
            OnDestroyHandle(ctx_);
        }
        ctx_ = null;
        handle_ = new Handle<T>();
    }

    protected abstract void OnDestroyHandle(IContext ctx);

    public uint32_t Gen => handle_.Gen;

    public uint32_t Index => handle_.Index;

    public nint IndexAsVoid()
    {
        return handle_.IndexAsVoid();
    }

    public static implicit operator Handle<T>(Holder<T> holder)
    {
        return holder == null ? new Handle<T>() : holder.handle_;
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            Reset();
            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~Holder()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
    }
}




