namespace HelixToolkit.Nex.Scene;

public struct NodeInfo : ISortable<NodeInfo>
{
    public int Version;
    public readonly int EntityId;
    public int Level { internal set; get; }
    internal bool SelfEnabled;
    internal bool ParentEnabled;
    public bool Enabled => SelfEnabled && ParentEnabled;

    public NodeInfo(int entityId = 0)
    {
        Version = 1;
        EntityId = entityId;
        SelfEnabled = true;
        ParentEnabled = true;
        Level = 0;
    }

    public override string ToString()
    {
        return $"NodeInfo: {EntityId}, Version: {Version}";
    }

    public bool Compare(ref NodeInfo obj)
    {
        return Level < obj.Level;
    }
}

/// <summary>
/// Optional display name for a node. Stored in a separate component so that
/// <see cref="NodeInfo"/> remains a fully blittable value type — sort swaps
/// on the NodeInfo storage array incur no GC write barriers.
/// </summary>
public struct NodeName(string name)
{
    public string Value = name;

    public NodeName()
        : this(string.Empty) { }
}

public struct Transform()
{
    private bool _isLocalDirty = true;
    public readonly bool IsLocalDirty => _isLocalDirty;

    private bool _isWorldDirty = true;
    public readonly bool IsWorldDirty => _isWorldDirty || IsLocalDirty;
    private Vector3 _scale = Vector3.One;
    public Vector3 Scale
    {
        set
        {
            if (value != _scale)
            {
                _scale = value;
                _isLocalDirty = true;
            }
        }
        readonly get => _scale;
    }

    private Vector3 _translation = Vector3.Zero;
    public Vector3 Translation
    {
        set
        {
            if (value != _translation)
            {
                _translation = value;
                _isLocalDirty = true;
            }
        }
        readonly get => _translation;
    }

    private Quaternion _rotation = Quaternion.Identity;

    public Quaternion Rotation
    {
        set
        {
            if (value != _rotation)
            {
                _rotation = value;
                _isLocalDirty = true;
            }
        }
        readonly get => _rotation;
    }

    private Matrix4x4 _value = Matrix4x4.Identity;
    public Matrix4x4 Value
    {
        get
        {
            if (_isLocalDirty)
            {
                _value =
                    Matrix4x4.CreateScale(Scale)
                    * Matrix4x4.CreateFromQuaternion(Rotation)
                    * Matrix4x4.CreateTranslation(Translation);
                _isLocalDirty = false;
                _isWorldDirty = true;
            }
            return _value;
        }
    }

    public bool UpdateWorldTransform(Matrix4x4 parent, out Matrix4x4 worldTransform)
    {
        return UpdateWorldTransform(ref parent, out worldTransform);
    }

    public bool UpdateWorldTransform(ref Matrix4x4 parent, out Matrix4x4 worldTransform)
    {
        if (!IsWorldDirty)
        {
            worldTransform = Matrix4x4.Identity;
            return false; // No change in world transform
        }
        worldTransform = parent * Value;
        _isWorldDirty = false;
        return true;
    }

    public override string ToString()
    {
        return $"Transform: {Value}";
    }

    public void MarkWorldDirty()
    {
        _isWorldDirty = true;
    }
}

public readonly struct WorldTransform(Matrix4x4 value)
{
    public readonly Matrix4x4 Value = value;

    public static implicit operator Matrix4x4(in WorldTransform wt) => wt.Value;

    public static readonly WorldTransform Identity = new(Matrix4x4.Identity);
}

public readonly struct Parent(Entity parent)
{
    public Entity ParentEntity { get; } = parent;
}

public readonly struct Children
{
    public readonly FastList<Node> ChildNodes = [];

    public Children() { }

    public Children(IEnumerable<Node> children)
    {
        foreach (var child in children)
        {
            Add(child);
        }
    }

    public void Add(Node child)
    {
        ChildNodes.Add(child);
    }

    public bool Remove(Node child)
    {
        return ChildNodes.Remove(child);
    }
}
