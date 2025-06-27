using Glslang.NET;
using System.Text;

namespace HelixToolkit.Nex.Graphics.Vulkan;

internal static class HxVkExtensions
{
    private static readonly ILogger logger = LogManager.Create<HxVkUtils>();

    public static VkFilter ToVk(this SamplerFilter filter)
    {
        switch (filter)
        {
            case SamplerFilter.Nearest:
                return VK.VK_FILTER_NEAREST;
            case SamplerFilter.Linear:
                return VK.VK_FILTER_LINEAR;
        }
        HxDebug.Assert(false, $"SamplerFilter value not handled: {filter}");
        return VK.VK_FILTER_LINEAR;
    }

    public static VkSamplerMipmapMode ToVk(this SamplerMip filter)
    {
        switch (filter)
        {
            case SamplerMip.Disabled:
            case SamplerMip.Nearest:
                return VK.VK_SAMPLER_MIPMAP_MODE_NEAREST;
            case SamplerMip.Linear:
                return VK.VK_SAMPLER_MIPMAP_MODE_LINEAR;
        }
        HxDebug.Assert(false, $"SamplerMipMap value not handled: {filter}");
        return VK.VK_SAMPLER_MIPMAP_MODE_NEAREST;
    }

    public static VkSamplerAddressMode ToVk(this SamplerWrap mode)
    {
        switch (mode)
        {
            case SamplerWrap.Repeat:
                return VK.VK_SAMPLER_ADDRESS_MODE_REPEAT;
            case SamplerWrap.Clamp:
                return VK.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
            case SamplerWrap.MirrorRepeat:
                return VK.VK_SAMPLER_ADDRESS_MODE_MIRRORED_REPEAT;
        }
        HxDebug.Assert(false, $"SamplerWrapMode value not handled: {mode}");
        return VK.VK_SAMPLER_ADDRESS_MODE_REPEAT;
    }

    public static VkCompareOp ToVk(this CompareOp func)
    {
        switch (func)
        {
            case CompareOp.Never:
                return VK.VK_COMPARE_OP_NEVER;
            case CompareOp.Less:
                return VK.VK_COMPARE_OP_LESS;
            case CompareOp.Equal:
                return VK.VK_COMPARE_OP_EQUAL;
            case CompareOp.LessEqual:
                return VK.VK_COMPARE_OP_LESS_OR_EQUAL;
            case CompareOp.Greater:
                return VK.VK_COMPARE_OP_GREATER;
            case CompareOp.NotEqual:
                return VK.VK_COMPARE_OP_NOT_EQUAL;
            case CompareOp.GreaterEqual:
                return VK.VK_COMPARE_OP_GREATER_OR_EQUAL;
            case CompareOp.AlwaysPass:
                return VK.VK_COMPARE_OP_ALWAYS;
        }
        HxDebug.Assert(false, $"CompareFunction value not handled: {func}");
        return VK.VK_COMPARE_OP_ALWAYS;
    }

    public static uint32_t GetBytesPerPixel(this VkFormat format)
    {
        switch (format)
        {
            case VK.VK_FORMAT_R8_UNORM:
                return 1;
            case VK.VK_FORMAT_R16_SFLOAT:
                return 2;
            case VK.VK_FORMAT_R8G8B8_UNORM:
            case VK.VK_FORMAT_B8G8R8_UNORM:
                return 3;
            case VK.VK_FORMAT_R8G8B8A8_UNORM:
            case VK.VK_FORMAT_B8G8R8A8_UNORM:
            case VK.VK_FORMAT_R8G8B8A8_SRGB:
            case VK.VK_FORMAT_R16G16_SFLOAT:
            case VK.VK_FORMAT_R32_SFLOAT:
            case VK.VK_FORMAT_R32_UINT:
                return 4;
            case VK.VK_FORMAT_R16G16B16_SFLOAT:
                return 6;
            case VK.VK_FORMAT_R16G16B16A16_SFLOAT:
            case VK.VK_FORMAT_R32G32_SFLOAT:
            case VK.VK_FORMAT_R32G32_UINT:
                return 8;
            case VK.VK_FORMAT_R32G32B32_SFLOAT:
                return 12;
            case VK.VK_FORMAT_R32G32B32A32_SFLOAT:
                return 16;
            default:
                // Handle other formats as needed, or return a default value
                // For example, we can return 1 for unsupported formats
                logger.LogError("GetBytesPerPixel: Unsupported VkFormat {Format}", format);
                break;
        }
        HxDebug.Assert(false, $"VkFormat value not handled: {format}");
        return 1;
    }

