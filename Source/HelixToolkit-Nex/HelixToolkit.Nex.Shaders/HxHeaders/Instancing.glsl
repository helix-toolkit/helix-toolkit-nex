@code_gen
struct InstanceTransform {
    vec4 quaternion;   // xyzw
    vec3 translation;  // xyz
    float scale;       // uniform scale
};


@code_gen
struct InstanceParams {
    vec4 albedo;
    vec2 uvOffset;
    vec2 uvScale;
};

// Function to convert quaternion to a 3x3 rotation matrix
mat3 quatToMat3(in vec4 q) {
    mat3 m;
    float qx2 = q.x + q.x; float qy2 = q.y + q.y; float qz2 = q.z + q.z;
    float qxx = q.x * qx2; float qxy = q.x * qy2; float qxz = q.x * qz2;
    float qyy = q.y * qy2; float qyz = q.y * qz2; float qzz = q.z * qz2;
    float qwx = q.w * qx2; float qwy = q.w * qy2; float qwz = q.w * qz2;

    m[0][0] = 1.0 - (qyy + qzz); m[1][0] = qxy - qwz;         m[2][0] = qxz + qwy;
    m[0][1] = qxy + qwz;         m[1][1] = 1.0 - (qxx + qzz); m[2][1] = qyz - qwx;
    m[0][2] = qxz - qwy;         m[1][2] = qyz + qwx;         m[2][2] = 1.0 - (qxx + qyy);
    return m;
}

mat4 instanceTransfromToMat4(in InstanceTransform inst) {
//    mat3 rot = quatToMat3(inst.quaternion);
//    mat4 m = mat4(rot);
//    m[3].xyz = inst.translation;
//    m[0] *= inst.scale;
//    m[1] *= inst.scale;
//    m[2] *= inst.scale;
//    return m;
    vec4 q = inst.quaternion;
    
    // 1. Vectorize the multiplications (Maps to fast FMA hardware instructions)
    vec3 q2 = q.xyz * 2.0;
    
    vec3 wx_wy_wz = q.w * q2;
    vec3 xx_xy_xz = q.x * q2;
    vec2 yy_yz    = q.y * q2.yz;
    float zz      = q.z * q2.z;
    
    // 2. Construct the scaled columns directly using vector addition/subtraction
    vec3 col0 = vec3(
        1.0 - yy_yz.x - zz,
        xx_xy_xz.y + wx_wy_wz.z,
        xx_xy_xz.z - wx_wy_wz.y
    ) * inst.scale;

    vec3 col1 = vec3(
        xx_xy_xz.y - wx_wy_wz.z,
        1.0 - xx_xy_xz.x - zz,
        yy_yz.y + wx_wy_wz.x
    ) * inst.scale;

    vec3 col2 = vec3(
        xx_xy_xz.z + wx_wy_wz.y,
        yy_yz.y - wx_wy_wz.x,
        1.0 - xx_xy_xz.x - yy_yz.x
    ) * inst.scale;

    // 3. Assemble the final mat4 directly with no intermediate copies
    return mat4(
        vec4(col0, 0.0),
        vec4(col1, 0.0),
        vec4(col2, 0.0),
        vec4(inst.translation, 1.0)
    );
}

vec3 transformCoord(in vec3 wp, in mat3 rot, float scale, in vec3 translation) {
    vec3 scaledPos = wp * scale;
    vec3 rotatedPos = rot * scaledPos;
    return rotatedPos + translation;
}

// Optimized quaternion rotation function
vec3 rotateQuaternion(vec3 v, vec4 q) {
    // t = 2 * cross(q.xyz, v)
    vec3 t = 2.0 * cross(q.xyz, v);
    // v' = v + q.w * t + cross(q.xyz, t)
    return v + q.w * t + cross(q.xyz, t);
}

vec3 transformCoordQuaternion(in vec3 wp, in vec4 quaternion, float scale, in vec3 translation) {
    vec3 scaledPos = wp * scale;
    vec3 rotatedPos = rotateQuaternion(scaledPos, quaternion);
    return rotatedPos + translation;
}
