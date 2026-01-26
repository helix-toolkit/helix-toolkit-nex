// Forward+ light structures
// Note: struct Light is defined in PBRFunctions.glsl which must be included

@code_gen
struct LightGridTile {
    uint lightCount;
    uint lightIndexOffset;
};

layout(buffer_reference, scalar) readonly buffer LightGridBuffer {
    LightGridTile tiles[]; // x=lightCount, y=lightIndexOffset
};

layout(buffer_reference, scalar) readonly buffer LightIndexBuffer {
    uint indices[];
};
