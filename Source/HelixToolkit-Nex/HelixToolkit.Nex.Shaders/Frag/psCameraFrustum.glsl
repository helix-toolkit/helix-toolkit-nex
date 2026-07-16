#include "HxHeaders/HeaderFrag.glsl"

layout(location = 0) in flat vec4 color;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = color;
}
