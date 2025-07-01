using Glslang.NET;
using System.Runtime.InteropServices;
using System.Text;

namespace HelixToolkit.Nex.Graphics.Vulkan;
using GLSLShaderStage = Glslang.NET.ShaderStage;

internal static class ShaderExtensions
{
    struct ShaderExt { }
    private static readonly ILogger logger = LogManager.Create<ShaderExt>();

    public static GLSLShaderStage ToGLSL(this VkShaderStageFlags stage)
    {
        return stage switch
        {
            VkShaderStageFlags.Vertex => GLSLShaderStage.Vertex,
            VkShaderStageFlags.TessellationControl => GLSLShaderStage.TessControl,
            VkShaderStageFlags.TessellationEvaluation => GLSLShaderStage.TessEvaluation,
            VkShaderStageFlags.Geometry => GLSLShaderStage.Geometry,
            VkShaderStageFlags.Fragment => GLSLShaderStage.Fragment,
            VkShaderStageFlags.Compute => GLSLShaderStage.Compute,
            VkShaderStageFlags.MeshEXT => GLSLShaderStage.Mesh,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
        };
    }

    public static ResourceLimits GetGlslangResource(this VkPhysicalDeviceLimits limits)
    {
        unsafe
        {
            var resource = new Glslang.NET.ResourceLimits()
            {
                maxLights = 32,
                maxClipPlanes = (int)limits.maxClipDistances,
                maxTextureUnits = 32,
                maxTextureCoords = 32,
                maxVertexAttribs = (int)limits.maxVertexInputAttributes,
                maxVertexUniformComponents = (int)limits.maxUniformBufferRange / 4,
                maxVaryingFloats = (int)Math.Min(limits.maxVertexOutputComponents, limits.maxFragmentInputComponents),
                maxVertexTextureImageUnits = 32,
                maxCombinedTextureImageUnits = 80,
                maxTextureImageUnits = 32,
                maxFragmentUniformComponents = 4096,
                maxDrawBuffers = 32,
                maxVertexUniformVectors = 128,
                maxVaryingVectors = 8,
                maxFragmentUniformVectors = 16,
                maxVertexOutputVectors = (int)limits.maxVertexOutputComponents / 4,
                maxFragmentInputVectors = (int)limits.maxFragmentInputComponents / 4,
                minProgramTexelOffset = limits.minTexelOffset,
                maxProgramTexelOffset = (int)limits.maxTexelOffset,
                maxClipDistances = (int)limits.maxClipDistances,
                maxComputeWorkGroupCountX = (int)limits.maxComputeWorkGroupCount[0],
                maxComputeWorkGroupCountY = (int)limits.maxComputeWorkGroupCount[1],
                maxComputeWorkGroupCountZ = (int)limits.maxComputeWorkGroupCount[2],
                maxComputeWorkGroupSizeX = (int)limits.maxComputeWorkGroupSize[0],
                maxComputeWorkGroupSizeY = (int)limits.maxComputeWorkGroupSize[1],
                maxComputeWorkGroupSizeZ = (int)limits.maxComputeWorkGroupSize[2],
                maxComputeUniformComponents = 1024,
                maxComputeTextureImageUnits = 16,
                maxComputeImageUniforms = 8,
                maxComputeAtomicCounters = 8,
                maxComputeAtomicCounterBuffers = 1,
                maxVaryingComponents = 60,
                maxVertexOutputComponents = (int)limits.maxVertexOutputComponents,
                maxGeometryInputComponents = (int)limits.maxGeometryInputComponents,
                maxGeometryOutputComponents = (int)limits.maxGeometryOutputComponents,
                maxFragmentInputComponents = (int)limits.maxFragmentInputComponents,
                maxImageUnits = 8,
                maxCombinedImageUnitsAndFragmentOutputs = 8,
                maxCombinedShaderOutputResources = 8,
                maxImageSamples = 0,
                maxVertexImageUniforms = 0,
                maxTessControlImageUniforms = 0,
                maxTessEvaluationImageUniforms = 0,
                maxGeometryImageUniforms = 0,
                maxFragmentImageUniforms = 8,
                maxCombinedImageUniforms = 8,
                maxGeometryTextureImageUnits = 16,
                maxGeometryOutputVertices = (int)limits.maxGeometryOutputVertices,
                maxGeometryTotalOutputComponents = (int)limits.maxGeometryTotalOutputComponents,
                maxGeometryUniformComponents = 1024,
                maxGeometryVaryingComponents = 64,
                maxTessControlInputComponents = (int)limits.maxTessellationControlPerVertexInputComponents,
                maxTessControlOutputComponents = (int)limits.maxTessellationControlPerVertexOutputComponents,
                maxTessControlTextureImageUnits = 16,
                maxTessControlUniformComponents = 1024,
                maxTessControlTotalOutputComponents = 4096,
                maxTessEvaluationInputComponents = (int)limits.maxTessellationEvaluationInputComponents,
                maxTessEvaluationOutputComponents = (int)limits.maxTessellationEvaluationOutputComponents,
                maxTessEvaluationTextureImageUnits = 16,
                maxTessEvaluationUniformComponents = 1024,
                maxTessPatchComponents = 120,
                maxPatchVertices = 32,
                maxTessGenLevel = 64,
                maxViewports = (int)limits.maxViewports,
                maxVertexAtomicCounters = 0,
                maxTessControlAtomicCounters = 0,
                maxTessEvaluationAtomicCounters = 0,
                maxGeometryAtomicCounters = 0,
                maxFragmentAtomicCounters = 8,
                maxCombinedAtomicCounters = 8,
                maxAtomicCounterBindings = 1,
                maxVertexAtomicCounterBuffers = 0,
                maxTessControlAtomicCounterBuffers = 0,
                maxTessEvaluationAtomicCounterBuffers = 0,
                maxGeometryAtomicCounterBuffers = 0,
                maxFragmentAtomicCounterBuffers = 1,
                maxCombinedAtomicCounterBuffers = 1,
                maxAtomicCounterBufferSize = 16384,
                maxTransformFeedbackBuffers = 4,
                maxTransformFeedbackInterleavedComponents = 64,
                maxCullDistances = (int)limits.maxCullDistances,
                maxCombinedClipAndCullDistances = (int)limits.maxCombinedClipAndCullDistances,
                maxSamples = 4,
                maxMeshOutputVerticesNV = 256,
                maxMeshOutputPrimitivesNV = 512,
                maxMeshWorkGroupSizeX_NV = 32,
                maxMeshWorkGroupSizeY_NV = 1,
                maxMeshWorkGroupSizeZ_NV = 1,
                maxTaskWorkGroupSizeX_NV = 32,
                maxTaskWorkGroupSizeY_NV = 1,
                maxTaskWorkGroupSizeZ_NV = 1,
                maxMeshViewCountNV = 4,
                maxMeshOutputVerticesEXT = 256,
                maxMeshOutputPrimitivesEXT = 512,
                maxMeshWorkGroupSizeX_EXT = 32,
                maxMeshWorkGroupSizeY_EXT = 1,
                maxMeshWorkGroupSizeZ_EXT = 1,
                maxTaskWorkGroupSizeX_EXT = 32,
                maxTaskWorkGroupSizeY_EXT = 1,
                maxTaskWorkGroupSizeZ_EXT = 1,
                maxMeshViewCountEXT = 4,
                maxDualSourceDrawBuffersEXT = 1,
                limits = new()
                {
                    nonInductiveForLoops = true,
                    whileLoops = true,
                    doWhileLoops = true,
                    generalUniformIndexing = true,
                    generalAttributeMatrixVectorIndexing = true,
                    generalVaryingIndexing = true,
                    generalSamplerIndexing = true,
                    generalVariableIndexing = true,
                    generalConstantMatrixVectorIndexing = true,
                },
            };
            return resource;
        }
    }

