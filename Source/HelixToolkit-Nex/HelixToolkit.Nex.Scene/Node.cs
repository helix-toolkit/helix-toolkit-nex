using System.Collections.Concurrent;

namespace HelixToolkit.Nex.Scene;

public class Node : IDisposable
{
    // Registry design rationale:
    //
    // The ECS (World, ComponentManager) is single-threaded per world — none of
    // its hot-path accessors (HasEntity, Get<T>, etc.) use locks. Building nodes
    // across multiple worlds from different threads is therefore safe as long as
    // each world is only accessed from one thread at a time, which matches the
    // ECS contract.
    //
    // A single global ConcurrentDictionary keyed by (worldId, entityId) would:
    //   - Pay a memory fence on every FindNode call (hot path: Parent getter,
    //     HasParent, UpdateTransforms)
    //   - Provide false safety — the surrounding ECS operations are not
    //     concurrent-safe anyway
    //
    // Instead we use a two-level structure:
    //   outer: static Dictionary<byte, Dictionary<int, Node>>
    //            worldId → per-world registry
    //   inner: plain Dictionary<int, Node>  (entityId → Node)
    //
    // The outer dictionary is only mutated when a world's first/last node is
    // registered (rare). A lightweight lock guards only that mutation.
    // The inner dictionary is accessed exclusively on the owning world's thread,
    // so it needs no synchronization at all.

    private static readonly Dictionary<byte, Dictionary<int, Node>> _worldRegistries = [];
    private static readonly object _registryLock = new();

    private static Dictionary<int, Node> GetOrCreateWorldRegistry(byte worldId)
    {
        // Fast path — world registry already exists (no lock needed for read
        // because world creation itself is serialized by World.CreateWorld).
        if (_worldRegistries.TryGetValue(worldId, out var registry))
        {
            return registry;
        }
        lock (_registryLock)
        {
            if (!_worldRegistries.TryGetValue(worldId, out registry))
            {
                registry = new Dictionary<int, Node>();
                _worldRegistries[worldId] = registry;
            }
            return registry;
        }
    }

    private static void RemoveWorldRegistry(byte worldId)
    {
        lock (_registryLock)
        {
            _worldRegistries.Remove(worldId);
        }
    }

    // Called only from within this world's thread — no lock required.
    internal static Node? FindNode(byte worldId, int entityId)
    {
        if (_worldRegistries.TryGetValue(worldId, out var registry))
        {
            registry.TryGetValue(entityId, out var node);
            return node;
        }
        return null;
    }

    // -------------------------------------------------------------------------

    public World World { get; }
    public Entity Entity { private set; get; }

    public NodeInfo Info => Entity.Get<NodeInfo>();

    public string Name
    {
        get => Entity.TryGet<NodeName>(out var nodeName) ? nodeName.Value : string.Empty;
        set
        {
            if (Entity.Has<NodeName>())
            {
                ref var nodeName = ref Entity.Get<NodeName>();
                nodeName.Value = value;
            }
            else
            {
                Entity.Set(new NodeName(value));
            }
        }
    }

    public Node? Parent
    {
        get
        {
            var parentEntity = Entity.Get<Parent>().ParentEntity;
            return parentEntity.Valid ? FindNode(World.Id, parentEntity.Id) : null;
        }
        private set
        {
            var currentParent = Parent;
            if (value == currentParent)
            {
                return;
            }

            if (value is null)
            {
                Entity.Set(new Parent(Entity.Null));
                Debug.Assert(!HasParent);
                ref var info = ref Entity.Get<NodeInfo>();
                if (info.Level == 0)
                {
                    return;
                }
                info.Level = 0;
                ParentEnabled = true;
            }
            else
            {
                Entity.Set(new Parent(value.Entity));
                Debug.Assert(HasParent);
                ref var info = ref Entity.Get<NodeInfo>();
                if (info.Level == value.Info.Level + 1)
                {
                    return;
                }
                info.Level = value.Info.Level + 1;
                ParentEnabled = value.Enabled;
            }
            Entity.Get<Transform>().MarkWorldDirty();
            NotifySceneChanged();
            UpdateChildrenLevels();
        }
    }

    public bool HasParent
    {
        get
        {
            var parentEntity = Entity.Get<Parent>().ParentEntity;
            return parentEntity.Valid;
        }
    }

    public IReadOnlyList<Node>? Children
    {
        get { return Entity.TryGet<Children>(out var children) ? children.ChildNodes : null; }
    }

