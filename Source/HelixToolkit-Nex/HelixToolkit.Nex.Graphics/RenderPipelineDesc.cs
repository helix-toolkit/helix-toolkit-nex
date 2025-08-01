namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Specialization constant entry. Used to define each constant entry. More information can be found in the Vulkan specification.
/// <see href="https://docs.vulkan.org/samples/latest/samples/performance/specialization_constants/README.html"/>
/// </summary>
public struct SpecializationConstantEntry()
{
    public uint32_t ConstantId;
    public uint32_t Offset; // offset within ShaderSpecializationConstantDesc::data
    public size_t Size;
};
/// <summary>
/// Specialization constant description. This structure is used to pass specialization data to the shader.
/// <see href="https://docs.vulkan.org/samples/latest/samples/performance/specialization_constants/README.html"/>
/// </summary>
public struct SpecializationConstantDesc()
{
    public const uint8_t SPECIALIZATION_CONSTANTS_MAX = 16;

    public readonly SpecializationConstantEntry[] Entries = new SpecializationConstantEntry[SPECIALIZATION_CONSTANTS_MAX];

    public byte[] Data = [];

    public readonly uint32_t NumSpecializationConstants()
    {
        for (uint32_t i = 0; i < SPECIALIZATION_CONSTANTS_MAX; i++)
        {
            if (Entries[i].Size == 0)
                return i;
        }
        return SPECIALIZATION_CONSTANTS_MAX;
    }
};
/// <summary>
/// Represents the configuration and state for a render pipeline in a graphics application.
/// </summary>
/// <remarks>This structure encapsulates various settings and resources required to define a render pipeline,
/// including shader modules, vertex input, and rendering states such as topology, culling, and polygon modes. It also
/// includes settings for color and depth-stencil attachments, as well as multisampling parameters. The default values
/// are set to common defaults, but can be customized to fit specific rendering needs.</remarks>
public struct RenderPipelineDesc
{
    public Topology Topology = Topology.Triangle;

    public VertexInput VertexInput = VertexInput.Null;

    public ShaderModuleHandle VertexShader = ShaderModuleHandle.Null;
    public ShaderModuleHandle TessControlShader = ShaderModuleHandle.Null;
    public ShaderModuleHandle TessEvalShader = ShaderModuleHandle.Null;
    public ShaderModuleHandle GeometryShader = ShaderModuleHandle.Null;
    public ShaderModuleHandle TaskShader = ShaderModuleHandle.Null;
    public ShaderModuleHandle MeshShader = ShaderModuleHandle.Null;
    public ShaderModuleHandle FragementShader = ShaderModuleHandle.Null;

    public SpecializationConstantDesc SpecInfo = new();

    public string EntryPointVert = "main";
    public string EntryPointTesc = "main";
    public string EntryPointTese = "main";
    public string EntryPointGeom = "main";
    public string EntryPointTask = "main";
    public string EntryPointMesh = "main";
    public string EntryPointFrag = "main";

    public readonly ColorAttachment[] Colors = new ColorAttachment[Constants.MAX_COLOR_ATTACHMENTS];
    public Format DepthFormat = Format.Invalid;
    public Format StencilFormat = Format.Invalid;

    public CullMode CullMode = CullMode.Back;
    public WindingMode FrontFaceWinding = WindingMode.CCW;
    public PolygonMode PolygonMode = PolygonMode.Fill;

    public StencilState BackFaceStencil;
    public StencilState FrontFaceStencil;

    public uint32_t SamplesCount = 1u;
    public uint32_t PatchControlPoints = 0;
    public float MinSampleShading = 0.0f;

    public string DebugName = string.Empty;

    public RenderPipelineDesc()
    {
        for (uint32_t i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
        {
            Colors[i] = new ColorAttachment();
        }
    }

    public readonly uint32_t GetNumColorAttachments()
    {
        for (uint32_t i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
        {
            if (Colors[i].Format == Format.Invalid)
            {
                return i;
            }
        }
        return Constants.MAX_COLOR_ATTACHMENTS;
    }
}