namespace HelixToolkit.Nex.ECS;

[Flags]
internal enum State : uint
{
    None = 0,
    Valid = 1,
    Enabled = 1 << 1,
    All = 3,
}
