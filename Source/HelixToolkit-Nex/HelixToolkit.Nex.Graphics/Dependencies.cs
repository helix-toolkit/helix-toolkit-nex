namespace HelixToolkit.Nex.Graphics;

public struct DependencyScope(Dependencies dependencies, DependencyScope.OpType op) : IDisposable
{
    public enum OpType
    {
        None,
        Buffer,
        Texture,
        InputAttachment,
    }

    private readonly Dependencies _dependencies = dependencies;
    private readonly OpType _op = op;
    private bool _disposed = false;

    public void Dispose()
    {
        if (_disposed)
            return;
        switch (_op)
        {
            case OpType.Texture:
                _dependencies.PopTexture();
                break;
            case OpType.Buffer:
                _dependencies.PopBuffer();
                break;
            case OpType.InputAttachment:
                _dependencies.PopInputAttachment();
                break;
        }
        _disposed = true;
    }
}

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

    private readonly BufferHandle[] _buffers = new BufferHandle[MAX_SUBMIT_DEPENDENCIES];

    private readonly TextureHandle[] _inputAttachments = new TextureHandle[
        Constants.MAX_COLOR_ATTACHMENTS
    ];

    public ReadOnlySpan<BufferHandle> BufferSpan => _buffers.AsSpan(0, (int)NumBuffers);

    public ReadOnlySpan<TextureHandle> TextureSpan => _textures.AsSpan(0, (int)NumTextures);

    public ReadOnlySpan<TextureHandle> InputAttachmentSpan => _inputAttachments.AsSpan(0, (int)NumInputAttachments);

    public uint NumTextures { private set; get; } = 0;

    public uint NumBuffers { private set; get; } = 0;

    public uint NumInputAttachments { private set; get; } = 0;


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

    public DependencyScope PushBufferScoped(BufferHandle buffer)
    {
        PushBuffer(buffer);
        return new DependencyScope(this, DependencyScope.OpType.Buffer);
    }

    public void PopBuffer()
    {
        if (NumBuffers == 0)
        {
            return;
        }
        _buffers[--NumBuffers] = BufferHandle.Null;
    }

    public void PushTexture(TextureHandle texture)
    {
        HxDebug.Assert(texture.Valid);
        _textures[NumTextures++] = texture;
    }

    public DependencyScope PushTextureScoped(TextureHandle texture)
    {
        PushTexture(texture);
        return new DependencyScope(this, DependencyScope.OpType.Texture);
    }

    public void PopTexture()
    {
        if (NumTextures == 0)
        {
            return;
        }
        _textures[--NumTextures] = TextureHandle.Null;
    }

    public void PushInputAttachment(TextureHandle texture)
    {
        HxDebug.Assert(texture.Valid);
        _inputAttachments[NumInputAttachments++] = texture;
    }

    public void PopInputAttachment()
    {
        if (NumInputAttachments == 0)
        {
            return;
        }
        _inputAttachments[--NumInputAttachments] = TextureHandle.Null;
    }

    public DependencyScope PushInputAttachmentScoped(TextureHandle texture)
    {
        PushInputAttachment(texture);
        return new DependencyScope(this, DependencyScope.OpType.InputAttachment);
    }

    public void Clear()
    {
        for (var i = 0; i < MAX_SUBMIT_DEPENDENCIES; i++)
        {
            _textures[i] = TextureHandle.Null;
            _buffers[i] = BufferHandle.Null;
        }
        for (var i = 0; i < Constants.MAX_COLOR_ATTACHMENTS; i++)
        {
            _inputAttachments[i] = TextureHandle.Null;
        }
        NumTextures = 0;
        NumBuffers = 0;
        NumInputAttachments = 0;
    }
}
