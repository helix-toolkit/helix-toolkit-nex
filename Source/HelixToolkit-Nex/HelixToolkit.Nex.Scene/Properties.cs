namespace HelixToolkit.Nex.Scene;

public sealed class NodeInfo : ISortable<NodeInfo>
{
    public int Version = 1;
    public Guid Id = Guid.NewGuid();
    public string Name = string.Empty;
    public Node? Node = null;
    internal bool SelfEnabled = true;
    internal bool ParentEnabled = true;
    public bool Enabled => SelfEnabled && ParentEnabled;

    public int Level { internal set; get; } = 0;

    public NodeInfo() { }

    public NodeInfo(Node node)
    {
        Node = node;
    }

    public override string ToString()
    {
        return $"NodeInfo: {Id}, Name: {Name}, Version: {Version}";
    }

    public bool Compare(ref NodeInfo obj)
    {
        return Level < obj.Level;
    }
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
