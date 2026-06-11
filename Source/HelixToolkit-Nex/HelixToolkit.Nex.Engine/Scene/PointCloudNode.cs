using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Scene;

/// <summary>
/// A scene node that wraps a <see cref="PointCloudDrawInfo"/>, exposing all of its
/// properties individually so callers never need to manage the component directly.
/// </summary>
public class PointCloudNode : Node
{
    public PointCloudNode(World world, string name)
        : base(world, name)
    {
        Entity.Set(new PointCloudDrawInfo());
        IsRenderable = true;
    }

    public PointCloudNode(World world, string name, PointCloudDrawInfo component)
        : this(world, name, ref component) { }

    public PointCloudNode(World world, string name, ref PointCloudDrawInfo component)
        : this(world, name)
    {
        Entity.Set(ref component);
    }

    /// <summary>
    /// Gets or sets the point cloud geometry.
    /// </summary>
    public Geometry? Geometry
    {
        get => Entity.Get<PointCloudDrawInfo>().Geometry;
        set
        {
            Entity.Update<PointCloudDrawInfo>(comp =>
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
        get => Entity.Get<PointCloudDrawInfo>().Hitable;
        set
        {
            Entity.Update<PointCloudDrawInfo>(comp =>
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
        get => Entity.Get<PointCloudDrawInfo>().FixedSize;
        set
        {
            Entity.Update<PointCloudDrawInfo>(comp =>
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
        get => Entity.Get<PointCloudDrawInfo>().PointMaterialName;
        set
        {
            Entity.Update<PointCloudDrawInfo>(comp =>
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
        get => Entity.Get<PointCloudDrawInfo>().PointMaterialId;
        set
        {
            Entity.Update<PointCloudDrawInfo>(comp =>
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
        get => Entity.Get<PointCloudDrawInfo>().Size;
        set
        {
            Entity.Update<PointCloudDrawInfo>(comp =>
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
        get => Entity.Get<PointCloudDrawInfo>().Color;
        set
        {
            Entity.Update<PointCloudDrawInfo>(comp =>
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
        get => Entity.Get<PointCloudDrawInfo>().TextureIndex;
        set
        {
            Entity.Update<PointCloudDrawInfo>(comp =>
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
        get => Entity.Get<PointCloudDrawInfo>().SamplerIndex;
        set
        {
            Entity.Update<PointCloudDrawInfo>(comp =>
            {
                comp.SamplerIndex = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets the number of valid points.
    /// </summary>
    public int PointCount => Entity.Get<PointCloudDrawInfo>().PointCount;

    /// <summary>
    /// Gets whether the underlying <see cref="PointCloudDrawInfo"/> has valid point data.
    /// </summary>
    public bool IsPointCloudValid => Entity.Get<PointCloudDrawInfo>().Valid;
}
