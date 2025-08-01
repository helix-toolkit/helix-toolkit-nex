namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Represents a rendering pass configuration, including color, depth, and stencil attachments.
/// </summary>
/// <remarks>The <see cref="RenderPass"/> class is used to define the setup for a rendering pass, specifying how
/// color, depth, and stencil attachments should be handled. It includes configurations for loading, storing, and
/// resolving attachments, as well as clear values for color, depth, and stencil.</remarks>
public sealed class RenderPass
{
    public struct AttachmentDesc()
    {
        public LoadOp LoadOp = LoadOp.Invalid;
        public StoreOp StoreOp = StoreOp.Store;
        public ResolveMode ResolveMode = ResolveMode.Average;
        public uint8_t Layer = 0;
        public uint8_t Level = 0;
        public Color4 ClearColor = new(0, 0, 0, 0);
        public float ClearDepth = 1.0f;
        public uint32_t ClearStencil = 0;
    }

    public readonly AttachmentDesc[] Colors = new AttachmentDesc[Constants.MAX_COLOR_ATTACHMENTS];

    public AttachmentDesc Depth = new() { LoadOp = LoadOp.DontCare, StoreOp = StoreOp.DontCare };
    public AttachmentDesc Stencil = new() { LoadOp = LoadOp.Invalid, StoreOp = StoreOp.DontCare };
    public uint32_t LayerCount = 1;
    public uint32_t ViewMask;

    public RenderPass(IEnumerable<AttachmentDesc>? colors = null)
    {
        for (uint32_t i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
        {
            Colors[i] = new AttachmentDesc();
        }
        if (colors != null)
        {
            uint32_t i = 0;
            foreach (var color in colors)
            {
                if (i < Constants.MAX_COLOR_ATTACHMENTS)
                {
                    Colors[i] = color;
                    i++;
                }
                else
                {
                    break; // Avoid exceeding the maximum number of color attachments
                }
            }
        }
    }

    public RenderPass(in AttachmentDesc colorAttachment) : this(Enumerable.Repeat(colorAttachment, 1))
    {
    }

    public uint32_t GetNumColorAttachments()
    {
        for (uint32_t i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
        {
            if (Colors[i].LoadOp == LoadOp.Invalid)
            {
                return i;
            }
        }
        return Constants.MAX_COLOR_ATTACHMENTS;
    }
}