namespace HelixToolkit.Nex.Scene;

public class Node : IDisposable
{
    public World World { get; }
    public Entity Entity { private set; get; }

    public NodeInfo Info => Entity.Get<NodeInfo>();

    public string Name
    {
        get => Info.Name;
        set => Entity.Get<NodeInfo>().Name = value;
    }

    public Guid Id => Info.Id;

    public Node? Parent
    {
        get
        {
            var entity = Entity.Get<Parent>().ParentEntity;
            return entity.Valid ? entity.Get<NodeInfo>().Node : null;
        }
        set
        {
            if (value is null)
            {
                Entity.Get<Parent>().ParentEntity = Entity.Null;
                Debug.Assert(!HasParent);
                ref var info = ref Entity.Get<NodeInfo>();
                if (info.Level == 0)
                {
                    return; // No change in level
                }
                info.Level = 0;
                ParentEnabled = true; // Reset parent enabled state when detaching from parent
            }
            else
            {
                Entity.Get<Parent>().ParentEntity = value.Entity;
                Debug.Assert(HasParent);
                ref var info = ref Entity.Get<NodeInfo>();
                if (info.Level == value.Info.Level + 1)
                {
                    return; // No change in level
                }
                Entity.Get<NodeInfo>().Level = value.Info.Level + 1;
                ParentEnabled = value.Enabled; // Inherit enabled state from new parent
            }
            UpdateChildrenLevels();
        }
    }

    public bool HasParent => Parent != null;

    public IReadOnlyList<Node>? Children
    {
        get { return Entity.TryGet<Children>(out var children) ? children.ChildNodes : null; }
    }

    public int ChildCount => Children?.Count ?? 0;

    public bool HasChildren => Children != null && Children.Count != 0;

    public bool Alive => World.HasEntity(Entity);

    public bool IsRoot => Info.Level == 0;

    public bool Enabled
    {
        get
        {
            ref var info = ref Entity.Get<NodeInfo>();
            return info.Enabled;
        }
        set
        {
            ref var info = ref Entity.Get<NodeInfo>();
            if (info.SelfEnabled == value)
            {
                return;
            }
            info.SelfEnabled = value;
            if (!HasChildren)
            {
                return;
            }
            ref var children = ref Entity.Get<Children>();
            foreach (var child in children.ChildNodes)
            {
                child.ParentEnabled = value;
            }
        }
    }

    internal bool ParentEnabled
    {
        get => Entity.Get<NodeInfo>().ParentEnabled;
        set
        {
            ref var info = ref Entity.Get<NodeInfo>();
            if (info.ParentEnabled == value)
            {
                return;
            }
            Entity.Get<NodeInfo>().ParentEnabled = value;
            if (info.SelfEnabled && HasChildren)
            {
                ref var children = ref Entity.Get<Children>();
                foreach (var child in children.ChildNodes)
                {
                    child.ParentEnabled = value;
                }
            }
        }
    }

    public ref Transform Transform => ref Entity.Get<Transform>();

    public ref WorldTransform WorldTransform => ref Entity.Get<WorldTransform>();

    public Node(in World world, in Entity? entity = null)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        Entity = entity ?? world.CreateEntity();
        Entity.Set(new NodeInfo(this));
        Entity.Set(new Transform());
        Entity.Set(WorldTransform.Identity);
        Entity.Set(new Parent());
        Entity.Set(new Children());
        VerifyExternalEntity(Entity);
    }

    public Node(in World world, string name)
        : this(world)
    {
        Name = name;
    }

    public void AddChild(Node node)
    {
        if (node.HasParent)
        {
            throw new InvalidOperationException(
                $"Node [{node}] already belongs to a parent [{node.Parent}]"
            );
        }
        ref var children = ref Entity.Get<Children>();
        children.Add(node);
        node.Parent = this;
    }

    public bool RemoveChild(Node node)
    {
        if (!HasChildren)
        {
            return false;
        }
        ref var children = ref Entity.Get<Children>();
        children.Remove(node);
        node.Parent = null;
        return true;
    }

    public bool RemoveChildAt(int index)
    {
        if (!HasChildren || index < 0 || index >= ChildCount)
        {
            return false;
        }
        ref var children = ref Entity.Get<Children>();
        var node = children.ChildNodes[index];
        children.ChildNodes.RemoveAt(index);
        node.Parent = null;
        return true;
    }

    public void SetWorldTransform(in WorldTransform transform)
    {
        Entity.Set(transform);
    }

    public override string ToString()
    {
        return $"Node: {Info}";
    }

    private void UpdateChildrenLevels()
    {
        if (!HasChildren)
            return;
        ref var children = ref Entity.Get<Children>();
        foreach (var child in children.ChildNodes)
        {
            child.Entity.Get<NodeInfo>().Level = Info.Level + 1;
            child.UpdateChildrenLevels();
        }
    }

    private static void VerifyExternalEntity(in Entity entity)
    {
        if (
            entity.Has<NodeInfo>()
            && entity.Has<Transform>()
            && entity.Has<Parent>()
            && entity.Has<Children>()
        )
        {
            return;
        }
        throw new ArgumentException("Entity does not have a NodeInfo component.", nameof(entity));
    }

    #region IDisposable Support
    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                if (HasChildren)
                {
                    // Remove all children before destroying the node
                    ref var children = ref Entity.Get<Children>();
                    foreach (var child in children.ChildNodes.ToArray())
                    {
                        child.Dispose();
                    }
                }
                // Remove the node from its parent if it has one
                if (HasParent)
                {
                    Parent?.RemoveChild(this);
                }
                Entity.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~Node()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion
}
