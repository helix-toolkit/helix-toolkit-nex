//#define DEBUG
using System.Runtime.CompilerServices;

namespace HelixToolkit.Nex.Geometries.Octrees;

/// <summary>
/// Base class for Array based static octree.
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class StaticOctree<T> : IOctreeBasic
    where T : unmanaged
{
    private static readonly ILogger logger = LogManager.Create<StaticOctree<T>>();
    public const int OctantSize = 8;

    /// <summary>
    /// Octant structure, size = 80 bytes
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    protected struct Octant
    {
        public readonly BoundingBox Bound;
        private int _c0,
            _c1,
            _c2,
            _c3,
            _c4,
            _c5,
            _c6,
            _c7;
        public readonly int Parent;
        public readonly int Index;
        public int Start { set; get; }
        public int End { set; get; }
        public bool IsBuilt { set; get; }
        public byte ActiveNode { private set; get; }
        public bool HasChildren
        {
            get { return ActiveNode != 0; }
        }
        public bool IsEmpty
        {
            get { return Count == 0; }
        }
        public int Count
        {
            get { return End - Start; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Octant"/> struct.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="index">The index.</param>
        /// <param name="bound">The bound.</param>
        public Octant(int parent, int index, ref BoundingBox bound)
        {
            Parent = parent;
            Index = index;
            Bound = bound;
            Start = End = 0;
            ActiveNode = 0;
            IsBuilt = false;
            _c0 = _c1 = _c2 = _c3 = _c4 = _c5 = _c6 = _c7 = -1;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Octant"/> struct.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="index">The index.</param>
        public Octant(int parent, int index)
        {
            Parent = parent;
            Index = index;
            Bound = new BoundingBox();
            Start = End = 0;
            ActiveNode = 0;
            IsBuilt = false;
            _c0 = _c1 = _c2 = _c3 = _c4 = _c5 = _c6 = _c7 = -1;
        }

        /// <summary>
        /// Gets the index of the child.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetChildIndex(int index)
        {
            switch (index)
            {
                case 0:
                    return _c0;
                case 1:
                    return _c1;
                case 2:
                    return _c2;
                case 3:
                    return _c3;
                case 4:
                    return _c4;
                case 5:
                    return _c5;
                case 6:
                    return _c6;
                case 7:
                    return _c7;
                default:
                    return -1;
            }
        }

        /// <summary>
        /// Sets the index of the child.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetChildIndex(int index, int value)
        {
            switch (index)
            {
                case 0:
                    _c0 = value;
                    break;
                case 1:
                    _c1 = value;
                    break;
                case 2:
                    _c2 = value;
                    break;
                case 3:
                    _c3 = value;
                    break;
                case 4:
                    _c4 = value;
                    break;
                case 5:
                    _c5 = value;
                    break;
                case 6:
                    _c6 = value;
                    break;
                case 7:
                    _c7 = value;
                    break;
            }
            if (value >= 0)
            {
                ActiveNode |= (byte)(1 << index);
            }
        }

        /// <summary>
        /// Gets or sets the <see cref="int"/> at the specified index.
        /// </summary>
        /// <value>
        /// The <see cref="int"/>.
        /// </value>
        /// <param name="index">The index.</param>
        /// <returns></returns>
        public int this[int index]
        {
            get { return GetChildIndex(index); }
            set { SetChildIndex(index, value); }
        }

        /// <summary>
        /// Determines whether [has child at index] [the specified index].
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns>
        ///   <c>true</c> if [has child at index] [the specified index]; otherwise, <c>false</c>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasChildAtIndex(int index)
        {
            return (ActiveNode & (byte)(1 << index)) != 0;
        }
    }

    /// <summary>
    /// Octant Array, used to manage a internal octant Array, which is the storage for the entire octree
    /// </summary>
    protected sealed class OctantArray
    {
        internal Octant[] Array = new Octant[128];
        public int Count { private set; get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="OctantArray"/> class.
        /// </summary>
        /// <param name="bound">The bound.</param>
        /// <param name="length">The length.</param>
        public OctantArray(BoundingBox bound, int length)
        {
            var octant = new Octant(-1, 0, ref bound) { Start = 0, End = length };
            Array[0] = octant;
            ++Count;
            //var size = System.Runtime.InteropServices.Marshal.SizeOf(octant);
        }

        /// <summary>
        /// Adds the specified parent index.
        /// </summary>
        /// <param name="parentIndex">Index of the parent.</param>
        /// <param name="childIndex">Index of the child.</param>
        /// <param name="bound">The bound.</param>
        /// <param name="newParent">The parent out.</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Add(int parentIndex, int childIndex, BoundingBox bound, ref Octant newParent)
        {
            if (Array.Length < Count + OctantSize)
            {
                var newSize = Array.Length * 2;
                if (newSize > int.MaxValue / 4) //Size is too big
                {
                    return false;
                }
                var newArray = new Octant[Array.Length * 2];
                System.Array.Copy(Array, newArray, Count);
                Array = newArray;
            }
            ref var parent = ref Array[parentIndex];

            Array[Count] = new Octant(parent.Index, Count, ref bound);
            parent[childIndex] = Count;
            ++Count;
            newParent = parent;
            return true;
        }

        /// <summary>
        /// Compacts the octree Array, remove all unused storage space at the end of the Array.
        /// </summary>
        public void Compact()
        {
            if (Array.Length > Count)
            {
                var newArray = new Octant[Count];
                System.Array.Copy(Array, newArray, Count);
                Array = newArray;
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref Octant Get(int i)
        {
            return ref Array[i];
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public Octant this[int index]
        {
            get { return Array[index]; }
            set { Array[index] = value; }
        }
    }

    /// <summary>
    ///
    /// </summary>
    public event EventHandler<EventArgs>? Hit;

    private OctantArray? _octants;

    private readonly List<BoundingBox> _hitPathBoundingBoxes = [];

    /// <summary>
    /// Internal octant Array size.
    /// </summary>
    public int OctantArraySize
    {
        get { return _octants != null ? _octants.Count : 0; }
    }

    /// <summary>
    ///
    /// </summary>
    public IList<BoundingBox> HitPathBoundingBoxes
    {
        get { return _hitPathBoundingBoxes.AsReadOnly(); }
    }

    private static readonly ObjectPool<Stack<KeyValuePair<int, int>>> hitStackPool = new ObjectPool<
        Stack<KeyValuePair<int, int>>
    >(
        () =>
        {
            return new Stack<KeyValuePair<int, int>>();
        },
        10
    );

    /// <summary>
    /// The minumum size for enclosing region is a 1x1x1 cube.
    /// </summary>
    public float MIN_SIZE
    {
        get { return Parameter.MinimumOctantSize; }
    }

    public OctreeBuildParameter Parameter { private set; get; }

    /// <summary>
    ///
    /// </summary>
    protected T[]? Objects { private set; get; }

    /// <summary>
    ///
    /// </summary>
    public bool TreeBuilt { protected set; get; } = false;

    /// <summary>
    ///
    /// </summary>
    public BoundingBox Bound { private set; get; }

    /// <summary>
    ///
    /// </summary>
    /// <param name="parameter"></param>
    public StaticOctree(OctreeBuildParameter parameter)
    {
        Parameter = parameter;
    }

    /// <summary>
    /// Call to build the tree
    /// </summary>
    public void BuildTree()
    {
        if (TreeBuilt)
        {
            return;
        }
#if DEBUG
        var tick = Stopwatch.GetTimestamp();
#endif
        Objects = GetObjects();
        _octants = new OctantArray(GetMaxBound(), Objects.Length);
        TreeTraversal(
            new Stack<KeyValuePair<int, int>>(),
            (index) =>
            {
                BuildSubTree(index);
            },
            null
        );
        _octants.Compact();
        TreeBuilt = true;
        Bound = _octants[0].Bound;
#if DEBUG
        tick = Stopwatch.GetTimestamp() - tick;
        logger.LogDebug(
            "Build static tree time = {0}; Total = {1}",
            (double)tick / Stopwatch.Frequency * 1000,
            _octants.Count
        );
#endif
    }

    protected abstract T[] GetObjects();

    /// <summary>
    /// Get the max bounding box of the octree
    /// </summary>
    /// <returns></returns>
    protected abstract BoundingBox GetMaxBound();

    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CheckDimension(ref BoundingBox bound)
    {
        var dimensions = bound.Maximum - bound.Minimum;

        if (dimensions == Vector3.Zero)
        {
            return false;
        }
        dimensions = bound.Maximum - bound.Minimum;
        //Check to see if the dimensions of the box are greater than the minimum dimensions
        if (dimensions.X < MIN_SIZE && dimensions.Y < MIN_SIZE && dimensions.Z < MIN_SIZE)
        {
            return false;
        }
        else
        {
            return true;
        }
    }

    /// <summary>
    /// Build sub tree nodes
    /// </summary>
    protected void BuildSubTree(int index)
    {
        Debug.Assert(_octants != null);
        Debug.Assert(Objects != null);
        if (_octants == null || Objects == null)
        {
            return;
        }
        var octant = _octants[index];
        if (octant.IsBuilt)
        {
            return;
        }
        var b = octant.Bound;
        if (
            CheckDimension(ref b)
            && !octant.IsEmpty
            && octant.Count > this.Parameter.MinObjectSizeToSplit
        )
        {
            var octantBounds = CreateOctants(ref b, Parameter.MinimumOctantSize);
            if (octantBounds.Length == OctantSize)
            {
                var start = octant.Start;
                var childOctant = new Octant();
                for (var childOctantIdx = 0; childOctantIdx < OctantSize; ++childOctantIdx)
                {
                    var count = 0;
                    var end = octant.End;
                    var hasChildOctant = false;
                    var childIdx = -1;

                    for (var i = end - 1; i >= start; --i)
                    {
                        var obj = Objects[i];
                        if (
                            IsContains(
                                ref octantBounds[childOctantIdx],
                                GetBoundingBoxFromItem(ref obj),
                                ref obj
                            )
                        )
                        {
                            if (!hasChildOctant) //Add New Child Octant if not having one.
                            {
                                if (
                                    !_octants.Add(
                                        index,
                                        childOctantIdx,
                                        octantBounds[childOctantIdx],
                                        ref octant
                                    )
                                )
                                {
                                    logger.LogDebug("Failed to add child");
                                    break;
                                }
                                childIdx = octant[childOctantIdx];
                                childOctant = _octants[childIdx];
                                hasChildOctant = true;
                            }
                            ++count;
                            childOctant.End = end;
                            var s = end - count;
                            childOctant.Start = s;
                            T o = Objects[i];
                            Objects[i] = Objects[s]; //swap objects. Move object into parent octant start/end range
                            Objects[s] = o; //Move object into child octant start/end range
                        }
                    }

                    if (hasChildOctant)
                    {
                        _octants[childIdx] = childOctant;
                    }
                    octant.End = end - count;
                }
            }
        }

        octant.IsBuilt = true;
        _octants[index] = octant;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="item"></param>
    /// <returns></returns>
    protected abstract BoundingBox GetBoundingBoxFromItem(ref T item);

    /// <summary>
    /// This finds the dimensions of the bounding box necessary to tightly enclose all items in the object list.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected BoundingBox FindEnclosingBox(int index)
    {
        if (_octants == null || Objects == null)
        {
            return BoundingBox.Empty;
        }
        ref var octant = ref _octants.Array[index];
        if (octant.Count == 0)
        {
            return BoundingBox.Empty;
        }
        var b = GetBoundingBoxFromItem(ref Objects[octant.Start]);
        for (var i = octant.Start + 1; i < octant.End; ++i)
        {
            var bound = GetBoundingBoxFromItem(ref Objects[i]);
            BoundingBox.Merge(ref b, ref bound, out b);
        }
        return b;
    }

    /// <summary>
    /// Create child octant bounding boxes for current parent bounding box
    /// </summary>
    /// <param name="box"></param>
    /// <param name="minSize"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BoundingBox[] CreateOctants(ref BoundingBox box, float minSize)
    {
        var dimensions = box.Maximum - box.Minimum;
        if (
            dimensions == Vector3.Zero
            || (dimensions.X < minSize || dimensions.Y < minSize || dimensions.Z < minSize)
        )
        {
            return new BoundingBox[0];
        }
        var half = dimensions / 2.0f;
        var center = box.Minimum + half;
        var minimum = box.Minimum;
        var maximum = box.Maximum;
        //Create subdivided regions for each octant
        return new BoundingBox[]
        {
            new BoundingBox(minimum, center),
            new BoundingBox(
                new Vector3(center.X, minimum.Y, minimum.Z),
                new Vector3(maximum.X, center.Y, center.Z)
            ),
            new BoundingBox(
                new Vector3(center.X, minimum.Y, center.Z),
                new Vector3(maximum.X, center.Y, maximum.Z)
            ),
            new BoundingBox(
                new Vector3(minimum.X, minimum.Y, center.Z),
                new Vector3(center.X, center.Y, maximum.Z)
            ),
            new BoundingBox(
                new Vector3(minimum.X, center.Y, minimum.Z),
                new Vector3(center.X, maximum.Y, center.Z)
            ),
            new BoundingBox(
                new Vector3(center.X, center.Y, minimum.Z),
                new Vector3(maximum.X, maximum.Y, center.Z)
            ),
            new BoundingBox(center, maximum),
            new BoundingBox(
                new Vector3(minimum.X, center.Y, center.Z),
                new Vector3(center.X, maximum.Y, maximum.Z)
            ),
        };
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="source"></param>
    /// <param name="target"></param>
    /// <param name="targetObj"></param>
    /// <returns></returns>
    protected virtual bool IsContains(ref BoundingBox source, BoundingBox target, ref T targetObj)
    {
        return BoxContainsBox(ref source, ref target);
    }

    /// <summary>
    /// Common function to traverse the tree
    /// </summary>
    /// <param name="stack"></param>
    /// <param name="process"></param>
    /// <param name="canVisitChildren"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TreeTraversal(
        Stack<KeyValuePair<int, int>> stack,
        Action<int> process,
        Func<int, bool>? canVisitChildren = null
    )
    {
        if (_octants == null || Objects == null)
        {
            return;
        }
        var parent = -1;
        var curr = -1;
        var dummy = new Octant(-1, -1);
        dummy[0] = 0;
        var parentOctant = dummy;
        while (true)
        {
            while (++curr < OctantSize)
            {
                if (parentOctant.HasChildAtIndex(curr))
                {
                    var childIdx = parentOctant[curr];
                    process(childIdx);
                    ref var octant = ref _octants.Array[childIdx];
                    if (
                        octant.HasChildren
                        && (canVisitChildren == null || canVisitChildren(octant.Index))
                    )
                    {
                        stack.Push(new KeyValuePair<int, int>(parent, curr));
                        parent = octant.Index;
                        curr = -1;
                        parentOctant = _octants[parent];
                    }
                }
            }
            if (stack.Count == 0)
            {
                break;
            }
            var prev = stack.Pop();
            parent = prev.Key;
            curr = prev.Value;
            if (parent == -1)
            {
                break;
            }
            parentOctant = _octants[parent];
        }
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="context"></param>
    /// <param name="model"></param>
    /// <param name="geometry"></param>
    /// <param name="modelMatrix"></param>
    /// <param name="hits"></param>
    /// <returns></returns>
    public bool HitTest(
        HitTestContext context,
        Entity model,
        Geometry geometry,
        Matrix modelMatrix,
        ref List<HitTestResult> hits
    )
    {
        return HitTest(context, model, geometry, modelMatrix, false, ref hits);
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="context"></param>
    /// <param name="model"></param>
    /// <param name="geometry"></param>
    /// <param name="modelMatrix"></param>
    /// <param name="returnMultiple"></param>
    /// <param name="hits"></param>
    /// <returns></returns>
    public bool HitTest(
        HitTestContext context,
        Entity model,
        Geometry geometry,
        Matrix modelMatrix,
        bool returnMultiple,
        ref List<HitTestResult> hits
    )
    {
        return HitTest(context, model, geometry, modelMatrix, returnMultiple, ref hits, 0);
    }

    /// <summary>
    /// Hits the test.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="model">The model.</param>
    /// <param name="geometry">The geometry.</param>
    /// <param name="modelMatrix">The model matrix.</param>
    /// <param name="hits">The hits.</param>
    /// <param name="hitThickness">The hit thickness.</param>
    /// <returns></returns>
    public virtual bool HitTest(
        HitTestContext context,
        Entity model,
        Geometry geometry,
        Matrix modelMatrix,
        ref List<HitTestResult> hits,
        float hitThickness
    )
    {
        return HitTest(context, model, geometry, modelMatrix, false, ref hits, hitThickness);
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="context"></param>
    /// <param name="model"></param>
    /// <param name="geometry"></param>
    /// <param name="modelMatrix"></param>
    /// <param name="returnMultiple"></param>
    /// <param name="hits"></param>
    /// <param name="hitThickness"></param>
    /// <returns></returns>
    public virtual bool HitTest(
        HitTestContext context,
        Entity model,
        Geometry geometry,
        Matrix modelMatrix,
        bool returnMultiple,
        ref List<HitTestResult> hits,
        float hitThickness
    )
    {
        if (_octants == null || Objects == null)
        {
            return false;
        }
        if (hits == null)
        {
            hits = new List<HitTestResult>();
        }
        _hitPathBoundingBoxes.Clear();
        var hitStack = hitStackPool.GetObject();
        hitStack.Clear();
        var isHit = false;
        var modelHits = new List<HitTestResult>();
        if (!Matrix.Invert(modelMatrix, out var modelInv))
        {
            return false;
        } //Cannot be inverted
        var rayWS = context.RayWS;
        var rayModel = new Ray(
            rayWS.Position.TransformCoordinate(ref modelInv),
            Vector3.Normalize(rayWS.Direction.TransformNormal(ref modelInv))
        );

        var parent = -1;
        var curr = -1;
        var dummy = new Octant(-1, -1);
        dummy[0] = 0;
        var parentOctant = dummy;
        while (true)
        {
            while (++curr < OctantSize)
            {
                if (parentOctant.HasChildAtIndex(curr))
                {
                    ref var octant = ref _octants.Array[parentOctant[curr]];
                    var isIntersect = false;
                    var nodeHit = HitTestCurrentNodeExcludeChild(
                        ref octant,
                        context,
                        model,
                        geometry,
                        modelMatrix,
                        ref rayModel,
                        returnMultiple,
                        ref modelHits,
                        ref isIntersect,
                        hitThickness
                    );
                    isHit |= nodeHit;
                    if (isIntersect && octant.HasChildren)
                    {
                        hitStack.Push(new KeyValuePair<int, int>(parent, curr));
                        parent = octant.Index;
                        curr = -1;
                        parentOctant = _octants[parent];
                    }
                    if (Parameter.RecordHitPathBoundingBoxes && nodeHit)
                    {
                        var n = octant;
                        while (true)
                        {
                            _hitPathBoundingBoxes.Add(n.Bound);
                            if (n.Parent >= 0)
                            {
                                n = _octants[n.Parent];
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }
            }
            if (hitStack.Count == 0)
            {
                break;
            }
            var prev = hitStack.Pop();
            parent = prev.Key;
            curr = prev.Value;
            if (parent == -1)
            {
                break;
            }
            parentOctant = _octants[parent];
        }
        hitStackPool.PutObject(hitStack);
        if (!isHit)
        {
            _hitPathBoundingBoxes.Clear();
        }
        else
        {
            hits.AddRange(modelHits);
            Hit?.Invoke(this, EventArgs.Empty);
        }
        return isHit;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="context"></param>
    /// <param name="sphere"></param>
    /// <param name="points"></param>
    /// <returns></returns>
    public virtual bool FindNearestPointBySphere(
        HitTestContext context,
        ref BoundingSphere sphere,
        ref List<HitTestResult> points
    )
    {
        if (_octants == null || Objects == null)
        {
            return false;
        }
        points ??= [];
        var hitStack = hitStackPool.GetObject();
        hitStack.Clear();
        var isHit = false;

        var parent = -1;
        var curr = -1;
        var dummy = new Octant(-1, -1);
        dummy[0] = 0;
        var parentOctant = dummy;
        while (true)
        {
            while (++curr < OctantSize)
            {
                if (parentOctant.HasChildAtIndex(curr))
                {
                    ref var octant = ref _octants.Array[parentOctant[curr]];
                    var isIntersect = false;
                    var nodeHit = FindNearestPointBySphereExcludeChild(
                        ref octant,
                        context,
                        ref sphere,
                        ref points,
                        ref isIntersect
                    );
                    isHit |= nodeHit;
                    if (octant.HasChildren && isIntersect)
                    {
                        hitStack.Push(new KeyValuePair<int, int>(parent, curr));
                        parent = octant.Index;
                        curr = -1;
                        parentOctant = _octants[parent];
                    }
                }
            }
            if (hitStack.Count == 0)
            {
                break;
            }
            var prev = hitStack.Pop();
            parent = prev.Key;
            curr = prev.Value;
            if (parent == -1)
            {
                break;
            }
            parentOctant = _octants[parent];
        }
        hitStackPool.PutObject(hitStack);
        return isHit;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="context"></param>
    /// <param name="point"></param>
    /// <param name="results"></param>
    /// <param name="heuristicSearchFactor"></param>
    /// <returns></returns>
    public virtual bool FindNearestPointFromPoint(
        HitTestContext context,
        ref Vector3 point,
        ref List<HitTestResult> results,
        float heuristicSearchFactor = 1f
    )
    {
        if (_octants == null || Objects == null)
        {
            return false;
        }
        results ??= [];
        var hitStack = hitStackPool.GetObject();
        hitStack.Clear();
        var sphere = new BoundingSphere(point, float.MaxValue);
        var isHit = false;
        heuristicSearchFactor = Math.Min(1.0f, Math.Max(0.1f, heuristicSearchFactor));

        var parent = -1;
        var curr = -1;
        var dummy = new Octant(-1, -1);
        dummy[0] = 0;
        var parentOctant = dummy;
        while (true)
        {
            while (++curr < OctantSize)
            {
                if (parentOctant.HasChildAtIndex(curr))
                {
                    ref var octant = ref _octants.Array[parentOctant[curr]];
                    var isIntersect = false;
                    var nodeHit = FindNearestPointBySphereExcludeChild(
                        ref octant,
                        context,
                        ref sphere,
                        ref results,
                        ref isIntersect
                    );
                    isHit |= nodeHit;
                    if (isIntersect)
                    {
                        if (results.Count > 0)
                        {
                            sphere.Radius = (float)results[0].Distance * heuristicSearchFactor;
                        }
                        if (octant.HasChildren)
                        {
                            hitStack.Push(new KeyValuePair<int, int>(parent, curr));
                            parent = octant.Index;
                            curr = -1;
                            parentOctant = _octants[parent];
                        }
                    }
                }
            }
            if (hitStack.Count == 0)
            {
                break;
            }
            var prev = hitStack.Pop();
            parent = prev.Key;
            curr = prev.Value;
            if (parent == -1)
            {
                break;
            }
            parentOctant = _octants[parent];
        }
        hitStackPool.PutObject(hitStack);
        return isHit;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="context"></param>
    /// <param name="point"></param>
    /// <param name="radius"></param>
    /// <param name="result"></param>
    /// <returns></returns>
    public bool FindNearestPointByPointAndSearchRadius(
        HitTestContext context,
        ref Vector3 point,
        float radius,
        ref List<HitTestResult> result
    )
    {
        var sphere = new BoundingSphere(point, radius);
        return FindNearestPointBySphere(context, ref sphere, ref result);
    }

    /// <summary>
    /// Find nearest point by sphere on current node only.
    /// </summary>
    /// <param name="octant"></param>
    /// <param name="context"></param>
    /// <param name="sphere"></param>
    /// <param name="points"></param>
    /// <param name="isIntersect"></param>
    /// <returns></returns>
    protected abstract bool FindNearestPointBySphereExcludeChild(
        ref Octant octant,
        HitTestContext context,
        ref BoundingSphere sphere,
        ref List<HitTestResult> points,
        ref bool isIntersect
    );

    /// <summary>
    /// Hit test for current node.
    /// </summary>
    /// <param name="octant"></param>
    /// <param name="context"></param>
    /// <param name="model"></param>
    /// <param name="geometry"></param>
    /// <param name="modelMatrix"></param>
    /// <param name="rayModel"></param>
    /// <param name="returnMultiple">Return multiple hit results or only the closest one</param>
    /// <param name="hits"></param>
    /// <param name="isIntersect"></param>
    /// <param name="hitThickness"></param>
    /// <returns></returns>
    protected abstract bool HitTestCurrentNodeExcludeChild(
        ref Octant octant,
        HitTestContext context,
        Entity model,
        Geometry geometry,
        Matrix modelMatrix,
        ref Ray rayModel,
        bool returnMultiple,
        ref List<HitTestResult> hits,
        ref bool isIntersect,
        float hitThickness
    );

    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    public Geometry CreateOctreeLineModel()
    {
        var geo = new Geometry(Topology.Line);
        if (_octants == null || Objects == null)
        {
            return geo;
        }
        geo.Vertices.Capacity = 8 * _octants.Count;
        geo.Indices.Capacity = 12 * 2 * _octants.Count;
        for (var i = 0; i < _octants.Count; ++i)
        {
            var box = _octants.Array[i].Bound;
            var verts = new Vector4[8];
            verts[0] = box.Minimum.ToVector4(1);
            verts[1] = new Vector4(box.Minimum.X, box.Minimum.Y, box.Maximum.Z, 1); //Z
            verts[2] = new Vector4(box.Minimum.X, box.Maximum.Y, box.Minimum.Z, 1); //Y
            verts[3] = new Vector4(box.Maximum.X, box.Minimum.Y, box.Minimum.Z, 1); //X

            verts[7] = box.Maximum.ToVector4(1);
            verts[4] = new Vector4(box.Maximum.X, box.Maximum.Y, box.Minimum.Z, 1); //Z
            verts[5] = new Vector4(box.Maximum.X, box.Minimum.Y, box.Maximum.Z, 1); //Y
            verts[6] = new Vector4(box.Minimum.X, box.Maximum.Y, box.Maximum.Z, 1); //X
            geo.Vertices.AddRange(verts);
            geo.AddLineIndices(0, 1);
            geo.AddLineIndices(0, 2);
            geo.AddLineIndices(0, 3);
            geo.AddLineIndices(7, 4);
            geo.AddLineIndices(7, 5);
            geo.AddLineIndices(7, 6);
            geo.AddLineIndices(1, 6);
            geo.AddLineIndices(1, 5);
            geo.AddLineIndices(4, 2);
            geo.AddLineIndices(4, 3);
            geo.AddLineIndices(2, 6);
            geo.AddLineIndices(3, 5);
        }
        return geo;
    }

    #region Special Tests
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool BoxContainsBox(ref BoundingBox source, ref BoundingBox target)
    {
        //Source contains target
        return source.Minimum.X <= target.Minimum.X
            && (
                target.Maximum.X <= source.Maximum.X
                && source.Minimum.Y <= target.Minimum.Y
                && target.Maximum.Y <= source.Maximum.Y
            )
            && source.Minimum.Z <= target.Minimum.Z
            && target.Maximum.Z <= source.Maximum.Z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool BoxDisjointSphere(BoundingBox box, ref BoundingSphere sphere)
    {
        var vector = sphere.Center.Clamp(box.Minimum, box.Maximum);
        var distance = Vector3.DistanceSquared(sphere.Center, vector);

        return distance > sphere.Radius * sphere.Radius;
    }
    #endregion
}