    public int ChildCount => Children?.Count ?? 0;

    public bool HasChildren
    {
        get { return Entity.TryGet<Children>(out var children) && children.ChildNodes.Count != 0; }
    }

    public bool Alive => World.HasEntity(Entity);

    public bool IsRoot => Entity.Get<NodeInfo>().Level == 0;

    public int Level => Entity.Get<NodeInfo>().Level;

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
            NotifySceneChanged();
            if (!Entity.TryGet<Children>(out var children) || children.ChildNodes.Count == 0)
            {
                return;
            }
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
            info.ParentEnabled = value;
            if (Entity.TryGet<Children>(out var children) && children.ChildNodes.Count != 0)
            {
                foreach (var child in children.ChildNodes)
                {
                    child.ParentEnabled = value;
                }
            }
        }
    }

    public ref Transform Transform => ref Entity.Get<Transform>();

    public ref WorldTransform WorldTransform => ref Entity.Get<WorldTransform>();

    public Node(World world, Entity? entity = null)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        Entity = entity ?? world.CreateEntity();

        if (entity.HasValue)
        {
            VerifyExternalEntity(Entity);
        }

        Entity.Set(new NodeInfo(Entity.Id));
        Entity.Set(new Transform());
        Entity.Set(WorldTransform.Identity);
        Entity.Set(new Parent());

        // Register in the per-world lookup. GetOrCreateWorldRegistry is only
        // called once per world (subsequent calls hit the fast non-locking path).
        GetOrCreateWorldRegistry(world.Id)[Entity.Id] = this;

        // Clean up the per-world registry when the world is disposed.
        world.Disposing += OnWorldDisposing;
    }

    public Node(World world, string name)
        : this(world)
    {
        Name = name;
    }

    private void OnWorldDisposing(object? sender, EventArgs e)
    {
        // The world is going away — drop the entire inner dictionary rather than
        // removing entries one by one as each node is disposed.
        if (sender is World w)
        {
            w.Disposing -= OnWorldDisposing;
            RemoveWorldRegistry(w.Id);
        }
    }

    public void AddChild(Node node)
    {
        if (node.HasParent)
        {
            throw new InvalidOperationException(
                $"Node [{node}] already belongs to a parent [{node.Parent}]"
            );
        }
        if (!Entity.Has<Children>())
        {
            Entity.Set(new Children());
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
        children.Remove(node);
        node.Parent = null;
        return true;
    }

    public void SetWorldTransform(in WorldTransform transform)
    {
        Entity.Set(transform);
    }

    public void NotifyComponentChanged<T>()
    {
        Entity.NotifyComponentChanged<T>();
    }

    public void NotifyTransformChanged()
    {
        NotifyComponentChanged<Transform>();
    }

    public void NotifySceneChanged()
    {
        NotifyComponentChanged<NodeInfo>();
    }

    public override string ToString()
    {
        return $"Node: [{Name}] {Info}";
    }

    private void UpdateChildrenLevels()
    {
        if (!Entity.TryGet<Children>(out var children) || children.ChildNodes.Count == 0)
            return;
        int myLevel = Entity.Get<NodeInfo>().Level;
        foreach (var child in children.ChildNodes)
        {
            ref var childInfo = ref child.Entity.Get<NodeInfo>();
            childInfo.Level = myLevel + 1;
            child.UpdateChildrenLevels();
        }
    }

    private static void VerifyExternalEntity(in Entity entity)
    {
        if (entity.Has<NodeInfo>() && entity.Has<Transform>() && entity.Has<Parent>())
        {
            return;
        }
        throw new ArgumentException(
            "External entity must have NodeInfo, Transform, and Parent components.",
            nameof(entity)
        );
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
                    ref var children = ref Entity.Get<Children>();
                    foreach (var child in children.ChildNodes.ToArray())
                    {
                        child.Dispose();
                    }
                }
                if (HasParent)
                {
                    Parent?.RemoveChild(this);
                }

                World.Disposing -= OnWorldDisposing;

                // Remove only this node's entry. The world-level registry is
                // dropped wholesale in OnWorldDisposing when the world itself
                // is disposed, so individual removes are skipped in that case.
                if (_worldRegistries.TryGetValue(World.Id, out var registry))
                {
                    registry.Remove(Entity.Id);
                }

                Entity.Dispose();
            }
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion
}
