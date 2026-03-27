namespace HelixToolkit.Nex.Engine.Components;

public struct DirectionalLightComponent()
{
    public DirectionalLight Light = new();

    /// <summary>Light direction (for spot lights)</summary>
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

    /// <summary>Light color (linear RGB)</summary>
    public Color4 Color
    {
        set
        {
            if (Light.Color != value.ToVector3())
            {
                Light.Color = value.ToVector3();
            }
        }
        get { return new Color4(Light.Color); }
    }

    /// <summary>Light intensity</summary>
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
}
