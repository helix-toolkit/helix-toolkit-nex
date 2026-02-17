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

    public readonly SpecializationConstantEntry[] Entries = new SpecializationConstantEntry[
        SPECIALIZATION_CONSTANTS_MAX
    ];

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
public sealed class RenderPipelineDesc
{
    public Topology Topology = Topology.Triangle;

    public VertexInput VertexInput = VertexInput.Null;

    public ShaderModuleResource VertexShader = ShaderModuleResource.Null;
    public ShaderModuleResource TessControlShader = ShaderModuleResource.Null;
    public ShaderModuleResource TessEvalShader = ShaderModuleResource.Null;
    public ShaderModuleResource GeometryShader = ShaderModuleResource.Null;
    public ShaderModuleResource TaskShader = ShaderModuleResource.Null;
    public ShaderModuleResource MeshShader = ShaderModuleResource.Null;
    public ShaderModuleResource FragementShader = ShaderModuleResource.Null;

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

    public StencilState BackFaceStencil = StencilState.Disabled;
    public StencilState FrontFaceStencil = StencilState.Disabled;

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

    /// <summary>
    /// Retrieves the number of valid color attachments.
    /// </summary>
    /// <remarks>The method iterates through the color attachments and counts the number of attachments  with
    /// a valid format. The count stops at the first attachment with an invalid format.</remarks>
    /// <returns>The number of valid color attachments. This value will be between 0 and  <see
    /// cref="Constants.MAX_COLOR_ATTACHMENTS"/>, inclusive.</returns>
    public uint32_t GetNumColorAttachments()
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

    /// <summary>
    /// Adds a specialization constant entry to the current specialization info.
    /// </summary>
    /// <remarks>This method appends the provided specialization constant data to the internal data buffer and
    /// updates the specialization constant entries accordingly. The maximum number of specialization constants is
    /// defined by <see cref="SpecializationConstantDesc.SPECIALIZATION_CONSTANTS_MAX"/>.</remarks>
    /// <param name="constantId">The unique identifier of the specialization constant.</param>
    /// <param name="data">The data associated with the specialization constant. Cannot be null.</param>
    /// <exception cref="InvalidOperationException">Thrown if the maximum number of specialization constants has been exceeded.</exception>
    public void WriteSpecInfo(uint32_t constantId, byte[] data)
    {
        if (
            SpecInfo.NumSpecializationConstants()
            >= SpecializationConstantDesc.SPECIALIZATION_CONSTANTS_MAX
        )
        {
            throw new InvalidOperationException(
                "Maximum number of specialization constants exceeded."
            );
        }
        for (uint32_t i = 0; i < SpecInfo.NumSpecializationConstants(); i++)
        {
            if (SpecInfo.Entries[i].ConstantId == constantId)
            {
                throw new InvalidOperationException(
                    $"Specialization constant with ID {constantId} already exists."
                );
            }
        }
        uint32_t offset = (uint32_t)SpecInfo.Data.Length;
        SpecInfo.Entries[SpecInfo.NumSpecializationConstants()] = new SpecializationConstantEntry
        {
            ConstantId = constantId,
            Offset = offset,
            Size = (uint32_t)data.Length,
        };
        SpecInfo.Data = SpecInfo.Data.Concat(data).ToArray();
    }

    /// <summary>
    /// Writes specification information for a given constant identifier and value.
    /// </summary>
    /// <remarks>This method serializes the provided value into a byte array and writes it along with the
    /// specified constant identifier. The type parameter <typeparamref name="T"/> must be unmanaged, ensuring that the
    /// value can be safely converted to raw memory.</remarks>
    /// <typeparam name="T">The type of the value to write. Must be an unmanaged type.</typeparam>
    /// <param name="constantId">The identifier of the constant associated with the specification information.</param>
    /// <param name="value">The value to write, represented as an unmanaged type.</param>
    public void WriteSpecInfo<T>(uint32_t constantId, T value)
        where T : unmanaged
    {
        var data = new byte[NativeHelper.SizeOf<T>()];
        unsafe
        {
            fixed (byte* pData = data)
            {
                *(T*)pData = value;
            }
        }
        WriteSpecInfo(constantId, data);
    }

    /// <summary>
    /// Creates a deep copy of the current <see cref="RenderPipelineDesc"/> instance.
    /// </summary>
    /// <remarks>The method performs a deep copy of all fields and properties, including arrays and complex
    /// objects,  ensuring that the cloned instance is independent of the original. This is useful when a separate  copy
    /// of the render pipeline description is needed without affecting the original instance.</remarks>
    /// <returns>A new <see cref="RenderPipelineDesc"/> instance that is a deep copy of the current instance.</returns>
    public RenderPipelineDesc Clone()
    {
        var clone = new RenderPipelineDesc
        {
            Topology = Topology,
            VertexInput = VertexInput,

            VertexShader = VertexShader,
            TessControlShader = TessControlShader,
            TessEvalShader = TessEvalShader,
            GeometryShader = GeometryShader,
            TaskShader = TaskShader,
            MeshShader = MeshShader,
            FragementShader = FragementShader,
        };

        // Deep clone SpecInfo
        for (uint32_t i = 0; i < SpecializationConstantDesc.SPECIALIZATION_CONSTANTS_MAX; i++)
        {
            clone.SpecInfo.Entries[i] = SpecInfo.Entries[i];
        }
        clone.SpecInfo.Data = (byte[])SpecInfo.Data.Clone();

        clone.EntryPointVert = EntryPointVert;
        clone.EntryPointTesc = EntryPointTesc;
        clone.EntryPointTese = EntryPointTese;
        clone.EntryPointGeom = EntryPointGeom;
        clone.EntryPointTask = EntryPointTask;
        clone.EntryPointMesh = EntryPointMesh;
        clone.EntryPointFrag = EntryPointFrag;

        // Deep clone Colors array
        for (uint32_t i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
        {
            clone.Colors[i] = Colors[i];
        }

        clone.DepthFormat = DepthFormat;
        clone.StencilFormat = StencilFormat;

        clone.CullMode = CullMode;
        clone.FrontFaceWinding = FrontFaceWinding;
        clone.PolygonMode = PolygonMode;

        clone.BackFaceStencil = BackFaceStencil;
        clone.FrontFaceStencil = FrontFaceStencil;

        clone.SamplesCount = SamplesCount;
        clone.PatchControlPoints = PatchControlPoints;
        clone.MinSampleShading = MinSampleShading;

        clone.DebugName = DebugName;

        return clone;
    }
}
