using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Scene;

/// <summary>
/// A scene node that wraps a <see cref="PointCloudComponent"/>, exposing all of its
/// properties individually so callers never need to manage the component directly.
/// </summary>
public class PointCloudNode : Node
{
    public PointCloudNode(World world, string name)
        : base(world, name)
    {
        Entity.Set(new PointCloudComponent());
    }

    public PointCloudNode(World world, string name, PointCloudComponent component)
        : base(world, name)
    {
        Entity.Set(ref component);
    }

    /// <summary>
    /// Gets or sets the point cloud geometry.
    /// </summary>
    public Geometry? Geometry
    {
        get => Entity.Get<PointCloudComponent>().Geometry;
        set
        {
            Entity.Update<PointCloudComponent>(comp =>
            {
                comp.Geometry = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets whether this point cloud can be hit-tested.
    /// </summary>
    public bool Hitable
    {
        get => Entity.Get<PointCloudComponent>().Hitable;
        set
        {
            Entity.Update<PointCloudComponent>(comp =>
            {
                comp.Hitable = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets whether the point size is fixed in screen space.
    /// </summary>
    public bool FixedSize
    {
        get => Entity.Get<PointCloudComponent>().FixedSize;
        set
        {
            Entity.Update<PointCloudComponent>(comp =>
            {
                comp.FixedSize = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the point material name used for shader lookup.
    /// </summary>
    public string? PointMaterialName
    {
        get => Entity.Get<PointCloudComponent>().PointMaterialName;
        set
        {
            Entity.Update<PointCloudComponent>(comp =>
            {
                comp.PointMaterialName = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the point material type ID.
    /// </summary>
    public MaterialTypeId PointMaterialId
    {
        get => Entity.Get<PointCloudComponent>().PointMaterialId;
        set
        {
            Entity.Update<PointCloudComponent>(comp =>
            {
                comp.PointMaterialId = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the size of each point.
    /// </summary>
    public float Size
    {
        get => Entity.Get<PointCloudComponent>().Size;
        set
        {
            Entity.Update<PointCloudComponent>(comp =>
            {
                comp.Size = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the default color for all points when vertex colors are not provided.
    /// </summary>
    public Color4 Color
    {
        get => Entity.Get<PointCloudComponent>().Color;
        set
        {
            Entity.Update<PointCloudComponent>(comp =>
            {
                comp.Color = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the texture index used for rendering.
    /// </summary>
    public uint TextureIndex
    {
        get => Entity.Get<PointCloudComponent>().TextureIndex;
        set
        {
            Entity.Update<PointCloudComponent>(comp =>
            {
                comp.TextureIndex = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the sampler index used for rendering.
    /// </summary>
    public uint SamplerIndex
    {
        get => Entity.Get<PointCloudComponent>().SamplerIndex;
        set
        {
            Entity.Update<PointCloudComponent>(comp =>
            {
                comp.SamplerIndex = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets the number of valid points.
    /// </summary>
    public int PointCount => Entity.Get<PointCloudComponent>().PointCount;

    /// <summary>
    /// Gets whether the underlying <see cref="PointCloudComponent"/> has valid point data.
    /// </summary>
    public bool IsPointCloudValid => Entity.Get<PointCloudComponent>().Valid;
}
