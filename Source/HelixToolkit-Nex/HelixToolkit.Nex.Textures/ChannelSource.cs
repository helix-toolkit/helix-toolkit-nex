namespace HelixToolkit.Nex.Textures;

/// <summary>
/// Describes where the value for one OMR output channel comes from.
/// Either a specific color channel extracted from a source <see cref="Image"/> (<see cref="ImageChannel"/>),
/// an inverted color channel extracted from a source <see cref="Image"/> (<see cref="InvertedImageChannel"/>),
/// or a constant byte value written to every pixel (<see cref="Constant"/>).
/// </summary>
public abstract class ChannelSource
{
    // Private constructor prevents external subclassing, making the hierarchy exhaustively sealed.
    private ChannelSource() { }

    /// <summary>
    /// A channel source that reads a specific color channel from a source <see cref="Image"/>.
    /// </summary>
    public sealed class ImageChannel : ChannelSource
    {
        /// <summary>The source image to sample from.</summary>
        public Image Source { get; }

        /// <summary>The color channel to read from the source image.</summary>
        public ChannelComponent Channel { get; }

        /// <summary>
        /// Initializes a new <see cref="ImageChannel"/> source.
        /// </summary>
        /// <param name="source">The source image. Must not be null.</param>
        /// <param name="channel">The color channel to read from the source image.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
        public ImageChannel(Image source, ChannelComponent channel)
        {
            ArgumentNullException.ThrowIfNull(source);
            Source = source;
            Channel = channel;
        }
    }

    /// <summary>
    /// A channel source that writes a fixed byte value to every pixel of the output channel.
    /// </summary>
    public sealed class Constant : ChannelSource
    {
        /// <summary>The constant byte value to write to every output pixel.</summary>
        public byte Value { get; }

        /// <summary>
        /// Initializes a new <see cref="Constant"/> source with the specified value.
        /// </summary>
        /// <param name="value">The constant byte value in the range [0, 255].</param>
        public Constant(byte value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// A channel source that reads a specific color channel from a source <see cref="Image"/>
    /// and applies the inversion transform <c>(byte)(255 - rawValue)</c> to each sampled byte.
    /// Use this to convert a gloss map (where 255 = fully smooth) into the roughness convention
    /// used by the OMR Blue channel (where 255 = fully rough).
    /// </summary>
    public sealed class InvertedImageChannel : ChannelSource
    {
        /// <summary>The source image to sample from.</summary>
        public Image Source { get; }

        /// <summary>The color channel to read from the source image.</summary>
        public ChannelComponent Channel { get; }

        /// <summary>
        /// Initializes a new <see cref="InvertedImageChannel"/> source.
        /// </summary>
        /// <param name="source">The source image. Must not be null.</param>
        /// <param name="channel">The color channel to read from the source image.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
        public InvertedImageChannel(Image source, ChannelComponent channel)
        {
            ArgumentNullException.ThrowIfNull(source);
            Source = source;
            Channel = channel;
        }
    }
}
