namespace HelixToolkit.Nex.Geometries;

public static class HitTestSettings
{
    /// <summary>
    /// Used to scale up small triangle during hit test.
    /// </summary>
    public static float SmallTriangleHitTestScaling = 1e3f;

    /// <summary>
    /// Used to determine if the triangle is small.
    /// Small triangle is defined as any edge length square is smaller than
    /// <see cref="SmallTriangleEdgeLengthSquare"/>.
    /// </summary>
    public static float SmallTriangleEdgeLengthSquare = 1e-3f;

    /// <summary>
    /// Used to enable small triangle hit test. It uses <see cref="SmallTriangleEdgeLengthSquare"/>
    /// to determine if triangle is too small. If it is too small, scale up the triangle before
    /// hit test.
    /// </summary>
    public static bool EnableSmallTriangleHitTestScaling = true;
}

public interface IRenderMatrices
{
    /// <summary>
    /// Gets the view matrix.
    /// </summary>
    /// <value>
    /// The view matrix.
    /// </value>
    Matrix ViewMatrix { get; }

    /// <summary>
    /// Gets the inversed view matrix.
    /// </summary>
    /// <value>
    /// The inversed view matrix.
    /// </value>
    Matrix ViewMatrixInv { get; }

    /// <summary>
    /// Gets the projection matrix.
    /// </summary>
    /// <value>
    /// The projection matrix.
    /// </value>
    Matrix ProjectionMatrix { get; }

    /// <summary>
    /// Gets the inversed projection matrix.
    /// </summary>
    Matrix ProjectionMatrixInv { get; }

    /// <summary>
    /// Gets the viewport matrix.
    /// </summary>
    /// <value>
    /// The viewport matrix.
    /// </value>
    Matrix ViewportMatrix { get; }

    /// <summary>
    /// Gets the screen view projection matrix.
    /// </summary>
    /// <value>
    /// The screen view projection matrix.
    /// </value>
    Matrix ScreenViewProjectionMatrix { get; }

    /// <summary>
    /// Gets a value indicating whether this instance is perspective.
    /// </summary>
    /// <value>
    ///   <c>true</c> if this instance is perspective; otherwise, <c>false</c>.
    /// </value>
    bool IsPerspective { get; }

    /// <summary>
    /// Gets the actual width.
    /// </summary>
    /// <value>
    /// The actual width.
    /// </value>
    float ActualWidth { get; }

    /// <summary>
    /// Gets the actual height.
    /// </summary>
    /// <value>
    /// The actual height.
    /// </value>
    float ActualHeight { get; }

    /// <summary>
    /// Gets the dpi scale.
    /// </summary>
    /// <value>
    /// The dpi scale.
    /// </value>
    float DpiScale { get; }

    /// <summary>
    /// Gets the bounding frustum.
    /// </summary>
    /// <value>
    /// The bounding frustum.
    /// </value>
    BoundingFrustum BoundingFrustum { get; }
}

public static class IRenderMatricesExtensions
{
    public static Vector2 ProjectToScreenSpace(this IRenderMatrices matrices, in Vector3 point3D)
    {
        return point3D.TransformCoordinate(matrices.ScreenViewProjectionMatrix).ToVector2();
    }

    public static Ray Unproject(this IRenderMatrices matrices, in Vector2 screenPoint)
    {
        var px = screenPoint.X;
        var py = screenPoint.Y;
        var w = matrices.ActualWidth / matrices.DpiScale;
        var h = matrices.ActualHeight / matrices.DpiScale;
        var pNear = new Vector3((2 * px / w) - 1, -((2 * py / h) - 1), 1);
        var pFar = pNear - new Vector3(0, 0, 0.05f);
        pNear = pNear
            .TransformCoordinate(matrices.ProjectionMatrixInv)
            .TransformCoordinate(matrices.ViewMatrixInv);
        pFar = pFar.TransformCoordinate(matrices.ProjectionMatrixInv)
            .TransformCoordinate(matrices.ViewMatrixInv);
        return new Ray(pNear, Vector3.Normalize(pFar - pNear));
    }
}

public sealed class HitTestContext
{
    /// <summary>
    /// Gets or sets the render matrices. This is only needed for line/point hit test.
    /// </summary>
    /// <value>
    /// The render matrices.
    /// </value>
    public IRenderMatrices RenderMatrices { get; }

    /// <summary>
    /// Gets or sets the ray in world space.
    /// </summary>
    /// <value>
    /// The ray.
    /// </value>
    public Ray RayWS { get; }

