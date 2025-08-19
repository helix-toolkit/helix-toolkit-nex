namespace HelixToolkit.Nex.Scene;

public struct NodeInfo
{
    public int Level = 0;
    public int Version = 1;
    public Guid Id = Guid.NewGuid();
    public string Name = string.Empty;
    public Node? Node = null;

    public NodeInfo()
    {

    }

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
    private bool isLocalDirty = true;
    public readonly bool IsLocalDirty => isLocalDirty;

    private bool isWorldDirty = true;
    public readonly bool IsWorldDirty => isWorldDirty || IsLocalDirty;
    private Vector3 scale = Vector3.One;
    public Vector3 Scale
    {
        set
        {
            if (value != scale)
            {
                scale = value;
                isLocalDirty = true;
            }
        }
        readonly get => scale;
    }

    private Vector3 translation = Vector3.Zero;
    public Vector3 Translation
    {
        set
        {
            if (value != translation)
            {
                translation = value;
                isLocalDirty = true;
            }
        }
        readonly get => translation;
    }

    private Quaternion rotation = Quaternion.Identity;

    public Quaternion Rotation
    {
        set
        {
            if (value != rotation)
            {
                rotation = value;
                isLocalDirty = true;
            }
        }
        readonly get => rotation;
    }

    public Matrix4x4 WorldTransform { private set; get; } = Matrix4x4.Identity;

    private Matrix4x4 value = Matrix4x4.Identity;
    public Matrix4x4 Value
    {
        get
        {
            if (isLocalDirty)
            {
                value = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Translation);
                isLocalDirty = false;
                isWorldDirty = true;
            }
            return value;
        }
    }

    public void UpdateWorldTransform(in Matrix4x4 parent)
    {
        if (!IsWorldDirty)
        {
            return; // No change in world transform
        }
        WorldTransform = parent * Value;
        isWorldDirty = false;
    }

    public override string ToString()
    {
        return $"Transform: {Value}";
    }

    public void MarkWorldDirty()
    {
        isWorldDirty = true;
    }
}

public struct Parent()
{
    public Entity ParentEntity = Entity.Null;
}

public readonly struct Children
{
    public readonly FastList<Node> ChildNodes = [];
    public Children()
    {
    }
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