using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HelixToolkit.Nex.Graphics;

public static class GraphicsSettings
{
    public static bool EnableDebug { get; set; } = true; // Enable debug mode by default
    public static bool SamplerMip_Disabled { get; set; } = false; // Enable mipmaps by default
}
