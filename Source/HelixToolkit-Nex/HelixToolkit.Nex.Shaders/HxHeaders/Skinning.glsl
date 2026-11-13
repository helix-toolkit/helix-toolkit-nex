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

// Morph target displacement of one vertex. Deltas of all targets are stored target-major:
// delta[target * vertexCount + vertex]. Missing normal/tangent deltas are zero.
@code_gen
struct GpuMorphDelta {
    vec3 position;
    float _padding0;
    vec3 normal;
    float _padding1;
    vec3 tangent;
    float _padding2;
};

// A non-zero morph target weight: only active targets are uploaded.
@code_gen
struct GpuMorphWeight {
    uint target;
    float weight;
};

// Push constants for the vertex deformation pass: morph targets first, then linear blend skinning.
@code_gen
struct SkinVertexPC {
    uint64_t paletteAddress;      // Skinning palette (mat4[]), 0 when the mesh is not skinned.
    uint64_t srcVertexAddress;    // Bind-pose positions (vec4[]).
    uint64_t srcPropsAddress;     // Bind-pose vertex props (GpuVertexProps[]), may be 0.
    uint64_t influenceAddress;    // Joint influences (GpuJointInfluence[]), 0 when not skinned.
    uint64_t dstVertexAddress;    // Deformed positions (vec4[]).
    uint64_t dstPropsAddress;     // Deformed vertex props (GpuVertexProps[]), may be 0.
    uint64_t morphDeltaAddress;   // Morph target deltas (GpuMorphDelta[]), 0 when not morphed.
    uint64_t morphWeightAddress;  // Active morph weights (GpuMorphWeight[]).
    uint vertexCount;
    uint jointCount;
    uint morphWeightCount;        // Number of active morph weights; 0 skips morphing.
    uint flags;                   // SKIN_VERTEX_FLAG_* bits.
};

// The geometry has generated flat normals with the vertices of each triangle stored consecutively:
// recompute the face normal from the morphed triangle.
const uint SKIN_VERTEX_FLAG_FLAT_NORMALS = 1u;

// The geometry has generated (not authored) tangents: keep them consistent with the morphed surface.
// With flat normals the face tangent is recomputed from the morphed triangle and its texture
// coordinates; otherwise the tangent is re-orthogonalized against the morphed normal.
const uint SKIN_VERTEX_FLAG_GENERATED_TANGENTS = 2u;

// With SKIN_VERTEX_FLAG_GENERATED_TANGENTS: the tangents were generated from the second texture
// coordinate set (texCoord1) because the normal map uses it, so the face tangent must be too.
const uint SKIN_VERTEX_FLAG_GENERATED_TANGENTS_UV1 = 4u;
