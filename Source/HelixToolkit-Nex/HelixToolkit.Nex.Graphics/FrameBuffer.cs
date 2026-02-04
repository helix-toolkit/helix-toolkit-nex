namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Represents a framebuffer used in rendering operations, containing color and depth-stencil attachments.
/// </summary>
/// <remarks>The <see cref="Framebuffer"/> class provides a structure for managing multiple color attachments and
/// a single depth-stencil attachment. It is designed to be used in graphics applications where rendering to textures is
/// required.</remarks>
public sealed class Framebuffer
{
    public struct AttachmentDesc()
    {
        public TextureHandle Texture = TextureHandle.Null;
        public TextureHandle ResolveTexture = TextureHandle.Null;
    };

    public readonly AttachmentDesc[] Colors = new AttachmentDesc[Constants.MAX_COLOR_ATTACHMENTS];
    public AttachmentDesc DepthStencil = new();

    public string DebugName = string.Empty;

    public Framebuffer(IEnumerable<AttachmentDesc>? attachments = null)
    {
        for (uint32_t i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
        {
            Colors[i] = new AttachmentDesc();
        }
        if (attachments != null)
        {
            uint32_t i = 0;
            foreach (var attachment in attachments)
            {
                if (i < Constants.MAX_COLOR_ATTACHMENTS)
                {
                    Colors[i] = attachment;
                    i++;
                }
                else
                {
                    break; // Avoid exceeding the maximum number of color attachments
                }
            }
        }
    }

    public Framebuffer(in AttachmentDesc colorAttachment) : this(Enumerable.Repeat(colorAttachment, 1))
    {
    }

    public uint32_t GetNumColorAttachments()
    {
        for (uint32_t i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
        {
            if (!Colors[i].Texture.Valid)
            {
                return i;
            }
        }
        return Constants.MAX_COLOR_ATTACHMENTS;
    }

    public static readonly Framebuffer Null = new();
}