    public static uint32_t GetNumImagePlanes(this VkFormat format)
    {
        switch (format)
        {
            case VK.VK_FORMAT_UNDEFINED:
                return 0;
            case VK.VK_FORMAT_G8_B8_R8_3PLANE_420_UNORM:
            case VK.VK_FORMAT_G8_B8_R8_3PLANE_422_UNORM:
            case VK.VK_FORMAT_G8_B8_R8_3PLANE_444_UNORM:
            case VK.VK_FORMAT_G12X4_B12X4_R12X4_3PLANE_420_UNORM_3PACK16:
            case VK.VK_FORMAT_G12X4_B12X4_R12X4_3PLANE_422_UNORM_3PACK16:
            case VK.VK_FORMAT_G12X4_B12X4_R12X4_3PLANE_444_UNORM_3PACK16:
            case VK.VK_FORMAT_G16_B16_R16_3PLANE_420_UNORM:
            case VK.VK_FORMAT_G16_B16_R16_3PLANE_422_UNORM:
            case VK.VK_FORMAT_G16_B16_R16_3PLANE_444_UNORM:
                return 3;
            case VK.VK_FORMAT_G8_B8R8_2PLANE_420_UNORM:
            case VK.VK_FORMAT_G8_B8R8_2PLANE_422_UNORM:
            case VK.VK_FORMAT_G12X4_B12X4R12X4_2PLANE_420_UNORM_3PACK16:
            case VK.VK_FORMAT_G12X4_B12X4R12X4_2PLANE_422_UNORM_3PACK16:
            case VK.VK_FORMAT_G16_B16R16_2PLANE_420_UNORM:
            case VK.VK_FORMAT_G16_B16R16_2PLANE_422_UNORM:
            case VK.VK_FORMAT_G8_B8R8_2PLANE_444_UNORM:
            case VK.VK_FORMAT_G10X6_B10X6R10X6_2PLANE_444_UNORM_3PACK16:
            case VK.VK_FORMAT_G12X4_B12X4R12X4_2PLANE_444_UNORM_3PACK16:
            case VK.VK_FORMAT_G16_B16R16_2PLANE_444_UNORM:
                return 2;
            default:
                return 1;
        }
    }

    public static bool IsDepthFormat(this VkFormat format)
    {
        return (format == VK.VK_FORMAT_D16_UNORM) || (format == VK.VK_FORMAT_X8_D24_UNORM_PACK32) || (format == VK.VK_FORMAT_D32_SFLOAT) ||
       (format == VK.VK_FORMAT_D16_UNORM_S8_UINT) || (format == VK.VK_FORMAT_D24_UNORM_S8_UINT) || (format == VK.VK_FORMAT_D32_SFLOAT_S8_UINT);
    }

    public static bool IsStencilFormat(this VkFormat format)
    {
        return (format == VK.VK_FORMAT_S8_UINT) || (format == VK.VK_FORMAT_D16_UNORM_S8_UINT) || (format == VK.VK_FORMAT_D24_UNORM_S8_UINT) ||
       (format == VK.VK_FORMAT_D32_SFLOAT_S8_UINT);
    }

