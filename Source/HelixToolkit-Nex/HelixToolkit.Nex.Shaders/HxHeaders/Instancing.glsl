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
    mat3 rot = quatToMat3(inst.quaternion);
    mat4 m = mat4(rot);
    m[3].xyz = inst.translation;
    m[0] *= inst.scale;
    m[1] *= inst.scale;
    m[2] *= inst.scale;
    return m;
}

vec3 transformCoord(in vec3 wp, in mat3 rot, float scale, in vec3 translation) {
    vec3 scaledPos = wp * scale;
    vec3 rotatedPos = rot * scaledPos;
    return rotatedPos + translation;
}
