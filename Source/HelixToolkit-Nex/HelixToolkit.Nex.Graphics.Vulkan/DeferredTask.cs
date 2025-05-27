namespace HelixToolkit.Nex.Graphics.Vulkan;

internal readonly struct DeferredTask(Action action, in SubmitHandle handle)
{
    public readonly Action action = action;
    public readonly SubmitHandle handle = handle;
};