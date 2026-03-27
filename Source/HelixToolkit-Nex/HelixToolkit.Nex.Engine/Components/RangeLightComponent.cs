using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Engine.Components;

public enum RangeLightType : uint
{
    Point = 1,
    Spot = 2,
}

public sealed class RangeLightComponent(RangeLightType type) : IIndexable
{
    internal Light Light = new() { Type = (uint)type };

    public RangeLightType Type { get; } = type;

    public Vector3 Position
    {
        set
        {
            if (Light.Position != value)
            {
                Light.Position = value;
            }
        }
        get { return Light.Position; }
    }

    public Vector3 Direction
    {
        set
        {
            if (Light.Direction != value)
            {
                Light.Direction = value;
            }
        }
        get { return Light.Direction; }
    }

    public float Range
    {
        set
        {
            if (Light.Range != value)
            {
                Light.Range = value;
            }
        }
        get { return Light.Range; }
    }

    public Color4 Color
    {
        set { Light.Color = value.ToVector3(); }
        get { return new Color4(Light.Color); }
    }

    public float Intensity
    {
        set
        {
            if (Light.Intensity != value)
            {
                Light.Intensity = value;
            }
        }
        get { return Light.Intensity; }
    }

    public Vector2 SpotAngles
    {
        set
        {
            if (Light.SpotAngles != value)
            {
                Light.SpotAngles = value;
            }
        }
        get { return Light.SpotAngles; }
    }

    public int Index { internal set; get; } = -1;
}
