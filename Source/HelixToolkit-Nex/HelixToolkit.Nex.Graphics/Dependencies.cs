namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Represents a collection of dependencies, including textures and buffers,  that can be submitted for processing.
/// </summary>
/// <remarks>This class provides a fixed-size collection of texture and buffer handles  that can be used to manage
/// dependencies in a rendering or processing pipeline.  The maximum number of dependencies is defined by <see
/// cref="MAX_SUBMIT_DEPENDENCIES"/>.</remarks>
public sealed class Dependencies
{
    public const uint32_t MAX_SUBMIT_DEPENDENCIES = 6;

    private readonly TextureHandle[] _textures = new TextureHandle[MAX_SUBMIT_DEPENDENCIES];

    public uint NumTextures { private set; get; } = 0;

    private readonly BufferHandle[] _buffers = new BufferHandle[MAX_SUBMIT_DEPENDENCIES];

    public ReadOnlySpan<BufferHandle> BufferSpan => _buffers.AsSpan(0, (int)NumBuffers);

    public ReadOnlySpan<TextureHandle> TextureSpan => _textures.AsSpan(0, (int)NumTextures);

    public uint NumBuffers { private set; get; } = 0;

    public readonly TextureHandle[] InputAttachments = new TextureHandle[
        Constants.MAX_COLOR_ATTACHMENTS
    ];

    public static readonly Dependencies Empty = new();

    public Dependencies(TextureHandle[]? textures = null, BufferHandle[]? buffers = null)
    {
        for (uint32_t i = 0; i < MAX_SUBMIT_DEPENDENCIES; i++)
        {
            _textures[i] = i < textures?.Length ? textures[i] : TextureHandle.Null;
            _buffers[i] = i < buffers?.Length ? buffers[i] : BufferHandle.Null;
        }
    }

    public void PushBuffer(BufferHandle buffer)
    {
        HxDebug.Assert(buffer.Valid);
        _buffers[NumBuffers++] = buffer;
    }

    public void PopBuffer()
    {
        _buffers[--NumBuffers] = BufferHandle.Null;
    }

    public void PushTexture(TextureHandle texture)
    {
        HxDebug.Assert(texture.Valid);
        _textures[NumTextures++] = texture;
    }

    public void PopTexture()
    {
        _textures[--NumTextures] = TextureHandle.Null;
    }

    public void Clear()
    {
        for (uint32_t i = 0; i < MAX_SUBMIT_DEPENDENCIES; i++)
        {
            _textures[i] = TextureHandle.Null;
            _buffers[i] = BufferHandle.Null;
        }
        NumTextures = 0;
        NumBuffers = 0;
    }
}
