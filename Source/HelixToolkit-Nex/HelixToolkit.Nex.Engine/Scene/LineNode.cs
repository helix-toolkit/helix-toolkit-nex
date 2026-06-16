using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Scene;

/// <summary>
/// A scene node that wraps a <see cref="LineDrawInfo"/>, exposing all of its
/// properties individually so callers never need to manage the component directly.
/// <para>
/// Line geometry is always a LINE LIST of disjoint 2-vertex segments: the vertex buffer
/// holds pairs [A0,A1, B0,B1, ...] and <c>LineCount = Vertices.Count / 2</c>.
/// </para>
/// </summary>
public class LineNode : Node
{
    public LineNode(World world, string name)
        : base(world, name)
    {
        // LineDrawInfo.Valid requires a non-empty LineMaterialName, so default it to
        // "Default" so a freshly created LineNode with geometry is immediately valid.
        Entity.Set(
            new LineDrawInfo
            {
                LineMaterialName = "Default",
                Cullable = true,
                Hitable = true,
            }
        );
        IsRenderable = true;
    }

    public LineNode(World world, string name, LineDrawInfo component)
        : this(world, name, ref component) { }

    public LineNode(World world, string name, ref LineDrawInfo component)
        : this(world, name)
    {
        Entity.Set(ref component);
    }

    /// <summary>
    /// Gets or sets the line geometry. Must be a line list of disjoint 2-vertex segments.
    /// </summary>
    public Geometry? Geometry
    {
        get => Entity.Get<LineDrawInfo>().Geometry;
        set
        {
            Entity.Update<LineDrawInfo>(comp =>
            {
                comp.Geometry = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the line color used when vertex colors are not provided.
    /// </summary>
    public Color4 LineColor
    {
        get => Entity.Get<LineDrawInfo>().LineColor;
        set
        {
            Entity.Update<LineDrawInfo>(comp =>
            {
                comp.LineColor = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the screen-space line width in pixels. Clamped by the shader to [1, 64].
    /// </summary>
    public float LineThickness
    {
        get => Entity.Get<LineDrawInfo>().LineThickness;
        set
        {
            Entity.Update<LineDrawInfo>(comp =>
            {
                comp.LineThickness = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the line material name used for shader lookup.
    /// </summary>
    public string? LineMaterialName
    {
        get => Entity.Get<LineDrawInfo>().LineMaterialName;
        set
        {
            Entity.Update<LineDrawInfo>(comp =>
            {
                comp.LineMaterialName = value ?? string.Empty;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets whether this line object can be hit-tested via GPU picking.
    /// </summary>
    public bool Hitable
    {
        get => Entity.Get<LineDrawInfo>().Hitable;
        set
        {
            Entity.Update<LineDrawInfo>(comp =>
            {
                comp.Hitable = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets whether this line object is cullable.
    /// </summary>
    public bool Cullable
    {
        get => Entity.Get<LineDrawInfo>().Cullable;
        set
        {
            Entity.Update<LineDrawInfo>(comp =>
            {
                comp.Cullable = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets the number of line segments (Vertices.Count / 2).
    /// </summary>
    public int LineCount => Entity.Get<LineDrawInfo>().LineCount;

    /// <summary>
    /// Gets whether the underlying <see cref="LineDrawInfo"/> has valid line data.
    /// </summary>
    public bool IsLineValid => Entity.Get<LineDrawInfo>().Valid;
}
