namespace HelixToolkit.Nex.Engine.Systems;

public sealed class SystemContext
{
    public WorldDataProvider DataProvider { get; }
    public IResourceManager ResourceManager => DataProvider.ResourceManager;

    public SystemContext(WorldDataProvider dataProvider)
    {
        DataProvider = dataProvider;
    }
}

public abstract class System : Initializable
{
    /// <summary>
    /// Gets or sets whether this system is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the execution priority. Lower values execute first.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Gets the set of component types this system requires on an entity.
    /// </summary>
    public IReadOnlySet<Type> RequiredComponents => _requiredComponents;
    private readonly HashSet<Type> _requiredComponents = [];

    protected System(int priority = 0)
    {
        Priority = priority;
    }

    /// <summary>
    /// Registers a required component type for entity matching.
    /// </summary>
    protected void Require<TComponent>()
        where TComponent : class
    {
        _requiredComponents.Add(typeof(TComponent));
    }

    /// <summary>
    /// Called every frame to perform logic updates.
    /// </summary>
    /// <param name="deltaTime">Time elapsed since the last update in seconds.</param>
    public virtual void Update(SystemContext context, float deltaTime) { }

    /// <summary>
    /// Determines whether the given set of component types satisfies this system's requirements.
    /// </summary>
    public bool Matches(IEnumerable<Type> componentTypes)
    {
        return _requiredComponents.All(componentTypes.Contains);
    }
}
