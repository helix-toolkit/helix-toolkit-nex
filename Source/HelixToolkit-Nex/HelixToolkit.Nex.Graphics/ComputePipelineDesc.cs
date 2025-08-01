namespace HelixToolkit.Nex.Graphics;

public struct ComputePipelineDesc()
{
    public ShaderModuleHandle ComputeShader;
    public SpecializationConstantDesc SpecInfo;
    public string EntryPoint = "main";
    public string DebugName = string.Empty;
}