    /// <summary>
    /// Gets or sets the hit point on screen space. This is the hit point on viewport region without DpiScaled coordinate.
    /// </summary>
    /// <value>
    /// The screen hit point.
    /// </value>
    public Vector2 HitPointSP { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HitTestContext"/> class.
    /// </summary>
    /// <param name="metrices">The render metrices.</param>
    /// <param name="rayWS">The ray in world space.</param>
    /// <param name="hitSP">The hit point on screen space. Pass in the hit point on viewport region directly.
    /// <para>Do not scale with DpiScale factor.</para></param>
    public HitTestContext(IRenderMatrices metrices, ref Ray rayWS, ref Vector2 hitSP)
    {
        RenderMatrices = metrices;
        RayWS = rayWS;
        HitPointSP = hitSP;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HitTestContext"/> class.
    /// </summary>
    /// <param name="metrices">The render metrices.</param>
    /// <param name="rayWS">The ray in world space.</param>
    /// <param name="hitSP">The hit point on screen space. Pass in the hit point on viewport region directly.
    /// <para>Do not scale with DpiScale factor.</para></param>
    public HitTestContext(IRenderMatrices metrices, Ray rayWS, Vector2 hitSP)
        : this(metrices, ref rayWS, ref hitSP) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="HitTestContext"/> class.
    /// This calculates screen hit point automatically from metrices and world space ray.
    /// </summary>
    /// <param name="metrices">The render metrices.</param>
    /// <param name="rayWS">The ray in world space.</param>
    public HitTestContext(IRenderMatrices metrices, ref Ray rayWS)
    {
        RenderMatrices = metrices;
        RayWS = rayWS;
        if (metrices != null)
        {
            HitPointSP = metrices.ProjectToScreenSpace(rayWS.Position);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HitTestContext"/> class.
    /// This calculates ray in world space automatically from metrices and hit point.
    /// </summary>
    /// <param name="metrices">The render metrices.</param>
    /// <param name="hitSP">Screen hit point. Pass in the hit point on viewport region directly.
    /// <para>Do not scale with DpiScale factor.</para></param>
    public HitTestContext(IRenderMatrices metrices, ref Vector2 hitSP)
    {
        RenderMatrices = metrices;
        HitPointSP = hitSP;
        if (metrices != null)
        {
            RayWS = metrices.Unproject(hitSP);
        }
    }
}

public class HitTestResult : IComparable<HitTestResult>
{
    /// <summary>
    /// Gets or sets the distance from the hit ray origin to the <see cref="PointHit"/>
    /// </summary>
    /// <value>
    /// The distance.
    /// </value>
    public double Distance { get; set; }

    /// <summary>
    /// Gets the Model3D intersected by the ray along which the hit test was performed.
    /// Model3D intersected by the ray.
    /// </summary>
    public Entity ModelHit { get; set; }

    /// <summary>
    /// Gets the Point at the intersection between the ray along which the hit
    /// test was performed and the hit object.
    /// Point at which the hit object was intersected by the ray.
    /// </summary>
    public Vector3 PointHit { get; set; }

    /// <summary>
    /// The normal vector of the triangle hit.
    /// </summary>
    public Vector3 NormalAtHit { get; set; }

    /// <summary>
    /// Indicates if this Result has data from a valid hit.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// This is a tag to add additional data.
    /// </summary>
    public object? Tag { get; set; }

    /// <summary>
    /// Gets or sets the geometry.
    /// </summary>
    /// <value>
    /// The geometry.
    /// </value>
    public Geometry? Geometry { set; get; }

    /// <summary>
    /// The hitted triangle vertex indices.
    /// </summary>
    public Tuple<int, int, int>? TriangleIndices { set; get; }

    public int CompareTo(HitTestResult? other)
    {
        if (other == null)
        {
            return 1;
        }
        else
        {
            return Distance.CompareTo(other.Distance);
        }
    }

    /// <summary>
    /// Shallow copy all the properties from another result.
    /// </summary>
    /// <param name="result">The result.</param>
    public void ShallowCopy(HitTestResult result)
    {
        Distance = result.Distance;
        ModelHit = result.ModelHit;
        PointHit = result.PointHit;
        NormalAtHit = result.NormalAtHit;
        IsValid = result.IsValid;
        Tag = result.Tag;
        Geometry = result.Geometry;
        TriangleIndices = result.TriangleIndices;
    }

    /// <summary>
    /// Get a descirption of the HitTestResult
    /// </summary>
    public override string ToString()
    {
        return $"{nameof(HitTestResult)} {nameof(ModelHit)}: {ModelHit}, {nameof(Distance)}: {Distance}, {nameof(IsValid)}: {IsValid}, {nameof(PointHit)}: {PointHit}, {nameof(NormalAtHit)}: {NormalAtHit}";
    }
}

/// <summary>
/// A specialized line hit test result.
/// </summary>
public class LineHitTestResult : HitTestResult
{
    /// <summary>
    /// Gets or sets the index of the line segment that was hit.
    /// </summary>
    public int LineIndex { get; set; } = -1;

    /// <summary>
    /// Gets or sets the shortest distance between the hit test ray and the line that was hit.
    /// </summary>
    public double RayToLineDistance { get; set; }

    /// <summary>
    /// Gets or sets the scalar of the closest point on the hit test ray.
    /// </summary>
    public double RayHitPointScalar { get; set; }

    /// <summary>
    /// Gets or sets the scalar of the closest point on the line that was hit.
    /// </summary>
    public double LineHitPointScalar { get; set; }
}