    public static StageAccess2 GetPipelineStageAccess(this VkImageLayout layout)
    {
        switch (layout)
        {
            case VK.VK_IMAGE_LAYOUT_UNDEFINED:
                return new StageAccess2
                {
                    stage = VkPipelineStageFlags2.TopOfPipe,
                    access = VkAccessFlags2.None,
                };
            case VK.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL:
                return new StageAccess2
                {
                    stage = VkPipelineStageFlags2.ColorAttachmentOutput,
                    access = VkAccessFlags2.ColorAttachmentRead | VkAccessFlags2.ColorAttachmentWrite,
                };
            case VK.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL:
                return new StageAccess2
                {
                    stage = VkPipelineStageFlags2.LateFragmentTests | VkPipelineStageFlags2.EarlyFragmentTests,
                    access = VkAccessFlags2.DepthStencilAttachmentRead | VkAccessFlags2.DepthStencilAttachmentWrite,
                };
            case VK.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL:
                return new StageAccess2
                {
                    stage = VkPipelineStageFlags2.FragmentShader | VkPipelineStageFlags2.ComputeShader
                    | VkPipelineStageFlags2.VertexShader | VkPipelineStageFlags2.TessellationControlShader
                    | VkPipelineStageFlags2.GeometryShader | VkPipelineStageFlags2.TaskShaderEXT | VkPipelineStageFlags2.MeshShaderEXT,
                    access = VkAccessFlags2.ShaderRead,
                };
            case VK.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL:
                return new StageAccess2
                {
                    stage = VkPipelineStageFlags2.Transfer,
                    access = VkAccessFlags2.TransferRead,
                };
            case VK.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL:
                return new StageAccess2
                {
                    stage = VkPipelineStageFlags2.Transfer,
                    access = VkAccessFlags2.TransferWrite,
                };
            case VK.VK_IMAGE_LAYOUT_GENERAL:
                return new StageAccess2
                {
                    stage = VkPipelineStageFlags2.ComputeShader | VkPipelineStageFlags2.Transfer,
                    access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite | VkAccessFlags2.TransferWrite,
                };
            case VK.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR:
                return new StageAccess2
                {
                    stage = VkPipelineStageFlags2.ColorAttachmentOutput | VkPipelineStageFlags2.ComputeShader,
                    access = VkAccessFlags2.None | VkAccessFlags2.ShaderWrite,
                };
            default:
                HxDebug.Assert(false, "Unsupported image layout transition!");
                return new StageAccess2
                {
                    stage = VkPipelineStageFlags2.AllCommands,
                    access = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite,
                };
        }
    }
    public static Format ToFormat(this VkFormat format)
    {
        switch (format)
        {
            case VK.VK_FORMAT_UNDEFINED:
                return Format.Invalid;
            case VK.VK_FORMAT_R8_UNORM:
                return Format.R_UN8;
            case VK.VK_FORMAT_R16_UNORM:
                return Format.R_UN16;
            case VK.VK_FORMAT_R16_SFLOAT:
                return Format.R_F16;
            case VK.VK_FORMAT_R16_UINT:
                return Format.R_UI16;
            case VK.VK_FORMAT_R8G8_UNORM:
                return Format.RG_UN8;
            case VK.VK_FORMAT_B8G8R8A8_UNORM:
                return Format.BGRA_UN8;
            case VK.VK_FORMAT_R8G8B8A8_UNORM:
                return Format.RGBA_UN8;
            case VK.VK_FORMAT_R8G8B8A8_SRGB:
                return Format.RGBA_SRGB8;
            case VK.VK_FORMAT_B8G8R8A8_SRGB:
                return Format.BGRA_SRGB8;
            case VK.VK_FORMAT_R16G16_UNORM:
                return Format.RG_UN16;
            case VK.VK_FORMAT_R16G16_SFLOAT:
                return Format.RG_F16;
            case VK.VK_FORMAT_R32G32_SFLOAT:
                return Format.RG_F32;
            case VK.VK_FORMAT_R16G16_UINT:
                return Format.RG_UI16;
            case VK.VK_FORMAT_R32_SFLOAT:
                return Format.R_F32;
            case VK.VK_FORMAT_R16G16B16A16_SFLOAT:
                return Format.RGBA_F16;
            case VK.VK_FORMAT_R32G32B32A32_UINT:
                return Format.RGBA_UI32;
            case VK.VK_FORMAT_R32G32B32A32_SFLOAT:
                return Format.RGBA_F32;
            case VK.VK_FORMAT_A2B10G10R10_UNORM_PACK32:
                return Format.A2B10G10R10_UN;
            case VK.VK_FORMAT_A2R10G10B10_UNORM_PACK32:
                return Format.A2R10G10B10_UN;
            case VK.VK_FORMAT_ETC2_R8G8B8_UNORM_BLOCK:
                return Format.ETC2_RGB8;
            case VK.VK_FORMAT_ETC2_R8G8B8_SRGB_BLOCK:
                return Format.ETC2_SRGB8;
            case VK.VK_FORMAT_D16_UNORM:
                return Format.Z_UN16;
            case VK.VK_FORMAT_BC7_UNORM_BLOCK:
                return Format.BC7_RGBA;
            case VK.VK_FORMAT_X8_D24_UNORM_PACK32:
                return Format.Z_UN24;
            case VK.VK_FORMAT_D24_UNORM_S8_UINT:
                return Format.Z_UN24_S_UI8;
            case VK.VK_FORMAT_D32_SFLOAT:
                return Format.Z_F32;
            case VK.VK_FORMAT_D32_SFLOAT_S8_UINT:
                return Format.Z_F32_S_UI8;
            case VK.VK_FORMAT_G8_B8R8_2PLANE_420_UNORM:
                return Format.YUV_NV12;
            case VK.VK_FORMAT_G8_B8_R8_3PLANE_420_UNORM:
                return Format.YUV_420p;
        }
        HxDebug.Assert(false, "VkFormat value not handled: {FORMAT}", format.ToString());
        return Format.Invalid;
    }

