#version 450

layout(location = 0) in vec2 inUV;
layout(location = 0) out vec4 outColor;

layout(push_constant) uniform PushConstants {
    int fps;
} pc;

float rect(vec2 uv, vec2 p, vec2 s) {
    vec2 d = abs(uv - p) - s;
    return float(max(d.x, d.y) < 0.0);
}

float segment(vec2 uv, int n) {
    int masks[10] = int[](0x3F,0x06,0x5B,0x4F,0x66,0x6D,0x7D,0x07,0x7F,0x6F);
    int m = masks[n];
    float res = 0.0;
    
    // Proportions adjusted for a single digit cell
    res += float((m & 0x01) != 0) * rect(uv, vec2(0.5, 0.85), vec2(0.25, 0.03)); // a
    res += float((m & 0x02) != 0) * rect(uv, vec2(0.75, 0.65), vec2(0.04, 0.18)); // b
    res += float((m & 0x04) != 0) * rect(uv, vec2(0.75, 0.35), vec2(0.04, 0.18)); // c
    res += float((m & 0x08) != 0) * rect(uv, vec2(0.5, 0.15), vec2(0.25, 0.03)); // d
    res += float((m & 0x10) != 0) * rect(uv, vec2(0.25, 0.35), vec2(0.04, 0.18)); // e
    res += float((m & 0x20) != 0) * rect(uv, vec2(0.25, 0.65), vec2(0.04, 0.18)); // f
    res += float((m & 0x40) != 0) * rect(uv, vec2(0.5, 0.50), vec2(0.25, 0.03)); // g
    
    return res;
}

void main() {
    // We assume the quad is wide (e.g., 3:1 aspect ratio)
    // Divide the quad into 3 horizontal cells for 3 digits
    vec2 uv = vec2(inUV.x, 1 - inUV.y); // Flip Y for texture coordinates
    float numDigits = 3.0;
    float xScaled = uv.x * numDigits;
    int digitIndex = int(xScaled); 
    vec2 digitUV = vec2(fract(xScaled), uv.y);
    
    // Calculate which digit to show based on position
    int val = pc.fps;
    int digit = 0;
    if (digitIndex == 0) digit = (val / 100) % 10;
    else if (digitIndex == 1) digit = (val / 10) % 10;
    else if (digitIndex == 2) digit = val % 10;
    
    float mask = segment(digitUV, digit);
    
    // Simple logic: if not part of a segment, make it transparent or dark
    if (mask < 0.5) {
        outColor = vec4(0.0, 0.0, 0.0, 0.4); // Semi-transparent black background
    } else {
        outColor = vec4(0.0, 1.0, 0.0, 1.0); // Bright green segments
    }
}
