#extension GL_EXT_buffer_reference : require
#extension GL_EXT_buffer_reference_uvec2 : require
#extension GL_EXT_debug_printf : enable
#extension GL_EXT_nonuniform_qualifier : require
#extension GL_EXT_samplerless_texture_functions : require
#extension GL_EXT_shader_explicit_arithmetic_types_float16 : require
#extension GL_EXT_shader_explicit_arithmetic_types_int64   : require
#extension GL_EXT_scalar_block_layout : require
#extension GL_EXT_buffer_reference2 : require

// Binding slot must be the same as Graphics.Vulkan.Bindings
#define BindingTextures 0

#define BindingSamplers 1

#define BindingStorageImages 2

#define BindingYUVImages 3


layout (set = 0, binding = BindingTextures) uniform texture2D kTextures2D[];
layout (set = 0, binding = BindingTextures) uniform texture3D kTextures3D[];
layout (set = 0, binding = BindingTextures) uniform textureCube kTexturesCube[];
layout (set = 0, binding = BindingTextures) uniform texture2D kTextures2DShadow[];
layout (set = 0, binding = BindingSamplers) uniform sampler kSamplers[];
layout (set = 0, binding = BindingSamplers) uniform samplerShadow kSamplersShadow[];

layout (set = 0, binding = BindingYUVImages) uniform sampler2D kSamplerYUV[];