    public static VkFormat ToVk(this Format format)
    {
        switch (format)
        {
            case Format.R_UN8:
                return VK.VK_FORMAT_R8_UNORM;
            case Format.R_UN16:
                return VK.VK_FORMAT_R16_UNORM;
            case Format.R_F16:
                return VK.VK_FORMAT_R16_SFLOAT;
            case Format.R_UI16:
                return VK.VK_FORMAT_R16_UINT;
            case Format.RG_UN8:
                return VK.VK_FORMAT_R8G8_UNORM;
            case Format.BGRA_UN8:
                return VK.VK_FORMAT_B8G8R8A8_UNORM;
            case Format.RGBA_UN8:
                return VK.VK_FORMAT_R8G8B8A8_UNORM;
            case Format.RGBA_SRGB8:
                return VK.VK_FORMAT_R8G8B8A8_SRGB;
            case Format.BGRA_SRGB8:
                return VK.VK_FORMAT_B8G8R8A8_SRGB;
            case Format.RG_UN16:
                return VK.VK_FORMAT_R16G16_UNORM;
            case Format.RG_F16:
                return VK.VK_FORMAT_R16G16_SFLOAT;
            case Format.RG_F32:
                return VK.VK_FORMAT_R32G32_SFLOAT;
            case Format.RG_UI16:
                return VK.VK_FORMAT_R16G16_UINT;
            case Format.R_F32:
                return VK.VK_FORMAT_R32_SFLOAT;
            case Format.RGBA_F16:
                return VK.VK_FORMAT_R16G16B16A16_SFLOAT;
            case Format.RGBA_UI32:
                return VK.VK_FORMAT_R32G32B32A32_UINT;
            case Format.RGBA_F32:
                return VK.VK_FORMAT_R32G32B32A32_SFLOAT;
            case Format.A2B10G10R10_UN:
                return VK.VK_FORMAT_A2B10G10R10_UNORM_PACK32;
            case Format.A2R10G10B10_UN:
                return VK.VK_FORMAT_A2R10G10B10_UNORM_PACK32;
            case Format.ETC2_RGB8:
                return VK.VK_FORMAT_ETC2_R8G8B8_UNORM_BLOCK;
            case Format.ETC2_SRGB8:
                return VK.VK_FORMAT_ETC2_R8G8B8_SRGB_BLOCK;
            case Format.Z_UN16:
                return VK.VK_FORMAT_D16_UNORM;
        }
        HxDebug.Assert(false, "Format value not handled: {FORMAT}", format.ToString());
        return VkFormat.Undefined;
    }

    public static VkFormat[] GetCompatibleDepthStencilFormats(this Format format)
    {
        return format switch
        {
            Format.Z_UN16 => [VK.VK_FORMAT_D16_UNORM, VK.VK_FORMAT_D16_UNORM_S8_UINT, VK.VK_FORMAT_D24_UNORM_S8_UINT, VK.VK_FORMAT_D32_SFLOAT],
            Format.Z_UN24 => [VK.VK_FORMAT_D24_UNORM_S8_UINT, VK.VK_FORMAT_D32_SFLOAT, VK.VK_FORMAT_D16_UNORM_S8_UINT],
            Format.Z_F32 => [VK.VK_FORMAT_D32_SFLOAT, VK.VK_FORMAT_D32_SFLOAT_S8_UINT, VK.VK_FORMAT_D24_UNORM_S8_UINT],
            Format.Z_UN24_S_UI8 => [VK.VK_FORMAT_D24_UNORM_S8_UINT, VK.VK_FORMAT_D16_UNORM_S8_UINT],
            Format.Z_F32_S_UI8 => [VK.VK_FORMAT_D32_SFLOAT_S8_UINT, VK.VK_FORMAT_D24_UNORM_S8_UINT, VK.VK_FORMAT_D16_UNORM_S8_UINT],
            _ => [VK.VK_FORMAT_D24_UNORM_S8_UINT, VK.VK_FORMAT_D32_SFLOAT],
        };
    }

    public static VkMemoryPropertyFlags ToVkMemoryPropertyFlags(this StorageType storage)
    {
        VkMemoryPropertyFlags memFlags = new();

        switch (storage)
        {
            case StorageType.Device:
                memFlags |= VK.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
                break;
            case StorageType.HostVisible:
                memFlags |= VK.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT;
                break;
            case StorageType.Memoryless:
                memFlags |= VK.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT | VK.VK_MEMORY_PROPERTY_LAZILY_ALLOCATED_BIT;
                break;
        }
        return memFlags;
    }

    public static VkComponentSwizzle ToVk(this Swizzle swizzle)
    {
        return swizzle switch
        {
            Swizzle.Default => VK.VK_COMPONENT_SWIZZLE_IDENTITY,
            Swizzle.Swizzle_0 => VK.VK_COMPONENT_SWIZZLE_ZERO,
            Swizzle.Swizzle_1 => VK.VK_COMPONENT_SWIZZLE_ONE,
            Swizzle.Swizzle_R => VK.VK_COMPONENT_SWIZZLE_R,
            Swizzle.Swizzle_G => VK.VK_COMPONENT_SWIZZLE_G,
            Swizzle.Swizzle_B => VK.VK_COMPONENT_SWIZZLE_B,
            Swizzle.Swizzle_A => VK.VK_COMPONENT_SWIZZLE_A,
            _ => throw new ArgumentOutOfRangeException(nameof(swizzle), $"Unsupported swizzle: {swizzle}"),
        };
    }

    public static bool Identity(this VkComponentMapping mapping)
    {
        return mapping.a == VkComponentSwizzle.Identity && mapping.r == VkComponentSwizzle.Identity && mapping.g == VkComponentSwizzle.Identity && mapping.b == VkComponentSwizzle.Identity;
    }

