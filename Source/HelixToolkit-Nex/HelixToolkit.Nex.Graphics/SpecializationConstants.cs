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

    public void WriteSpecInfo(uint32_t constantId, byte[] data)
    {
        if (NumSpecializationConstants() >= SpecializationConstantDesc.SPECIALIZATION_CONSTANTS_MAX)
        {
            throw new InvalidOperationException(
                "Maximum number of specialization constants exceeded."
            );
        }
        for (uint32_t i = 0; i < NumSpecializationConstants(); i++)
        {
            if (Entries[i].ConstantId == constantId)
            {
                throw new InvalidOperationException(
                    $"Specialization constant with ID {constantId} already exists."
                );
            }
        }
        var offset = (uint32_t)Data.Length;
        Entries[NumSpecializationConstants()] = new SpecializationConstantEntry
        {
            ConstantId = constantId,
            Offset = offset,
            Size = (uint32_t)data.Length,
        };
        var oldData = Data;
        var newData = new byte[oldData.Length + data.Length];
        Array.Copy(oldData, 0, newData, 0, oldData.Length);
        Array.Copy(data, 0, newData, oldData.Length, data.Length);
        Data = newData;
    }

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
};
