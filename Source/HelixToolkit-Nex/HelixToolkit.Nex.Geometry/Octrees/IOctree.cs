namespace HelixToolkit.Nex.Geometries.Octrees;

/// <summary>
///
/// </summary>
public sealed class OctreeBuildParameter : ObservableObject
{
    private float _minimumOctantSize = 1f;

    /// <summary>
    /// Minimum Octant size.
    /// </summary>
    public float MinimumOctantSize
    {
        set { Set(ref _minimumOctantSize, value); }
        get { return _minimumOctantSize; }
    }

    private int _minObjectSizeToSplit = 2;

    /// <summary>
    /// Minimum object in each octant to start splitting into smaller octant during build
    /// </summary>
    public int MinObjectSizeToSplit
    {
        set { Set(ref _minObjectSizeToSplit, value); }
        get { return _minObjectSizeToSplit; }
    }

    private bool _autoDeleteIfEmpty = true;

    /// <summary>
    /// Delete empty octant automatically
    /// </summary>
    public bool AutoDeleteIfEmpty
    {
        set { Set(ref _autoDeleteIfEmpty, value); }
        get { return _autoDeleteIfEmpty; }
    }

    private bool _cubify = false;

    /// <summary>
    /// Generate cube _octants instead of rectangle _octants
    /// </summary>
    public bool Cubify
    {
        set { Set(ref _cubify, value); }
        get { return _cubify; }
    }

    /// <summary>
    /// Record hit path bounding boxes for debugging or display purpose only
    /// </summary>
    public bool RecordHitPathBoundingBoxes { set; get; } = false;

    /// <summary>
    /// Use parallel tree traversal to build the octree
    /// </summary>
    public bool EnableParallelBuild { set; get; } = false;

    /// <summary>
    ///
    /// </summary>
    public OctreeBuildParameter() { }

    /// <summary>
    ///
    /// </summary>
    /// <param name="minSize"></param>
    public OctreeBuildParameter(float minSize)
    {
        MinimumOctantSize = Math.Max(0, minSize);
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="autoDeleteIfEmpty"></param>
    public OctreeBuildParameter(bool autoDeleteIfEmpty)
    {
        AutoDeleteIfEmpty = autoDeleteIfEmpty;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="minSize"></param>
    /// <param name="autoDeleteIfEmpty"></param>
    public OctreeBuildParameter(int minSize, bool autoDeleteIfEmpty)
        : this(minSize)
    {
        AutoDeleteIfEmpty = autoDeleteIfEmpty;
    }
}

/// <summary>
/// Interface for basic octree. Used to implement static octree and dynamic octree
/// </summary>
public interface IOctreeBasic
{
    /// <summary>
    /// Whether the tree has been built.
    /// </summary>
    bool TreeBuilt { get; }

    /// <summary>
    ///
    /// </summary>
    event EventHandler<EventArgs> Hit;

    /// <summary>
    /// Output the hit path of the tree traverse. Only for debugging
    /// </summary>
    IList<BoundingBox> HitPathBoundingBoxes { get; }

    /// <summary>
    /// Octree parameter
    /// </summary>
    OctreeBuildParameter Parameter { get; }

    /// <summary>
    /// Gets the bound.
    /// </summary>
    /// <value>
    /// The bound.
    /// </value>
    BoundingBox Bound { get; }

    /// <summary>
    /// Build the static octree
    /// </summary>
    void BuildTree();

    /// <summary>
    /// Hit test. Only returns closest hit test result
    /// </summary>
    /// <param name="context"></param>
    /// <param name="model"></param>
    /// <param name="geometry"></param>
    /// <param name="modelMatrix"></param>
    /// <param name="hits"></param>
    /// <returns></returns>
    bool HitTest(
        HitTestContext context,
        Entity model,
        Geometry geometry,
        Matrix modelMatrix,
        ref List<HitTestResult> hits
    );

    /// <summary>
    /// Hits the test. Returns multiple hits if returnsMultiple = true/>
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="model">The model entity.</param>
    /// <param name="geometry">The geometry.</param>
    /// <param name="modelMatrix">The model matrix.</param>
    /// <param name="returnsMultiple">if set to <c>true</c> [returns multiple].</param>
    /// <param name="hits">The hits.</param>
    /// <returns></returns>
    bool HitTest(
        HitTestContext context,
        Entity model,
        Geometry geometry,
        Matrix modelMatrix,
        bool returnsMultiple,
        ref List<HitTestResult> hits
    );

    /// <summary>
    ///
    /// </summary>
    /// <param name="context"></param>
    /// <param name="model"></param>
    /// <param name="geometry"></param>
    /// <param name="modelMatrix"></param>
    /// <param name="hits"></param>
    /// <param name="hitThickness"></param>
    /// <returns></returns>
    bool HitTest(
        HitTestContext context,
        Entity model,
        Geometry geometry,
        Matrix modelMatrix,
        ref List<HitTestResult> hits,
        float hitThickness
    );

    /// <summary>
    /// Hits the test.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="model">The model entity.</param>
    /// <param name="geometry">The geometry.</param>
    /// <param name="modelMatrix">The model matrix.</param>
    /// <param name="returnsMultiple">if set to <c>true</c> [returns multiple].</param>
    /// <param name="hits">The hits.</param>
    /// <param name="hitThickness">The hit thickness.</param>
    /// <returns></returns>
    bool HitTest(
        HitTestContext context,
        Entity model,
        Geometry geometry,
        Matrix modelMatrix,
        bool returnsMultiple,
        ref List<HitTestResult> hits,
        float hitThickness
    );

    /// <summary>
    ///
    /// </summary>
    /// <param name="context"></param>
    /// <param name="point"></param>
    /// <param name="results"></param>
    /// <param name="heuristicSearchFactor"></param>
    /// <returns></returns>
    bool FindNearestPointFromPoint(
        HitTestContext context,
        ref Vector3 point,
        ref List<HitTestResult> results,
        float heuristicSearchFactor = 1f
    );

    /// <summary>
    ///
    /// </summary>
    /// <param name="context"></param>
    /// <param name="sphere"></param>
    /// <param name="points"></param>
    /// <returns></returns>
    bool FindNearestPointBySphere(
        HitTestContext context,
        ref BoundingSphere sphere,
        ref List<HitTestResult> points
    );

    /// <summary>
    /// Finds the nearest point by point and search radius.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="point">The point.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="result">The result.</param>
    /// <returns></returns>
    bool FindNearestPointByPointAndSearchRadius(
        HitTestContext context,
        ref Vector3 point,
        float radius,
        ref List<HitTestResult> result
    );

    /// <summary>
    /// Creates the octree line model for debugging or visualize the octree
    /// </summary>
    /// <returns></returns>
    Geometry CreateOctreeLineModel();
}
