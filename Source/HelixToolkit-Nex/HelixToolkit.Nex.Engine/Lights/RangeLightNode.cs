using HelixToolkit.Nex.Engine.Components;

namespace HelixToolkit.Nex.Lights;

public abstract class RangeLightNode : Node
{
    public RangeLightNode(World world, string name, RangeLightType type)
        : base(world, name)
    {
        Entity.Set(new RangeLightInfo(type));
    }

    /// <summary>
    /// Gets or sets the position of the light in world space, which defines the location of the light source in the scene.
    /// </summary>
    public Vector3 Position
    {
        get => Entity.Get<RangeLightInfo>().Position;
        set
        {
            Entity.Update<RangeLightInfo>(comp =>
            {
                comp.Position = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the color of the light, which defines the hue and saturation of the light emitted by the light source.
    /// </summary>
    public Color4 Color
    {
        get => Entity.Get<RangeLightInfo>().Color;
        set
        {
            Entity.Update<RangeLightInfo>(comp =>
            {
                comp.Color = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the intensity of the light, which defines how bright the light is in the scene.
    /// </summary>
    public float Intensity
    {
        get => Entity.Get<RangeLightInfo>().Intensity;
        set
        {
            Entity.Update<RangeLightInfo>(comp =>
            {
                comp.Intensity = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the range of the light, which defines how far the light can reach in the scene.
    /// </summary>
    public float Range
    {
        get => Entity.Get<RangeLightInfo>().Range;
        set
        {
            Entity.Update<RangeLightInfo>(comp =>
            {
                comp.Range = value;
                return comp;
            });
        }
    }
}

/// <summary>
/// Represents a point light node in the scene graph, which emits light uniformly in all directions from a single point in space.
/// </summary>
public class PointLightNode : RangeLightNode
{
    public PointLightNode(World world, string name = "PointLight")
        : base(world, name, RangeLightType.Point) { }
}

/// <summary>
/// Represents a spotlight node in the scene graph, which emits light in a specific direction with a defined cone angle.
/// </summary>
public class SpotLightNode : RangeLightNode
{
    public SpotLightNode(World world, string name = "SpotLight")
        : base(world, name, RangeLightType.Spot) { }

    /// <summary>
    /// Gets or sets the direction of the spotlight, which defines the orientation of the light emitted by the spotlight.
    /// </summary>
    public Vector3 Direction
    {
        get => Entity.Get<RangeLightInfo>().Direction;
        set
        {
            Entity.Update<RangeLightInfo>(comp =>
            {
                comp.Direction = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the spot angles of the spotlight, which define the inner and outer cone angles of the light emitted by the spotlight.
    /// </summary>
    public Vector2 SpotAngles
    {
        get => Entity.Get<RangeLightInfo>().SpotAngles;
        set
        {
            Entity.Update<RangeLightInfo>(comp =>
            {
                comp.SpotAngles = value;
                return comp;
            });
        }
    }
}
