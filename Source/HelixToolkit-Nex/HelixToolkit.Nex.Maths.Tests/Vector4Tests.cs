using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace HelixToolkit.Nex.Maths.Tests;

[TestClass]
public class Vector4Tests
{
    private const float Epsilon = 1e-5f;

    #region MinMax Tests

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void MinMax_EmptyList_ReturnsZero(bool fastList)
    {
        IList<Vector4> vectors = fastList ? new FastList<Vector4>() : new List<Vector4>();

        Vector4Helper.MinMax(vectors, out var min, out var max);

        Assert.AreEqual(Vector4.Zero, min);
        Assert.AreEqual(Vector4.Zero, max);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void MinMax_SingleVector_ReturnsSameVector(bool fastList)
    {
        var vector = new Vector4(1.0f, 2.0f, 3.0f, 4.0f);
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4> { vector }
            : new List<Vector4> { vector };

        Vector4Helper.MinMax(vectors, out var min, out var max);

        AssertVector4Equal(vector, min);
        AssertVector4Equal(vector, max);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void MinMax_TwoVectors_ReturnsCorrectMinMax(bool fastList)
    {
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(1.0f, 5.0f, 3.0f, 2.0f),
                new Vector4(4.0f, 2.0f, 6.0f, 1.0f),
            }
            : new List<Vector4>
            {
                new Vector4(1.0f, 5.0f, 3.0f, 2.0f),
                new Vector4(4.0f, 2.0f, 6.0f, 1.0f),
            };

        Vector4Helper.MinMax(vectors, out var min, out var max);

        AssertVector4Equal(new Vector4(1.0f, 2.0f, 3.0f, 1.0f), min);
        AssertVector4Equal(new Vector4(4.0f, 5.0f, 6.0f, 2.0f), max);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void MinMax_FourVectors_SSEPath_ReturnsCorrectMinMax(bool fastList)
    {
        // Test exactly 4 vectors to hit the SSE path
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(1.0f, 8.0f, 3.0f, 9.0f),
                new Vector4(4.0f, 2.0f, 6.0f, 1.0f),
                new Vector4(7.0f, 5.0f, 0.0f, 4.0f),
                new Vector4(2.0f, 9.0f, 8.0f, 3.0f),
            }
            : new List<Vector4>
            {
                new Vector4(1.0f, 8.0f, 3.0f, 9.0f),
                new Vector4(4.0f, 2.0f, 6.0f, 1.0f),
                new Vector4(7.0f, 5.0f, 0.0f, 4.0f),
                new Vector4(2.0f, 9.0f, 8.0f, 3.0f),
            };

        Vector4Helper.MinMax(vectors, out var min, out var max);

        AssertVector4Equal(new Vector4(1.0f, 2.0f, 0.0f, 1.0f), min);
        AssertVector4Equal(new Vector4(7.0f, 9.0f, 8.0f, 9.0f), max);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void MinMax_EightVectors_AVXPath_ReturnsCorrectMinMax(bool fastList)
    {
        // Test exactly 8 vectors to hit the AVX path
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(1.0f, 8.0f, 3.0f, 9.0f),
                new Vector4(4.0f, 2.0f, 6.0f, 1.0f),
                new Vector4(7.0f, 5.0f, 0.0f, 4.0f),
                new Vector4(2.0f, 9.0f, 8.0f, 3.0f),
                new Vector4(5.0f, 1.0f, 4.0f, 7.0f),
                new Vector4(3.0f, 6.0f, 2.0f, 8.0f),
                new Vector4(9.0f, 4.0f, 7.0f, 2.0f),
                new Vector4(6.0f, 7.0f, 1.0f, 5.0f),
            }
            : new List<Vector4>
            {
                new Vector4(1.0f, 8.0f, 3.0f, 9.0f),
                new Vector4(4.0f, 2.0f, 6.0f, 1.0f),
                new Vector4(7.0f, 5.0f, 0.0f, 4.0f),
                new Vector4(2.0f, 9.0f, 8.0f, 3.0f),
                new Vector4(5.0f, 1.0f, 4.0f, 7.0f),
                new Vector4(3.0f, 6.0f, 2.0f, 8.0f),
                new Vector4(9.0f, 4.0f, 7.0f, 2.0f),
                new Vector4(6.0f, 7.0f, 1.0f, 5.0f),
            };

        Vector4Helper.MinMax(vectors, out var min, out var max);

        AssertVector4Equal(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), min);
        AssertVector4Equal(new Vector4(9.0f, 9.0f, 8.0f, 9.0f), max);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void MinMax_NonAlignedCount_ReturnsCorrectMinMax(bool fastList)
    {
        // Test with 13 vectors to ensure remainder processing works correctly
        IList<Vector4> vectors = fastList ? new FastList<Vector4>() : new List<Vector4>();
        for (int i = 0; i < 13; i++)
        {
            vectors.Add(new Vector4(i, 13 - i, i * 0.5f, (13 - i) * 0.5f));
        }

        Vector4Helper.MinMax(vectors, out var min, out var max);

        AssertVector4Equal(new Vector4(0.0f, 1.0f, 0.0f, 0.5f), min);
        AssertVector4Equal(new Vector4(12.0f, 13.0f, 6.0f, 6.5f), max);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void MinMax_NegativeValues_ReturnsCorrectMinMax(bool fastList)
    {
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(-5.0f, -2.0f, -8.0f, -1.0f),
                new Vector4(-3.0f, -6.0f, -4.0f, -9.0f),
                new Vector4(-7.0f, -1.0f, -2.0f, -3.0f),
                new Vector4(-4.0f, -8.0f, -6.0f, -5.0f),
            }
            : new List<Vector4>
            {
                new Vector4(-5.0f, -2.0f, -8.0f, -1.0f),
                new Vector4(-3.0f, -6.0f, -4.0f, -9.0f),
                new Vector4(-7.0f, -1.0f, -2.0f, -3.0f),
                new Vector4(-4.0f, -8.0f, -6.0f, -5.0f),
            };

        Vector4Helper.MinMax(vectors, out var min, out var max);

        AssertVector4Equal(new Vector4(-7.0f, -8.0f, -8.0f, -9.0f), min);
        AssertVector4Equal(new Vector4(-3.0f, -1.0f, -2.0f, -1.0f), max);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void MinMax_MixedPositiveNegative_ReturnsCorrectMinMax(bool fastList)
    {
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(-5.0f, 2.0f, -8.0f, 1.0f),
                new Vector4(3.0f, -6.0f, 4.0f, -9.0f),
                new Vector4(-7.0f, 1.0f, 2.0f, 3.0f),
                new Vector4(4.0f, -8.0f, -6.0f, 5.0f),
            }
            : new List<Vector4>
            {
                new Vector4(-5.0f, 2.0f, -8.0f, 1.0f),
                new Vector4(3.0f, -6.0f, 4.0f, -9.0f),
                new Vector4(-7.0f, 1.0f, 2.0f, 3.0f),
                new Vector4(4.0f, -8.0f, -6.0f, 5.0f),
            };

        Vector4Helper.MinMax(vectors, out var min, out var max);

        AssertVector4Equal(new Vector4(-7.0f, -8.0f, -8.0f, -9.0f), min);
        AssertVector4Equal(new Vector4(4.0f, 2.0f, 4.0f, 5.0f), max);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void MinMax_WithStartAndCount_ReturnsCorrectMinMax(bool fastList)
    {
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(100.0f, 100.0f, 100.0f, 100.0f), // Skip these
                new Vector4(200.0f, 200.0f, 200.0f, 200.0f),
                new Vector4(1.0f, 8.0f, 3.0f, 9.0f), // Process from here
                new Vector4(4.0f, 2.0f, 6.0f, 1.0f),
                new Vector4(7.0f, 5.0f, 0.0f, 4.0f),
                new Vector4(2.0f, 9.0f, 8.0f, 3.0f),
                new Vector4(300.0f, 300.0f, 300.0f, 300.0f), // Skip this
            }
            : new List<Vector4>
            {
                new Vector4(100.0f, 100.0f, 100.0f, 100.0f), // Skip these
                new Vector4(200.0f, 200.0f, 200.0f, 200.0f),
                new Vector4(1.0f, 8.0f, 3.0f, 9.0f), // Process from here
                new Vector4(4.0f, 2.0f, 6.0f, 1.0f),
                new Vector4(7.0f, 5.0f, 0.0f, 4.0f),
                new Vector4(2.0f, 9.0f, 8.0f, 3.0f),
                new Vector4(300.0f, 300.0f, 300.0f, 300.0f), // Skip this
            };

        Vector4Helper.MinMax(vectors, 2, 4, out var min, out var max);

        AssertVector4Equal(new Vector4(1.0f, 2.0f, 0.0f, 1.0f), min);
        AssertVector4Equal(new Vector4(7.0f, 9.0f, 8.0f, 9.0f), max);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [ExpectedException(typeof(ArgumentOutOfRangeException))]
    public void MinMax_StartAndCountExceedLength_ThrowsException(bool fastList)
    {
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(1.0f, 2.0f, 3.0f, 4.0f),
                new Vector4(5.0f, 6.0f, 7.0f, 8.0f),
            }
            : new List<Vector4>
            {
                new Vector4(1.0f, 2.0f, 3.0f, 4.0f),
                new Vector4(5.0f, 6.0f, 7.0f, 8.0f),
            };

        Vector4Helper.MinMax(vectors, 1, 5, out _, out _);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void MinMax_AllComponentsSame_ReturnsCorrectMinMax(bool fastList)
    {
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(5.0f, 5.0f, 5.0f, 5.0f),
                new Vector4(5.0f, 5.0f, 5.0f, 5.0f),
                new Vector4(5.0f, 5.0f, 5.0f, 5.0f),
                new Vector4(5.0f, 5.0f, 5.0f, 5.0f),
            }
            : new List<Vector4>
            {
                new Vector4(5.0f, 5.0f, 5.0f, 5.0f),
                new Vector4(5.0f, 5.0f, 5.0f, 5.0f),
                new Vector4(5.0f, 5.0f, 5.0f, 5.0f),
                new Vector4(5.0f, 5.0f, 5.0f, 5.0f),
            };

        Vector4Helper.MinMax(vectors, out var min, out var max);

        AssertVector4Equal(new Vector4(5.0f, 5.0f, 5.0f, 5.0f), min);
        AssertVector4Equal(new Vector4(5.0f, 5.0f, 5.0f, 5.0f), max);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void MinMax_ExtremeValues_ReturnsCorrectMinMax(bool fastList)
    {
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(float.MaxValue, float.MinValue, 0.0f, 1.0f),
                new Vector4(float.MinValue, float.MaxValue, -1.0f, -1.0f),
                new Vector4(0.0f, 0.0f, float.MaxValue, float.MinValue),
                new Vector4(1.0f, -1.0f, float.MinValue, float.MaxValue),
            }
            : new List<Vector4>
            {
                new Vector4(float.MaxValue, float.MinValue, 0.0f, 1.0f),
                new Vector4(float.MinValue, float.MaxValue, -1.0f, -1.0f),
                new Vector4(0.0f, 0.0f, float.MaxValue, float.MinValue),
                new Vector4(1.0f, -1.0f, float.MinValue, float.MaxValue),
            };

        Vector4Helper.MinMax(vectors, out var min, out var max);

        AssertVector4Equal(
            new Vector4(float.MinValue, float.MinValue, float.MinValue, float.MinValue),
            min
        );
        AssertVector4Equal(
            new Vector4(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue),
            max
        );
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void MinMax_LargeArray_SIMD_vs_Scalar_SameResult(bool fastList)
    {
        // Test with a large array to ensure SIMD and scalar paths produce identical results
        var random = new Random(42); // Fixed seed for reproducibility
        IList<Vector4> vectors = fastList ? new FastList<Vector4>() : new List<Vector4>();
        for (int i = 0; i < 100; i++)
        {
            vectors.Add(
                new Vector4(
                    (float)random.NextDouble() * 200 - 100,
                    (float)random.NextDouble() * 200 - 100,
                    (float)random.NextDouble() * 200 - 100,
                    (float)random.NextDouble() * 200 - 100
                )
            );
        }

        // Test with SIMD enabled
        bool originalSIMDSetting = MathSettings.EnableSIMD;
        try
        {
            MathSettings.EnableSIMD = true;
            Vector4Helper.MinMax(vectors, out var minSIMD, out var maxSIMD);

            MathSettings.EnableSIMD = false;
            Vector4Helper.MinMax(vectors, out var minScalar, out var maxScalar);

            AssertVector4Equal(minScalar, minSIMD, "SIMD Min differs from Scalar Min");
            AssertVector4Equal(maxScalar, maxSIMD, "SIMD Max differs from Scalar Max");
        }
        finally
        {
            MathSettings.EnableSIMD = originalSIMDSetting;
        }
    }

    #endregion

    #region GetCentroid Tests

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GetCentroid_SingleVector_ReturnsSameVector(bool fastList)
    {
        var vector = new Vector4(1.0f, 2.0f, 3.0f, 4.0f);
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4> { vector }
            : new List<Vector4> { vector };

        var centroid = Vector4Helper.GetCentroid(vectors);

        AssertVector4Equal(vector, centroid);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GetCentroid_TwoVectors_ReturnsMidpoint(bool fastList)
    {
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(0.0f, 0.0f, 0.0f, 0.0f),
                new Vector4(2.0f, 4.0f, 6.0f, 8.0f),
            }
            : new List<Vector4>
            {
                new Vector4(0.0f, 0.0f, 0.0f, 0.0f),
                new Vector4(2.0f, 4.0f, 6.0f, 8.0f),
            };

        var centroid = Vector4Helper.GetCentroid(vectors);

        AssertVector4Equal(new Vector4(1.0f, 2.0f, 3.0f, 4.0f), centroid);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GetCentroid_FourVectors_ReturnsCorrectCentroid(bool fastList)
    {
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(1.0f, 2.0f, 3.0f, 4.0f),
                new Vector4(5.0f, 6.0f, 7.0f, 8.0f),
                new Vector4(9.0f, 10.0f, 11.0f, 12.0f),
                new Vector4(13.0f, 14.0f, 15.0f, 16.0f),
            }
            : new List<Vector4>
            {
                new Vector4(1.0f, 2.0f, 3.0f, 4.0f),
                new Vector4(5.0f, 6.0f, 7.0f, 8.0f),
                new Vector4(9.0f, 10.0f, 11.0f, 12.0f),
                new Vector4(13.0f, 14.0f, 15.0f, 16.0f),
            };

        var centroid = Vector4Helper.GetCentroid(vectors);

        // Expected: (1+5+9+13)/4, (2+6+10+14)/4, (3+7+11+15)/4, (4+8+12+16)/4
        AssertVector4Equal(new Vector4(7.0f, 8.0f, 9.0f, 10.0f), centroid);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GetCentroid_EightVectors_ReturnsCorrectCentroid(bool fastList)
    {
        IList<Vector4> vectors = fastList ? new FastList<Vector4>() : new List<Vector4>();
        for (int i = 0; i < 8; i++)
        {
            vectors.Add(new Vector4(i, i * 2, i * 3, i * 4));
        }

        var centroid = Vector4Helper.GetCentroid(vectors);

        // Expected centroid: ((0+1+2+3+4+5+6+7)/8, ...) = (3.5, 7.0, 10.5, 14.0)
        AssertVector4Equal(new Vector4(3.5f, 7.0f, 10.5f, 14.0f), centroid);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GetCentroid_NonAlignedCount_ReturnsCorrectCentroid(bool fastList)
    {
        IList<Vector4> vectors = fastList ? new FastList<Vector4>() : new List<Vector4>();
        for (int i = 0; i < 13; i++)
        {
            vectors.Add(new Vector4(i, i + 1, i + 2, i + 3));
        }

        var centroid = Vector4Helper.GetCentroid(vectors);

        // Expected: sum(0..12)/13 = 78/13 = 6.0
        AssertVector4Equal(new Vector4(6.0f, 7.0f, 8.0f, 9.0f), centroid);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GetCentroid_NegativeValues_ReturnsCorrectCentroid(bool fastList)
    {
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(-4.0f, -3.0f, -2.0f, -1.0f),
                new Vector4(-2.0f, -1.0f, 0.0f, 1.0f),
                new Vector4(0.0f, 1.0f, 2.0f, 3.0f),
                new Vector4(2.0f, 3.0f, 4.0f, 5.0f),
            }
            : new List<Vector4>
            {
                new Vector4(-4.0f, -3.0f, -2.0f, -1.0f),
                new Vector4(-2.0f, -1.0f, 0.0f, 1.0f),
                new Vector4(0.0f, 1.0f, 2.0f, 3.0f),
                new Vector4(2.0f, 3.0f, 4.0f, 5.0f),
            };

        var centroid = Vector4Helper.GetCentroid(vectors);

        AssertVector4Equal(new Vector4(-1.0f, 0.0f, 1.0f, 2.0f), centroid);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GetCentroid_WithStartAndCount_ReturnsCorrectCentroid(bool fastList)
    {
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(100.0f, 100.0f, 100.0f, 100.0f), // Skip
                new Vector4(1.0f, 2.0f, 3.0f, 4.0f), // Process from here
                new Vector4(3.0f, 4.0f, 5.0f, 6.0f),
                new Vector4(5.0f, 6.0f, 7.0f, 8.0f),
                new Vector4(200.0f, 200.0f, 200.0f, 200.0f), // Skip
            }
            : new List<Vector4>
            {
                new Vector4(100.0f, 100.0f, 100.0f, 100.0f), // Skip
                new Vector4(1.0f, 2.0f, 3.0f, 4.0f), // Process from here
                new Vector4(3.0f, 4.0f, 5.0f, 6.0f),
                new Vector4(5.0f, 6.0f, 7.0f, 8.0f),
                new Vector4(200.0f, 200.0f, 200.0f, 200.0f), // Skip
            };

        var centroid = Vector4Helper.GetCentroid(vectors, 1, 3);

        // Expected: (1+3+5)/3, (2+4+6)/3, (3+5+7)/3, (4+6+8)/3 = (3, 4, 5, 6)
        AssertVector4Equal(new Vector4(3.0f, 4.0f, 5.0f, 6.0f), centroid);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [ExpectedException(typeof(ArgumentOutOfRangeException))]
    public void GetCentroid_StartAndCountExceedLength_ThrowsException(bool fastList)
    {
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(1.0f, 2.0f, 3.0f, 4.0f),
                new Vector4(5.0f, 6.0f, 7.0f, 8.0f),
            }
            : new List<Vector4>
            {
                new Vector4(1.0f, 2.0f, 3.0f, 4.0f),
                new Vector4(5.0f, 6.0f, 7.0f, 8.0f),
            };

        Vector4Helper.GetCentroid(vectors, 1, 5);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GetCentroid_AllSameValues_ReturnsSameValue(bool fastList)
    {
        IList<Vector4> vectors = fastList ? new FastList<Vector4>() : new List<Vector4>();
        for (int i = 0; i < 10; i++)
        {
            vectors.Add(new Vector4(5.0f, 6.0f, 7.0f, 8.0f));
        }

        var centroid = Vector4Helper.GetCentroid(vectors);

        AssertVector4Equal(new Vector4(5.0f, 6.0f, 7.0f, 8.0f), centroid);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GetCentroid_LargeArray_SIMD_vs_Scalar_SameResult(bool fastList)
    {
        // Test with a large array to ensure SIMD and scalar paths produce identical results
        var random = new Random(42); // Fixed seed for reproducibility
        IList<Vector4> vectors = fastList ? new FastList<Vector4>() : new List<Vector4>();
        for (int i = 0; i < 100; i++)
        {
            vectors.Add(
                new Vector4(
                    (float)random.NextDouble() * 200 - 100,
                    (float)random.NextDouble() * 200 - 100,
                    (float)random.NextDouble() * 200 - 100,
                    (float)random.NextDouble() * 200 - 100
                )
            );
        }

        // Test with SIMD enabled
        bool originalSIMDSetting = MathSettings.EnableSIMD;
        try
        {
            MathSettings.EnableSIMD = true;
            var centroidSIMD = Vector4Helper.GetCentroid(vectors);

            MathSettings.EnableSIMD = false;
            var centroidScalar = Vector4Helper.GetCentroid(vectors);

            AssertVector4Equal(
                centroidScalar,
                centroidSIMD,
                "SIMD Centroid differs from Scalar Centroid"
            );
        }
        finally
        {
            MathSettings.EnableSIMD = originalSIMDSetting;
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GetCentroid_SymmetricVectors_ReturnsOrigin(bool fastList)
    {
        IList<Vector4> vectors = fastList
            ? new FastList<Vector4>
            {
                new Vector4(1.0f, 0.0f, 0.0f, 0.0f),
                new Vector4(-1.0f, 0.0f, 0.0f, 0.0f),
                new Vector4(0.0f, 1.0f, 0.0f, 0.0f),
                new Vector4(0.0f, -1.0f, 0.0f, 0.0f),
                new Vector4(0.0f, 0.0f, 1.0f, 0.0f),
                new Vector4(0.0f, 0.0f, -1.0f, 0.0f),
                new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                new Vector4(0.0f, 0.0f, 0.0f, -1.0f),
            }
            : new List<Vector4>
            {
                new Vector4(1.0f, 0.0f, 0.0f, 0.0f),
                new Vector4(-1.0f, 0.0f, 0.0f, 0.0f),
                new Vector4(0.0f, 1.0f, 0.0f, 0.0f),
                new Vector4(0.0f, -1.0f, 0.0f, 0.0f),
                new Vector4(0.0f, 0.0f, 1.0f, 0.0f),
                new Vector4(0.0f, 0.0f, -1.0f, 0.0f),
                new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                new Vector4(0.0f, 0.0f, 0.0f, -1.0f),
            };

        var centroid = Vector4Helper.GetCentroid(vectors);

        AssertVector4Equal(Vector4.Zero, centroid);
    }

    #endregion

    #region Helper Methods

    private static void AssertVector4Equal(Vector4 expected, Vector4 actual, string? message = null)
    {
        bool areEqual =
            Math.Abs(expected.X - actual.X) < Epsilon
            && Math.Abs(expected.Y - actual.Y) < Epsilon
            && Math.Abs(expected.Z - actual.Z) < Epsilon
            && Math.Abs(expected.W - actual.W) < Epsilon;

        if (!areEqual)
        {
            string errorMessage = message ?? "Vectors are not equal";
            errorMessage += $"\nExpected: ({expected.X}, {expected.Y}, {expected.Z}, {expected.W})";
            errorMessage += $"\nActual: ({actual.X}, {actual.Y}, {actual.Z}, {actual.W})";
            Assert.Fail(errorMessage);
        }
    }

    #endregion
}
