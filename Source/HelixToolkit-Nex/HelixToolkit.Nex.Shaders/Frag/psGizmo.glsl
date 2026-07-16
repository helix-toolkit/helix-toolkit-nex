#include "HxHeaders/HeaderFrag.glsl"

// Solid-handle fragment shader for gizmo rendering.
//
// Writes the handle colour to color attachment 0 (the tone-mapped LDR target)
// and the gizmo pick to color attachment 1 (the RG_F32 entity-id target) so the
// existing picking readback path can resolve gizmo handle picks (Req 1.4). The
// pick is carried as two pre-packed flat varyings from the vertex shader
// (encodedR / encodedG, produced by Utils.PackGizmoInfo on the CPU) and written
// straight into the RG_F32 target's two channels via uintBitsToFloat. R carries
// the world-id-zero + Gizmo encoding-type discriminator plus the owning gizmo
// entity id; G carries the packed GizmoHandleId (mode + axis) (Req 4.1, 4.2,
// 4.4). This matches the raw-bit convention used by the mesh/line pipelines
// (blend is disabled on the entity-id attachment so the bits are written
// unmodified).

layout(location = 0) in flat vec4 v_color;
layout(location = 1) in flat uint v_encodedR;
layout(location = 2) in flat uint v_encodedG;

layout(location = 0) out vec4 outColor;
layout(location = 1) out vec2 outEntityId;

void main() {
    outColor = v_color;

    // Write both pre-packed pick words: R (discriminator + owning entity id) and
    // G (packed GizmoHandleId), reinterpreted into the RG_F32 target (Req 4.4).
    outEntityId = vec2(uintBitsToFloat(v_encodedR), uintBitsToFloat(v_encodedG));
}