    public static VkSamplerCreateInfo ToVkSamplerCreateInfo(this SamplerStateDesc desc, in VkPhysicalDeviceLimits limits)
    {
        HxDebug.Assert(desc.MipLodMax >= desc.MipLodMin,
                 $"mipLodMax {desc.MipLodMax} must be greater than or equal to mipLodMin {desc.MipLodMin}");

        VkSamplerCreateInfo ci = new()
        {
            magFilter = desc.MagFilter.ToVk(),
            minFilter = desc.MinFilter.ToVk(),
            mipmapMode = desc.MipMap.ToVk(),
            addressModeU = desc.WrapU.ToVk(),
            addressModeV = desc.WrapV.ToVk(),
            addressModeW = desc.WrapW.ToVk(),
            mipLodBias = 0.0f,
            anisotropyEnable = VK_BOOL.False,
            maxAnisotropy = 0.0f,
            compareEnable = desc.DepthCompareEnabled ? VK_BOOL.True : VK_BOOL.False,
            compareOp = desc.DepthCompareEnabled ? desc.DepthCompareOp.ToVk() : VK.VK_COMPARE_OP_ALWAYS,
            minLod = (float)desc.MipLodMin,
            maxLod = desc.MipMap == SamplerMip.Disabled ? (float)desc.MipLodMin : (float)desc.MipLodMax,
            borderColor = VK.VK_BORDER_COLOR_INT_OPAQUE_BLACK,
            unnormalizedCoordinates = VK_BOOL.False,
        };

        if (desc.MaxAnisotropic > 1)
        {
            bool isAnisotropicFilteringSupported = limits.maxSamplerAnisotropy > 1;
            HxDebug.Assert(isAnisotropicFilteringSupported, "Anisotropic filtering is not supported by the device.");
            ci.anisotropyEnable = isAnisotropicFilteringSupported ? VK_BOOL.True : VK_BOOL.False;

            if (limits.maxSamplerAnisotropy < desc.MaxAnisotropic)
            {
                logger.LogWarning(
                    "Supplied sampler anisotropic value greater than max supported by the device, setting to {Anisotropic}", limits.maxSamplerAnisotropy);
            }
            ci.maxAnisotropy = Math.Min((float)limits.maxSamplerAnisotropy, (float)desc.MaxAnisotropic);
        }

        return ci;
    }

    public static VkShaderStageFlags ToVk(this ShaderStage stage)
    {
        switch (stage)
        {
            case ShaderStage.Vertex:
                return VK.VK_SHADER_STAGE_VERTEX_BIT;
            case ShaderStage.TessellationControl:
                return VK.VK_SHADER_STAGE_TESSELLATION_CONTROL_BIT;
            case ShaderStage.TessellationEvaluation:
                return VK.VK_SHADER_STAGE_TESSELLATION_EVALUATION_BIT;
            case ShaderStage.Geometry:
                return VK.VK_SHADER_STAGE_GEOMETRY_BIT;
            case ShaderStage.Fragment:
                return VK.VK_SHADER_STAGE_FRAGMENT_BIT;
            case ShaderStage.Compute:
                return VK.VK_SHADER_STAGE_COMPUTE_BIT;
            case ShaderStage.Mesh:
                return VK.VK_SHADER_STAGE_MESH_BIT_EXT;
            default:
                HxDebug.Assert(false, $"ShaderStage value not handled: {stage}");
                return VkShaderStageFlags.None;
        }
    }

    public static ColorSpace ToColorSpace(this VkColorSpaceKHR colorSpace)
    {
        switch (colorSpace)
        {
            case VK.VK_COLOR_SPACE_SRGB_NONLINEAR_KHR:
                return ColorSpace.SRGB_NONLINEAR;
            case VK.VK_COLOR_SPACE_EXTENDED_SRGB_LINEAR_EXT:
                return ColorSpace.SRGB_LINEAR;
            case VK.VK_COLOR_SPACE_HDR10_ST2084_EXT:
                return ColorSpace.HDR10;
            default:
                HxDebug.Assert(false, $"Unsupported color space {colorSpace}");
                return ColorSpace.SRGB_NONLINEAR;
        }
    }

    public static VkDevice GetVkDevice(this IContext ctx)
    {
        return ctx is VulkanContext vkCtx ? vkCtx.GetVkDevice() : VkDevice.Null;
    }

    public static VkPhysicalDevice GetVkPhysicalDevice(this IContext ctx)
    {
        return ctx is VulkanContext vkCtx ? vkCtx.GetVkPhysicalDevice() : VkPhysicalDevice.Null;
    }

    public static VkCommandBuffer GetVkCommandBuffer(this ICommandBuffer buffer)
    {
        return buffer is CommandBuffer vkCmdBuffer ? vkCmdBuffer.CmdBuffer : VkCommandBuffer.Null;
    }

    public static VkBuffer GetVkBuffer(this IContext ctx, in BufferHandle buffer)
    {
        return ctx is VulkanContext vkCtx && buffer.Valid ? vkCtx.BuffersPool.Get(buffer)!.VkBuffer : VkBuffer.Null;
    }

    public static VkImage GetVkImage(this IContext ctx, in TextureHandle texture)
    {
        return ctx is VulkanContext vkCtx && texture.Valid ? vkCtx.TexturesPool.Get(texture)!.Image : VkImage.Null;
    }

