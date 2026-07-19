using HelixToolkit.Nex.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Textures.Tests;

/// <summary>
/// Tests for <see cref="Image.NewCube(System.Collections.Generic.IReadOnlyList{Image})"/>,
/// the six-faces-to-cubemap assembly used by the environment-map cubemap API.
/// </summary>
[TestClass]
public class ImageCubeTests
{
    private const int FaceSize = 4;

    private static Image MakeFace(uint fill, Format format = Format.RGBA_UN8, int size = FaceSize)
    {
        var face = Image.New2D(size, size, 1, format);
        if (format == Format.RGBA_UN8)
        {
            var pb = face.GetPixelBuffer(0, 0);
            for (var y = 0; y < size; ++y)
            {
                for (var x = 0; x < size; ++x)
                {
                    pb.SetPixel(x, y, fill);
                }
            }
        }
        return face;
    }

    private static Image[] MakeSixFaces() =>
        [
            MakeFace(1),
            MakeFace(2),
            MakeFace(3),
            MakeFace(4),
            MakeFace(5),
            MakeFace(6),
        ];

    private static void DisposeAll(Image[] faces)
    {
        foreach (var f in faces)
            f?.Dispose();
    }

    [TestMethod]
    public void NewCube_FromSixFaces_ProducesCubeDescription()
    {
        var faces = MakeSixFaces();
        try
        {
            using var cube = Image.NewCube(faces);

            Assert.AreEqual(TextureDimension.TextureCube, cube.Description.Dimension);
            Assert.AreEqual(6, cube.Description.ArraySize);
            Assert.AreEqual(FaceSize, cube.Description.Width);
            Assert.AreEqual(FaceSize, cube.Description.Height);
            Assert.AreEqual(1, cube.Description.MipLevels);
            Assert.AreEqual(Format.RGBA_UN8, cube.Description.Format);
        }
        finally
        {
            DisposeAll(faces);
        }
    }

    [TestMethod]
    public void NewCube_FromSixFaces_CopiesEachFaceToMatchingLayer()
    {
        var faces = MakeSixFaces();
        try
        {
            using var cube = Image.NewCube(faces);

            // Each face i was filled with value (i + 1); verify it landed in cube layer i,
            // checking both corners to confirm full-buffer (not just first-pixel) copy.
            for (var i = 0; i < 6; ++i)
            {
                var pb = cube.GetPixelBuffer(i, 0);
                Assert.AreEqual((uint)(i + 1), pb.GetPixel<uint>(0, 0), $"layer {i} top-left");
                Assert.AreEqual(
                    (uint)(i + 1),
                    pb.GetPixel<uint>(FaceSize - 1, FaceSize - 1),
                    $"layer {i} bottom-right"
                );
            }
        }
        finally
        {
            DisposeAll(faces);
        }
    }

    [TestMethod]
    public void NewCube_WrongFaceCount_Throws()
    {
        var faces = new[] { MakeFace(1), MakeFace(2), MakeFace(3) };
        try
        {
            Assert.ThrowsException<ArgumentException>(() => Image.NewCube(faces));
        }
        finally
        {
            DisposeAll(faces);
        }
    }

    [TestMethod]
    public void NewCube_NonSquareFace_Throws()
    {
        var faces = MakeSixFaces();
        faces[2].Dispose();
        faces[2] = Image.New2D(FaceSize, FaceSize * 2, 1, Format.RGBA_UN8);
        try
        {
            Assert.ThrowsException<ArgumentException>(() => Image.NewCube(faces));
        }
        finally
        {
            DisposeAll(faces);
        }
    }

    [TestMethod]
    public void NewCube_MismatchedFaceSize_Throws()
    {
        var faces = MakeSixFaces();
        faces[4].Dispose();
        faces[4] = MakeFace(5, size: FaceSize * 2);
        try
        {
            Assert.ThrowsException<ArgumentException>(() => Image.NewCube(faces));
        }
        finally
        {
            DisposeAll(faces);
        }
    }

    [TestMethod]
    public void NewCube_MismatchedFaceFormat_Throws()
    {
        var faces = MakeSixFaces();
        faces[5].Dispose();
        faces[5] = Image.New2D(FaceSize, FaceSize, 1, Format.RG_UN8);
        try
        {
            Assert.ThrowsException<ArgumentException>(() => Image.NewCube(faces));
        }
        finally
        {
            DisposeAll(faces);
        }
    }
}
