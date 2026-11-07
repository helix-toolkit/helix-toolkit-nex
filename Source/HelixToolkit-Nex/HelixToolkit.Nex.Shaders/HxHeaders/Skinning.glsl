// Per-vertex bone influences for linear blend skinning.
// Joint indices index into the skin's joint list; weights are normalized (sum to 1).
@code_gen
struct GpuJointInfluence {
    uint joint0;
    uint joint1;
    uint joint2;
    uint joint3;
    vec4 weights;
};

// Push constants for the skin palette pass: palette[j] = jointMatrices[j] * inverseBindMatrices[j].
@code_gen
struct SkinPalettePC {
    uint64_t jointMatrixAddress;  // Joint world matrices relative to the skinned mesh node (mat4[]).
    uint64_t inverseBindAddress;  // Inverse bind matrices of the skeleton (mat4[]).
    uint64_t paletteAddress;      // Output skinning palette (mat4[]).
    uint jointCount;
    uint _padding0;
};

// Push constants for the vertex skinning pass.
@code_gen
struct SkinVertexPC {
    uint64_t paletteAddress;      // Skinning palette (mat4[]).
    uint64_t srcVertexAddress;    // Bind-pose positions (vec4[]).
    uint64_t srcPropsAddress;     // Bind-pose vertex props (GpuVertexProps[]), may be 0.
    uint64_t influenceAddress;    // Joint influences (GpuJointInfluence[]).
    uint64_t dstVertexAddress;    // Skinned positions (vec4[]).
    uint64_t dstPropsAddress;     // Skinned vertex props (GpuVertexProps[]), may be 0.
    uint vertexCount;
    uint jointCount;
};