    public static VkImageView GetVkImageView(this IContext ctx, in TextureHandle texture)
    {
        return ctx is VulkanContext vkCtx && texture.Valid ? vkCtx.TexturesPool.Get(texture)!.ImageView : VkImageView.Null;
    }

    public static VkShaderModule GetVkShaderModule(this IContext ctx, in ShaderModuleHandle shader)
    {
        return ctx is VulkanContext vkCtx && shader.Valid ? vkCtx.ShaderModulesPool.Get(shader)!.ShaderModule : VkShaderModule.Null;
    }

    public static VkUtf8ReadOnlyString ToVkUtf8ReadOnlyString(this string str)
    {
        return Encoding.UTF8.GetBytes(str);
    }

    public static VkIndexType ToVk(this IndexFormat index)
    {
        return index switch
        {
            IndexFormat.UI8 => VkIndexType.Uint8,
            IndexFormat.UI16 => VkIndexType.Uint16,
            IndexFormat.UI32 => VkIndexType.Uint32,
            _ => throw new NotSupportedException($"Index format {index} is not supported."),
        };
    }

    public static VkBlendFactor ToVk(this BlendFactor factor)
    {
        return factor switch
        {
            BlendFactor.Zero => VkBlendFactor.Zero,
            BlendFactor.One => VkBlendFactor.One,
            BlendFactor.SrcColor => VkBlendFactor.SrcColor,
            BlendFactor.OneMinusSrcColor => VkBlendFactor.OneMinusSrcColor,
            BlendFactor.DstColor => VkBlendFactor.DstColor,
            BlendFactor.OneMinusDstColor => VkBlendFactor.OneMinusDstColor,
            BlendFactor.SrcAlpha => VkBlendFactor.SrcAlpha,
            BlendFactor.OneMinusSrcAlpha => VkBlendFactor.OneMinusSrcAlpha,
            BlendFactor.DstAlpha => VkBlendFactor.DstAlpha,
            BlendFactor.OneMinusDstAlpha => VkBlendFactor.OneMinusDstAlpha,
            _ => throw new NotSupportedException($"Blend factor {factor} is not supported."),
        };
    }

    public static VkBlendOp ToVk(this BlendOp operation)
    {
        return operation switch
        {
            BlendOp.Add => VkBlendOp.Add,
            BlendOp.Subtract => VkBlendOp.Subtract,
            BlendOp.ReverseSubtract => VkBlendOp.ReverseSubtract,
            BlendOp.Min => VkBlendOp.Min,
            BlendOp.Max => VkBlendOp.Max,
            _ => throw new NotSupportedException($"Blend operation {operation} is not supported."),
        };
    }

    public static uint32_t GetMaxPushConstantsSize(this ShaderModuleState? state, uint32_t constantSize)
    {
        return state is not null && state ? Math.Max(state.PushConstantsSize, constantSize) : constantSize;
    }

    public static VkPrimitiveTopology ToVk(this Topology topology)
    {
        return topology switch
        {
            Topology.Point => VkPrimitiveTopology.PointList,
            Topology.Line => VkPrimitiveTopology.LineList,
            Topology.LineStrip => VkPrimitiveTopology.LineStrip,
            Topology.Triangle => VkPrimitiveTopology.TriangleList,
            Topology.TriangleStrip => VkPrimitiveTopology.TriangleStrip,
            Topology.Patch => VkPrimitiveTopology.PatchList,
            _ => throw new NotSupportedException($"Topology {topology} is not supported."),
        };
    }

    public static VkPolygonMode ToVk(this PolygonMode mode)
    {
        return mode switch
        {
            PolygonMode.Fill => VkPolygonMode.Fill,
            PolygonMode.Line => VkPolygonMode.Line,
            PolygonMode.Point => VkPolygonMode.Point,
            _ => throw new NotSupportedException($"Polygon mode {mode} is not supported."),
        };
    }

    public static VkCullModeFlags ToVk(this CullMode mode)
    {
        return mode switch
        {
            CullMode.None => VkCullModeFlags.None,
            CullMode.Front => VkCullModeFlags.Front,
            CullMode.Back => VkCullModeFlags.Back,
            _ => throw new NotSupportedException($"Cull mode {mode} is not supported."),
        };
    }

    public static VkStencilOp ToVk(this StencilOp op)
    {
        return op switch
        {
            StencilOp.Keep => VkStencilOp.Keep,
            StencilOp.Zero => VkStencilOp.Zero,
            StencilOp.Replace => VkStencilOp.Replace,
            StencilOp.IncrementClamp => VkStencilOp.IncrementAndClamp,
            StencilOp.DecrementClamp => VkStencilOp.DecrementAndClamp,
            StencilOp.Invert => VkStencilOp.Invert,
            StencilOp.IncrementWrap => VkStencilOp.IncrementAndWrap,
            StencilOp.DecrementWrap => VkStencilOp.DecrementAndWrap,
            _ => throw new NotSupportedException($"Stencil operation {op} is not supported."),
        };
    }

