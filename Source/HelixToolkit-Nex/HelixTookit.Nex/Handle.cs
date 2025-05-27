namespace HelixToolkit.Nex;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Handle<T>(uint index, uint gen)
{
    private readonly uint index_ = index; // the index of this handle within a Pool  
    private readonly uint gen_ = gen; // the generation of this handle to prevent the ABA Problem  

    public bool Empty => gen_ == 0;

    public bool Valid => gen_ != 0;


    public uint Index => index_;


    public uint Gen => gen_;

    public nint IndexAsVoid()
    {
        return (nint)index_;
    }

    public static bool operator ==(Handle<T> left, Handle<T> right) // Fixed CS0558: Made static and public  
    {
        return left.index_ == right.index_ && left.gen_ == right.gen_;
    }

    public static bool operator !=(Handle<T> left, Handle<T> right) // Fixed CS0558: Made static and public  
    {
        return left.index_ != right.index_ || left.gen_ != right.gen_;
    }

    public override bool Equals(object? obj) // Added Equals override for proper equality comparison  
    {
        return obj != null && obj is Handle<T> other && this == other;
    }

    public override int GetHashCode() // Added GetHashCode override for proper hashing  
    {
        return HashCode.Combine(index_, gen_);
    }

    public static implicit operator bool(Handle<T> handle) // Fixed CS1019: Changed to explicit operator  
    {
        return handle.Valid;
    }

    public static readonly Handle<T> Null = new(0, 0); // Added a static Null handle for convenience
}
