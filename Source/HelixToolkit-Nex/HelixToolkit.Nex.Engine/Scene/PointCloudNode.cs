using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Scene;

/// <summary>
/// A scene node that wraps a <see cref="PointDrawInfo"/>, exposing all of its
/// properties individually so callers never need to manage the component directly.
/// </summary>
public class PointCloudNode : Node
{
    public PointCloudNode(World world, string name)
        : base(world, name)
    {
        Entity.Set(
            new PointDrawInfo()
            {
                Cullable = true,
                Hitable = true,
                PointMaterialTypeName = "Default",
            }
        );
        IsRenderable = true;
    }

    public PointCloudNode(World world, string name, PointDrawInfo component)
        : this(world, name, ref component) { }

    public PointCloudNode(World world, string name, ref PointDrawInfo component)
        : this(world, name)
    {
        Entity.Set(ref component);
    }

    /// <summary>
    /// Gets or sets the point cloud geometry.
    /// </summary>
    public Geometry? Geometry
    {
        get => Entity.Get<PointDrawInfo>().Geometry;
        set
        {
            Entity.Update<PointDrawInfo>(comp =>
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
        get => Entity.Get<PointDrawInfo>().Hitable;
        set
        {
            Entity.Update<PointDrawInfo>(comp =>
            {
                comp.Hitable = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets whether this point cloud can be frustum-culled.
    /// </summary>
    public bool Cullable
    {
        get => Entity.Get<PointDrawInfo>().Cullable;
        set
        {
            Entity.Update<PointDrawInfo>(comp =>
            {
                comp.Cullable = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets whether the point size is fixed in screen space.
    /// </summary>
    public bool FixedSize
    {
        get => Entity.Get<PointDrawInfo>().FixedSize;
        set
        {
            Entity.Update<PointDrawInfo>(comp =>
            {
                comp.FixedSize = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the point material name used for shader lookup.
    /// </summary>
    public string PointMaterialName
    {
        get => Entity.Get<PointDrawInfo>().PointMaterialTypeName;
        set
        {
            Entity.Update<PointDrawInfo>(comp =>
            {
                comp.PointMaterialTypeName = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the size of each point.
    /// </summary>
    public float Size
    {
        get => Entity.Get<PointDrawInfo>().PointSize;
        set
        {
            Entity.Update<PointDrawInfo>(comp =>
            {
                comp.PointSize = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the default color for all points when vertex colors are not provided.
    /// </summary>
    public Color4 Color
    {
        get => Entity.Get<PointDrawInfo>().PointColor;
        set
        {
            Entity.Update<PointDrawInfo>(comp =>
            {
                comp.PointColor = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the texture index used for rendering.
    /// </summary>
    public uint TextureIndex
    {
        get => Entity.Get<PointDrawInfo>().TextureIndex;
        set
        {
            Entity.Update<PointDrawInfo>(comp =>
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
        get => Entity.Get<PointDrawInfo>().SamplerIndex;
        set
        {
            Entity.Update<PointDrawInfo>(comp =>
            {
                comp.SamplerIndex = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets the number of valid points.
    /// </summary>
    public int PointCount => Entity.Get<PointDrawInfo>().PointCount;

    /// <summary>
    /// Gets whether the underlying <see cref="PointDrawInfo"/> has valid point data.
    /// </summary>
    public bool IsPointCloudValid => Entity.Get<PointDrawInfo>().Valid;
}