    public static VkFrontFace ToVk(this WindingMode mode)
    {
        return mode switch
        {
            WindingMode.CW => VkFrontFace.Clockwise,
            WindingMode.CCW => VkFrontFace.CounterClockwise,
            _ => throw new NotSupportedException($"Winding mode {mode} is not supported."),
        };
    }

    public static VkResolveModeFlags ResolveModeToVkResolveModeFlagBits(this ResolveMode mode, VkResolveModeFlags supported)
    {
        return mode switch
        {
            ResolveMode.None => VkResolveModeFlags.None,
            ResolveMode.SampleZero => VkResolveModeFlags.SampleZero,
            ResolveMode.Average => supported.HasFlag(VkResolveModeFlags.Average) ? VkResolveModeFlags.Average : VkResolveModeFlags.SampleZero,
            ResolveMode.Min => supported.HasFlag(VkResolveModeFlags.Min) ? VkResolveModeFlags.Min : VkResolveModeFlags.SampleZero,
            ResolveMode.Max => supported.HasFlag(VkResolveModeFlags.Average) ? VkResolveModeFlags.Average : VkResolveModeFlags.SampleZero,
            _ => VkResolveModeFlags.SampleZero,// Default to SampleZero if unsupported mode is provided
        };
    }

    public static VkAttachmentLoadOp ToVk(this LoadOp op)
    {
        return op switch
        {
            LoadOp.Load => VkAttachmentLoadOp.Load,
            LoadOp.Clear => VkAttachmentLoadOp.Clear,
            LoadOp.DontCare => VkAttachmentLoadOp.DontCare,
            _ => throw new NotSupportedException($"Load operation {op} is not supported."),
        };
    }

    public static VkAttachmentStoreOp ToVk(this StoreOp op)
    {
        return op switch
        {
            StoreOp.Store => VkAttachmentStoreOp.Store,
            StoreOp.DontCare => VkAttachmentStoreOp.DontCare,
            _ => throw new NotSupportedException($"Store operation {op} is not supported."),
        };
    }

    public static VkClearColorValue ToVk(this Color4 color)
    {
        return new VkClearColorValue(color.Red, color.Green, color.Blue, color.Alpha);
    }

    public static void ImageMemoryBarrier2(this VkCommandBuffer buffer,
                              VkImage image,
                              StageAccess2 src,
                              StageAccess2 dst,
                              VkImageLayout oldImageLayout,
                              VkImageLayout newImageLayout,
                              VkImageSubresourceRange subresourceRange)
    {
        VkImageMemoryBarrier2 barrier = new()
        {
            srcStageMask = src.stage,
            srcAccessMask = src.access,
            dstStageMask = dst.stage,
            dstAccessMask = dst.access,
            oldLayout = oldImageLayout,
            newLayout = newImageLayout,
            srcQueueFamilyIndex = VK.VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK.VK_QUEUE_FAMILY_IGNORED,
            image = image,
            subresourceRange = subresourceRange,
        };

        unsafe
        {
            VkDependencyInfo depInfo = new()
            {
                imageMemoryBarrierCount = 1,
                pImageMemoryBarriers = &barrier,
            };
            VK.vkCmdPipelineBarrier2(buffer, &depInfo);
        }
    }

    public static bool IsShaderStage(this VkPipelineStageFlags2 stage)
    {
        return stage.HasFlag(VkPipelineStageFlags2.VertexShader) || stage.HasFlag(VkPipelineStageFlags2.TessellationControlShader)
            || stage.HasFlag(VkPipelineStageFlags2.TessellationEvaluationShader) || stage.HasFlag(VkPipelineStageFlags2.GeometryShader)
            || stage.HasFlag(VkPipelineStageFlags2.FragmentShader) || stage.HasFlag(VkPipelineStageFlags2.ComputeShader)
            || stage.HasFlag(VkPipelineStageFlags2.MeshShaderEXT) || stage.HasFlag(VkPipelineStageFlags2.TaskShaderEXT);
    }