    public static ResultCode CreateShaderModuleFromSPIRV(this VkDevice vkDevice, nint spirv, size_t numBytes, out ShaderModuleState moduleOut, string? debugName)
    {
        moduleOut = ShaderModuleState.Null;
        VkShaderModule vkShaderModule = VkShaderModule.Null;
        unsafe
        {
            VkShaderModuleCreateInfo ci = new()
            {
                codeSize = numBytes,
                pCode = (uint32_t*)spirv,
            };
            {
                VkResult result = VK.vkCreateShaderModule(vkDevice, &ci, null, out vkShaderModule);
                if (result != VK.VK_SUCCESS)
                {
                    return ResultCode.InvalidState;
                }
            }
            if (GraphicsSettings.EnableDebug && !string.IsNullOrEmpty(debugName))
            {
                vkDevice.SetDebugObjectName(VK.VK_OBJECT_TYPE_SHADER_MODULE, (nuint)vkShaderModule.Handle, debugName);
            }

            HxDebug.Assert(vkShaderModule != VkShaderModule.Null);

            {
                spvReflectCreateShaderModule(numBytes, (void*)spirv, out var mdl).CheckResult();
                uint32_t pushConstantsSize = 0;

                for (uint32_t i = 0; i < mdl.push_constant_block_count; ++i)
                {
                    ref SpvReflectBlockVariable block = ref mdl.push_constant_blocks[i];
                    pushConstantsSize = Math.Max(pushConstantsSize, block.offset + block.size);
                }
                spvReflectDestroyShaderModule(&mdl);
                moduleOut = new ShaderModuleState() { ShaderModule = vkShaderModule, PushConstantsSize = pushConstantsSize };
                return ResultCode.Ok;
            }
        }
    }

