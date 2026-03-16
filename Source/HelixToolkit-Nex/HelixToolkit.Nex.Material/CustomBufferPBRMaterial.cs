namespace HelixToolkit.Nex.Material;

public class CustomBufferPBRMaterial<T> : PBRMaterial
    where T : unmanaged
{
    private CustomMaterialBuffer<T>? _buffer;

    protected override bool OnCreate(IContext context, in RenderPipelineDesc pipelineDesc)
    {
        _buffer = new CustomMaterialBuffer<T>(context);
        return base.OnCreate(context, pipelineDesc);
    }

    protected override void OnDisposing()
    {
        _buffer?.Dispose();
        base.OnDisposing();
    }
}
