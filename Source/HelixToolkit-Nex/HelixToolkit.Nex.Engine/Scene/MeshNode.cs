using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Scene;

/// <summary>
/// A scene node that wraps a <see cref="MeshComponent"/>, exposing all of its
/// properties individually so callers never need to manage the component directly.
/// </summary>
public class MeshNode : Node
{
    public MeshNode(World world, string name)
        : base(world, name)
    {
        Entity.Set(MeshComponent.Empty);
    }

    public MeshNode(World world, string name, MeshComponent meshComponent)
        : base(world, name)
    {
        Entity.Set(meshComponent);
    }

    /// <summary>
    /// Gets or sets the geometry resource.
    /// </summary>
    public Geometry? Geometry
    {
        get => Entity.Get<MeshComponent>().Geometry;
        set
        {
            Entity.Update<MeshComponent>(comp =>
            {
                comp.Geometry = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the PBR material properties.
    /// </summary>
    public PBRMaterialProperties? MaterialProperties
    {
        get => Entity.Get<MeshComponent>().MaterialProperties;
        set
        {
            Entity.Update<MeshComponent>(comp =>
            {
                comp.MaterialProperties = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the instancing data.
    /// </summary>
    public Instancing? Instancing
    {
        get => Entity.Get<MeshComponent>().Instancing;
        set
        {
            Entity.Update<MeshComponent>(comp =>
            {
                comp.Instancing = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets whether this mesh can be culled.
    /// </summary>
    public bool Cullable
    {
        get => Entity.Get<MeshComponent>().Cullable;
        set
        {
            Entity.Update<MeshComponent>(comp =>
            {
                comp.Cullable = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets whether this mesh can be hit-tested.
    /// </summary>
    public bool Hitable
    {
        get => Entity.Get<MeshComponent>().Hitable;
        set
        {
            Entity.Update<MeshComponent>(comp =>
            {
                comp.Hitable = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets whether the underlying <see cref="MeshComponent"/> is valid
    /// (has both geometry and material assigned).
    /// </summary>
    public bool IsMeshValid => Entity.Get<MeshComponent>().Valid;

    /// <summary>
    /// Marks this mesh as transparent by adding the <see cref="TransparentComponent"/> tag.
    /// </summary>
    public bool IsTransparent
    {
        get => Entity.Has<TransparentComponent>();
        set
        {
            if (value && !Entity.Has<TransparentComponent>())
            {
                Entity.Tag<TransparentComponent>();
            }
            else if (!value && Entity.Has<TransparentComponent>())
            {
                Entity.Remove<TransparentComponent>();
            }
        }
    }
}
