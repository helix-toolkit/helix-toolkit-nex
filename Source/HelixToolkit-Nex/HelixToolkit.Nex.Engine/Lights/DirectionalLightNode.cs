using HelixToolkit.Nex.Engine.Components;

namespace HelixToolkit.Nex.Lights;

/// <summary>
/// Represents a directional light node in the scene graph, which emits light in a specific direction and affects all objects in the scene uniformly.
/// </summary>
public class DirectionalLightNode : Node
{
    public DirectionalLightNode(World world, string name = "DirectionalLight")
        : this(world, name, new DirectionalLightInfo()) { }

    public DirectionalLightNode(World world, string name, DirectionalLightInfo component)
        : this(world, name, ref component) { }

    public DirectionalLightNode(World world, string name, ref DirectionalLightInfo component)
        : base(world, name)
    {
        Entity.Set(ref component);
    }

    /// <summary>
    /// Gets or sets the direction of the light in world space, which defines the orientation of the light source and the direction in which the light is emitted.
    /// </summary>
    public Vector3 Direction
    {
        get => Entity.Get<DirectionalLightInfo>().Direction;
        set
        {
            Entity.Update<DirectionalLightInfo>(comp =>
            {
                comp.Direction = value;
                return comp;
            });
        }
    }

    /// <summary>
    /// Gets or sets the color of the light, which defines the hue and saturation of the light emitted by the light source.
    /// </summary>
    public Color4 Color
    {
        get => Entity.Get<DirectionalLightInfo>().Color;
        set
        {
            Entity.Update<DirectionalLightInfo>(comp =>
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
        get => Entity.Get<DirectionalLightInfo>().Intensity;
        set
        {
            Entity.Update<DirectionalLightInfo>(comp =>
            {
                comp.Intensity = value;
                return comp;
            });
        }
    }
}
