// Shared GLSL utility functions.

// Returns a matrix that transforms normals for the linear transform m: the cofactor matrix of m,
// i.e. the inverse-transpose scaled by det(m), with the sign of det(m) so mirroring transforms keep
// normals facing outward. Correct under non-uniform scale; the result must be normalized. A singular
// m yields a zero vector instead of NaNs.
mat3 normalMatrix(mat3 m) {
    mat3 cofactor = mat3(cross(m[1], m[2]), cross(m[2], m[0]), cross(m[0], m[1]));
    return dot(m[0], cofactor[0]) < 0.0 ? -cofactor : cofactor;
}
