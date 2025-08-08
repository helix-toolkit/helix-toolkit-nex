using Arch.Core;
using System.Numerics;

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
    public Vector3 Scale { set; get; } = Vector3.One;

    public Vector3 Translation { set; get; } = Vector3.Zero;

    public Quaternion Rotation { set; get; } = Quaternion.Identity;

    public Matrix4x4 WorldTransform { private set; get; } = Matrix4x4.Identity;

    public readonly Matrix4x4 Value => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Translation);

    public void UpdateWorldTransform(ref Matrix4x4 parent)
    {
        WorldTransform = Value * parent;
    }

    public override readonly string ToString()
    {
        return $"Transform: {Value}";
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