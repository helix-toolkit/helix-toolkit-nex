namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Describes the configuration for creating a compute pipeline.
/// </summary>
/// <remarks>
/// A compute pipeline is used for GPU compute operations. It specifies the compute shader,
/// entry point, and optional specialization constants that control shader behavior at pipeline creation time.
/// </remarks>
public sealed class ComputePipelineDesc()
{
    /// <summary>
    /// The handle to the compute shader module to use in this pipeline.
    /// </summary>
    public ShaderModuleResource ComputeShader = ShaderModuleResource.Null;

    /// <summary>
    /// Specialization constants that allow compile-time configuration of the shader.
    /// </summary>
    /// <remarks>
    /// Specialization constants enable optimizations by providing constant values at pipeline creation time
    /// rather than at shader compilation time. See <see cref="SpecializationConstantDesc"/> for more information.
    /// </remarks>
    public SpecializationConstantDesc SpecInfo = new();

    /// <summary>
    /// The name of the entry point function in the compute shader. Defaults to "main".
    /// </summary>
    public string EntryPoint = "main";

    /// <summary>
    /// Optional debug name for the pipeline, used in debugging and profiling tools.
    /// </summary>
    public string DebugName = string.Empty;

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
        var oldData = SpecInfo.Data;
        var newData = new byte[oldData.Length + data.Length];
        Array.Copy(oldData, 0, newData, 0, oldData.Length);
        Array.Copy(data, 0, newData, oldData.Length, data.Length);
        SpecInfo.Data = newData;
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
    /// Creates a new instance of <see cref="ComputePipelineDesc"/> that is a deep copy of the current instance.
    /// </summary>
    /// <remarks>The returned instance contains copies of all mutable fields, ensuring that changes to the
    /// cloned object do not affect the original instance.</remarks>
    /// <returns>A new <see cref="ComputePipelineDesc"/> instance that is a deep copy of the current instance.</returns>
    public ComputePipelineDesc Clone()
    {
        var clone = new ComputePipelineDesc
        {
            ComputeShader = ComputeShader,
            SpecInfo = new SpecializationConstantDesc { Data = (byte[])SpecInfo.Data.Clone() },
            EntryPoint = EntryPoint,
            DebugName = DebugName,
        };
        for (uint32_t i = 0; i < SpecInfo.NumSpecializationConstants(); i++)
        {
            clone.SpecInfo.Entries[i] = SpecInfo.Entries[i];
        }
        return clone;
    }
}
