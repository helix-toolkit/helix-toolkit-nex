namespace HelixToolkit.Nex.Graphics;

/// <summary>
/// Represents a collection of dependencies, including textures and buffers,  that can be submitted for processing.
/// </summary>
/// <remarks>This class provides a fixed-size collection of texture and buffer handles  that can be used to manage
/// dependencies in a rendering or processing pipeline.  The maximum number of dependencies is defined by <see
/// cref="MAX_SUBMIT_DEPENDENCIES"/>.</remarks>
public sealed class Dependencies
{
    public const uint32_t MAX_SUBMIT_DEPENDENCIES = 4;

    public readonly TextureHandle[] Textures = new TextureHandle[MAX_SUBMIT_DEPENDENCIES];

    public readonly BufferHandle[] Buffers = new BufferHandle[MAX_SUBMIT_DEPENDENCIES];

    public static readonly Dependencies Empty = new();

    public Dependencies()
    {
        for (uint32_t i = 0; i < MAX_SUBMIT_DEPENDENCIES; i++)
        {
            Textures[i] = TextureHandle.Null;
            Buffers[i] = BufferHandle.Null;
        }
    }
}