    public static ResultCode CreateShaderModuleFromGLSL(this VkDevice vkDevice, ShaderStage stage, nint source, ShaderDefine[]? defines,
        in VkPhysicalDeviceLimits limits, out ShaderModuleState shaderModule, string? debugName = null)
    {
        shaderModule = ShaderModuleState.Null;
        HxDebug.Assert(source.Valid());
        if (source.IsNull())
        {
            logger.LogError("Shader source is empty");
            return ResultCode.ArgumentNull;
        }
        VkShaderStageFlags vkStage = stage.ToVk();
        StringBuilder builder = new();
        string src = Marshal.PtrToStringUTF8(source)!.Trim();
        if (!src.StartsWith("#version "))
        {
            if (vkStage == VkShaderStageFlags.TaskEXT || vkStage == VkShaderStageFlags.MeshEXT)
            {
                // add #version 460 to the shader source
                builder.Append(
                    """
                    #version 460
                    #extension GL_EXT_buffer_reference : require
                    #extension GL_EXT_buffer_reference_uvec2 : require
                    #extension GL_EXT_debug_printf : enable
                    #extension GL_EXT_nonuniform_qualifier : require
                    #extension GL_EXT_shader_explicit_arithmetic_types_float16 : require
                    #extension GL_EXT_mesh_shader : require
                    
                    """);
            }
            if (vkStage == VkShaderStageFlags.Vertex || vkStage == VkShaderStageFlags.Compute
                || vkStage == VkShaderStageFlags.TessellationControl || vkStage == VkShaderStageFlags.TessellationEvaluation)
            {
                builder.Append("""
                    #version 460
                    #extension GL_EXT_buffer_reference : require
                    #extension GL_EXT_buffer_reference_uvec2 : require
                    #extension GL_EXT_debug_printf : enable
                    #extension GL_EXT_nonuniform_qualifier : require
                    #extension GL_EXT_samplerless_texture_functions : require
                    #extension GL_EXT_shader_explicit_arithmetic_types_float16 : require

                    """);
            }
            if (vkStage == VkShaderStageFlags.Fragment)
            {
                builder.Append("""
                    #version 460
                    #extension GL_EXT_buffer_reference : require
                    #extension GL_EXT_buffer_reference_uvec2 : require
                    #extension GL_EXT_debug_printf : enable
                    #extension GL_EXT_nonuniform_qualifier : require
                    #extension GL_EXT_samplerless_texture_functions : require
                    #extension GL_EXT_shader_explicit_arithmetic_types_float16 : require

                    layout (set = 0, binding = 0) uniform texture2D kTextures2D[];
                    layout (set = 1, binding = 0) uniform texture3D kTextures3D[];
                    layout (set = 2, binding = 0) uniform textureCube kTexturesCube[];
                    layout (set = 3, binding = 0) uniform texture2D kTextures2DShadow[];
                    layout (set = 0, binding = 1) uniform sampler kSamplers[];
                    layout (set = 3, binding = 1) uniform samplerShadow kSamplersShadow[];

                    layout (set = 0, binding = 3) uniform sampler2D kSamplerYUV[];

                    vec4 textureBindless2D(uint textureid, uint samplerid, vec2 uv) {
                      return texture(nonuniformEXT(sampler2D(kTextures2D[textureid], kSamplers[samplerid])), uv);
                    }
                    vec4 textureBindless2DLod(uint textureid, uint samplerid, vec2 uv, float lod) {
                      return textureLod(nonuniformEXT(sampler2D(kTextures2D[textureid], kSamplers[samplerid])), uv, lod);
                    }
                    float textureBindless2DShadow(uint textureid, uint samplerid, vec3 uvw) {
                      return texture(nonuniformEXT(sampler2DShadow(kTextures2DShadow[textureid], kSamplersShadow[samplerid])), uvw);
                    }
                    ivec2 textureBindlessSize2D(uint textureid) {
                      return textureSize(nonuniformEXT(kTextures2D[textureid]), 0);
                    }
                    vec4 textureBindlessCube(uint textureid, uint samplerid, vec3 uvw) {
                      return texture(nonuniformEXT(samplerCube(kTexturesCube[textureid], kSamplers[samplerid])), uvw);
                    }
                    vec4 textureBindlessCubeLod(uint textureid, uint samplerid, vec3 uvw, float lod) {
                      return textureLod(nonuniformEXT(samplerCube(kTexturesCube[textureid], kSamplers[samplerid])), uvw, lod);
                    }
                    int textureBindlessQueryLevels2D(uint textureid) {
                      return textureQueryLevels(nonuniformEXT(kTextures2D[textureid]));
                    }
                    int textureBindlessQueryLevelsCube(uint textureid) {
                      return textureQueryLevels(nonuniformEXT(kTexturesCube[textureid]));
                    }

                    """);
            }
            builder.Append(src);
            src = builder.ToString();
        }
        var glslangResource = limits.GetGlslangResource();

        var result = ShaderUtils.CompileShader(vkStage, src, glslangResource, defines, out var spirv);
        if (result.HasError())
        {
            logger.LogError("Failed to compile shader: {REASON}", result.ToString());
            return ResultCode.CompileError;
        }
        unsafe
        {
            using var pSpirv = spirv.Pin();
            return vkDevice.CreateShaderModuleFromSPIRV((nint)pSpirv.Pointer, (size_t)(spirv.Length * sizeof(uint)), out shaderModule, debugName);
        }
    }
}

