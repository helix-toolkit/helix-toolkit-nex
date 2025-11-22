namespace HelixToolkit.Nex.Graphics.Vulkan;

internal readonly struct DeferredTask(Action action, in SubmitHandle handle)
{
    public readonly Action Action = action;
    public readonly SubmitHandle Handle = handle;
};
