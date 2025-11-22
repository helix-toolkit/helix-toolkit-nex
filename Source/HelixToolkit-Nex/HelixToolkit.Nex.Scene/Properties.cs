namespace HelixToolkit.Nex.Scene;

public struct NodeInfo
{
    public int Level = 0;
    public int Version = 1;
    public Guid Id = Guid.NewGuid();
    public string Name = string.Empty;
    public Node? Node = null;

    public NodeInfo() { }

    public NodeInfo(Node node)
    {
        Node = node;
    }

    public override readonly string ToString()
    {
        return $"NodeInfo: {Id}, Name: {Name}, Level: {Level}, Version: {Version}";
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

    public Matrix4x4 WorldTransform { private set; get; } = Matrix4x4.Identity;

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

    public void UpdateWorldTransform(in Matrix4x4 parent)
    {
        if (!IsWorldDirty)
        {
            return; // No change in world transform
        }
        WorldTransform = parent * Value;
        _isWorldDirty = false;
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

public struct Parent()
{
    public Entity ParentEntity = Entity.Null;
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
