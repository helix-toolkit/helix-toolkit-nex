using System.Numerics;

namespace HelixToolkit.Nex.Maths.Tests;

[TestClass]
public sealed class MatrixTests
{
    private static void VerifyMatrix(
        in Matrix4x4 expected,
        in Matrix4x4 actual,
        float epsilon = 1e-4f
    )
    {
        Assert.IsTrue(
            MathUtil.NearEqual(expected.M11, actual.M11),
            $"M11 expected {expected.M11}, actual {actual.M11}"
        );
        Assert.IsTrue(
            MathUtil.NearEqual(expected.M12, actual.M12),
            $"M12 expected {expected.M12}, actual {actual.M12}"
        );
        Assert.IsTrue(
            MathUtil.NearEqual(expected.M13, actual.M13),
            $"M13 expected {expected.M13}, actual {actual.M13}"
        );
        Assert.IsTrue(
            MathUtil.NearEqual(expected.M14, actual.M14),
            $"M14 expected {expected.M14}, actual {actual.M14}"
        );

        Assert.IsTrue(
            MathUtil.NearEqual(expected.M21, actual.M21),
            $"M21 expected {expected.M21}, actual {actual.M21}"
        );
        Assert.IsTrue(
            MathUtil.NearEqual(expected.M22, actual.M22),
            $"M22 expected {expected.M22}, actual {actual.M22}"
        );
        Assert.IsTrue(
            MathUtil.NearEqual(expected.M23, actual.M23),
            $"M23 expected {expected.M23}, actual {actual.M23}"
        );
        Assert.IsTrue(
            MathUtil.NearEqual(expected.M24, actual.M24),
            $"M24 expected {expected.M24}, actual {actual.M24}"
        );

        Assert.IsTrue(
            MathUtil.NearEqual(expected.M31, actual.M31),
            $"M31 expected {expected.M31}, actual {actual.M31}"
        );
        Assert.IsTrue(
            MathUtil.NearEqual(expected.M32, actual.M32),
            $"M32 expected {expected.M32}, actual {actual.M32}"
        );
        Assert.IsTrue(
            MathUtil.NearEqual(expected.M33, actual.M33),
            $"M33 expected {expected.M33}, actual {actual.M33}"
        );
        Assert.IsTrue(
            MathUtil.NearEqual(expected.M34, actual.M34),
            $"M34 expected {expected.M34}, actual {actual.M34}"
        );

        Assert.IsTrue(
            MathUtil.NearEqual(expected.M41, actual.M41),
            $"M41 expected {expected.M41}, actual {actual.M41}"
        );
        Assert.IsTrue(
            MathUtil.NearEqual(expected.M42, actual.M42),
            $"M42 expected {expected.M42}, actual {actual.M42}"
        );
        Assert.IsTrue(
            MathUtil.NearEqual(expected.M43, actual.M43),
            $"M43 expected {expected.M43}, actual {actual.M43}"
        );
        Assert.IsTrue(
            MathUtil.NearEqual(expected.M44, actual.M44),
            $"M44 expected {expected.M44}, actual {actual.M44}"
        );
    }

    [TestMethod]
    [DataRow(MathF.PI / 4, 1, 0.1f, 1000f)]
    [DataRow(MathF.PI / 3, 0.8f, 0.01f, 100f)]
    public void PerspectiveRHInverseZ(float fov, float aspect, float near, float far)
    {
        var perspective = MatrixHelper.PerspectiveFovRHReverseZ(fov, aspect, near, far);
        var expected = Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, near, far);
        expected *= new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 0, 0, 0, 1, 1);
        VerifyMatrix(expected, perspective);
    }

    [TestMethod]
    [DataRow(MathF.PI / 4, 1, 0.1f, 1000f)]
    [DataRow(MathF.PI / 4, 1, 0.1f, float.PositiveInfinity)]
    [DataRow(MathF.PI / 3, 0.8f, 0.01f, 100f)]
    [DataRow(MathF.PI / 3, 0.8f, 0.01f, float.PositiveInfinity)]
    public void InvertPerspectiveRHReverseZTest(float fov, float aspect, float near, float far)
    {
        var perspective = MatrixHelper.PerspectiveFovRHReverseZ(fov, aspect, near, far);
        var inverted = MatrixHelper.InversedPerspectiveFovRHReverseZ(fov, aspect, near, far);
        var combined = Matrix4x4.Multiply(perspective, inverted);
        VerifyMatrix(Matrix4x4.Identity, combined);
    }

    [TestMethod]
    [DataRow(1024, 768, 0.1f, 1000f)]
    [DataRow(1080, 800, 0.01f, 100f)]
    public void OrthoRHInverseZ(float width, float height, float near, float far)
    {
        var ortho = MatrixHelper.OrthoRHReverseZ(width, height, near, far);
        var expected = Matrix4x4.CreateOrthographic(width, height, near, far);
        expected *= new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 0, 0, 0, 1, 1);
        VerifyMatrix(expected, ortho);
    }

    [TestMethod]
    [DataRow(1024, 768, 0.1f, 1000f)]
    [DataRow(1080, 800, 0.01f, 100f)]
    public void InvertOrthoRHReverseZTest(float width, float height, float near, float far)
    {
        var ortho = MatrixHelper.OrthoRHReverseZ(width, height, near, far);
        var inverted = MatrixHelper.InversedOrthoRHReverseZ(width, height, near, far);
        var combined = Matrix4x4.Multiply(ortho, inverted);
        VerifyMatrix(Matrix4x4.Identity, combined);
    }
}