internal sealed class ShaderUtils
{
    static readonly ILogger logger = LogManager.Create<ShaderUtils>();

    public static ResultCode CompileShader(VkShaderStageFlags stage, string code, in ResourceLimits glslLangResource, ShaderDefine[]? defines, out uint[] outSPIRV)
    {
        outSPIRV = [];
        CompilationInput input = new()
        {
            language = SourceType.GLSL,
            stage = stage.ToGLSL(),
            client = ClientType.Vulkan,
            clientVersion = TargetClientVersion.Vulkan_1_3,
            targetLanguage = TargetLanguage.SPV,
            targetLanguageVersion = TargetLanguageVersion.SPV_1_6,
            code = code,
            defaultVersion = 100,
            defaultProfile = ShaderProfile.None,
            forceDefaultVersionAndProfile = false,
            forwardCompatible = false,
            messages = MessageType.Default,
            resourceLimits = glslLangResource,
        };

        using Shader shader = new(input);

        foreach(var define in defines ?? [])
        {
            shader.SetPreamble($"{define}\n");
        }

        if (!shader.Preprocess())
        {
            logger.LogError("Shader preprocessing failed:");
            logger.LogError("{LOG}", shader.GetInfoLog());
            logger.LogError("{LOG}", shader.GetDebugLog());
            HxDebug.Assert(false);
            return ResultCode.CompileError;
        }

        if (!shader.Parse())
        {
            logger.LogError("Shader parsing failed:");
            logger.LogError("{LOG}", shader.GetInfoLog());
            logger.LogError("{LOG}", shader.GetDebugLog());
            logger.LogError("{LOG}", shader.GetPreprocessedCode());
            HxDebug.Assert(false);
            return ResultCode.CompileError;
        }

        using var program = new Program();
        program.AddShader(shader);

        if (!program.Link(MessageType.SpvRules | MessageType.VulkanRules))
        {
            logger.LogError("Shader linking failed:\n");
            logger.LogError("{LOG}", program.GetInfoLog());
            logger.LogError("{LOG}", program.GetDebugLog());
            HxDebug.Assert(false);
            return ResultCode.CompileError;
        }

        SPIRVOptions options = new()
        {
            generateDebugInfo = true,
            stripDebugInfo = false,
            disableOptimizer = false,
            optimizeSize = true,
            disassemble = false,
            validate = true,
            emitNonsemanticShaderDebugInfo = false,
            emitNonsemanticShaderDebugSource = false,
        };

        if (!program.GenerateSPIRV(out outSPIRV, stage.ToGLSL(), options))
        {
            logger.LogError("Shader SPIR-V generation failed:\n");
            logger.LogError("{LOG}", program.GetInfoLog());
            logger.LogError("{LOG}", program.GetDebugLog());
            HxDebug.Assert(false);
            return ResultCode.CompileError;
        }

        var message = program.GetSPIRVMessages();

        if (!string.IsNullOrWhiteSpace(message))
        {
            logger.LogWarning("{LOG}", message);
        }

        logger.LogDebug("Shader SPIR-V code generated successfully. Size: {Size} bytes", outSPIRV.Length);
        return ResultCode.Ok;
    }


}