    public static void BufferBarrier2(this VkCommandBuffer cmdbuffer, in VulkanBuffer buf, VkPipelineStageFlags2 srcStage, VkPipelineStageFlags2 dstStage)
    {
        VkBufferMemoryBarrier2 barrier = new()
        {
            srcStageMask = srcStage,
            srcAccessMask = 0,
            dstStageMask = dstStage,
            dstAccessMask = 0,
            srcQueueFamilyIndex = VK.VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK.VK_QUEUE_FAMILY_IGNORED,
            buffer = buf!.VkBuffer,
            offset = 0,
            size = VK.VK_WHOLE_SIZE,
        };

        if (srcStage.HasFlag(VkPipelineStageFlags2.Transfer))
        {
            barrier.srcAccessMask |= VkAccessFlags2.TransferRead | VkAccessFlags2.TransferWrite;
        }
        else if (srcStage.HasFlag(VkPipelineStageFlags2.AllGraphics | VkPipelineStageFlags2.AllCommands) || srcStage.IsShaderStage())
        {
            barrier.srcAccessMask |= VkAccessFlags2.ShaderRead | VkAccessFlags2.ShaderWrite;
        }

        if (dstStage.HasFlag(VkPipelineStageFlags2.Transfer))
        {
            barrier.dstAccessMask |= VkAccessFlags2.TransferRead | VkAccessFlags2.TransferWrite;
        }
        else if(srcStage.HasFlag(VkPipelineStageFlags2.AllGraphics | VkPipelineStageFlags2.AllCommands) || srcStage.IsShaderStage())
        {
            barrier.dstAccessMask |= VkAccessFlags2.ShaderRead | VkAccessFlags2.ShaderWrite;
        }
        if (dstStage.HasFlag(VkPipelineStageFlags2.DrawIndirect))
        {
            barrier.dstAccessMask |= VkAccessFlags2.IndirectCommandRead;
        }
        if (buf.vkUsageFlags_.HasFlag(VkBufferUsageFlags.IndexBuffer))
        {
            barrier.dstAccessMask |= VkAccessFlags2.IndexRead;
            barrier.dstStageMask |= VkPipelineStageFlags2.IndexInput;
        }
        unsafe
        {
            VkDependencyInfo depInfo = new()
            {
                bufferMemoryBarrierCount = 1,
                pBufferMemoryBarriers = &barrier,
            };

            VK.vkCmdPipelineBarrier2(cmdbuffer, &depInfo);
        }
    }

    public static void TransitionToColorAttachment(this VkCommandBuffer buffer, VulkanImage colorTex)
    {
        if (colorTex.IsDepthFormat || colorTex.IsStencilFormat)
        {
            logger.LogError("Color attachments cannot have depth/stencil formats");
            return;
        }
        HxDebug.Assert(colorTex.ImageFormat != VkFormat.Undefined, "Invalid color attachment format");
        colorTex.TransitionLayout(buffer, VK.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, new VkImageSubresourceRange(VK.VK_IMAGE_ASPECT_COLOR_BIT, 0, VK.VK_REMAINING_MIP_LEVELS, 0, VK.VK_REMAINING_ARRAY_LAYERS));
    }

    public static VkSemaphore CreateSemaphore(this VkDevice device, string? debugName = null)
    {
        unsafe
        {
            VK.vkCreateSemaphore(device, new VkSemaphoreCreateInfo(), null, out var semaphore).CheckResult();
            if (debugName != null)
            {
                VK.vkSetDebugUtilsObjectNameEXT(device, VkObjectType.Semaphore, semaphore.Handle, debugName).CheckResult("Failed to set debug name for semaphore.");
            }
            return semaphore;
        }
    }

    public static VkSemaphore CreateSemaphoreTimeline(this VkDevice device, ulong initialValue, string? debugName = null)
    {
        var semaphoreInfo = new VkSemaphoreTypeCreateInfo()
        {
            semaphoreType = VkSemaphoreType.Timeline,
            initialValue = initialValue
        };
        unsafe
        {
            var createInfo = new VkSemaphoreCreateInfo()
            {
                pNext = &semaphoreInfo
            };
            VK.vkCreateSemaphore(device, &createInfo, null, out var semaphore).CheckResult();
            if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(debugName))
            {
                VK.vkSetDebugUtilsObjectNameEXT(device, VkObjectType.Semaphore, semaphore, debugName).CheckResult("Failed to set debug name for semaphore.");
            }
            return semaphore;
        }
    }

    public static VkFence CreateFence(this VkDevice device, string? debugName = null)
    {
        unsafe
        {
            VK.vkCreateFence(device, new VkFenceCreateInfo(), null, out var fence).CheckResult();
            if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(debugName))
            {
                VK.vkSetDebugUtilsObjectNameEXT(device, VkObjectType.Fence, fence, debugName).CheckResult("Failed to set debug name for fence.");
            }
            return fence;
        }
    }

    public unsafe static VkResult SetDebugObjectName(this VkDevice device, VkObjectType type, nuint handle, VkUtf8ReadOnlyString name)
    {
        VkDebugUtilsObjectNameInfoEXT ni = new()
        {
            objectType = type,
            objectHandle = handle,
            pObjectName = name,
        };
        return VK.vkSetDebugUtilsObjectNameEXT(device, &ni);
    }

    public unsafe static VkResult SetDebugObjectName(this VkDevice device, VkObjectType type, nuint handle, string? name)
    {
        return VK.vkSetDebugUtilsObjectNameEXT(device, type, handle, name);
    }

    public unsafe static VkResult SetDebugObjectName(this VkDevice device, VkObjectType type, nuint handle, ReadOnlySpan<byte> name)
    {
        return VK.vkSetDebugUtilsObjectNameEXT(device, type, handle, name);
    }

    public unsafe static VkResult SetDebugObjectName(this VkDevice device, VkObjectType type, nuint handle, ReadOnlySpan<char> name)
    {
        return VK.vkSetDebugUtilsObjectNameEXT(device, type, handle, name);
    }
}