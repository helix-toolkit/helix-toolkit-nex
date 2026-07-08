using HelixToolkit.Nex.Rendering.Components;

namespace HelixToolkit.Nex.Rendering;

/// <summary>
/// Factory helpers for image/icon billboards: camera-facing textured quads that draw a plain
/// (non-SDF) texture. These use the default billboard material (material id 0), which samples a
/// bindless texture and multiplies it by the billboard color, so they render an ordinary sprite
/// rather than SDF text. This is the image counterpart to
/// <see cref="TextLayoutHelper.CreateTextBillboard"/>.
/// </summary>
public static class BillboardHelper
{
    /// <summary>A UV rectangle covering the entire texture: (u0, v0, u1, v1) = (0, 0, 1, 1).</summary>
    private static readonly Vector4 FullTextureUv = new(0f, 0f, 1f, 1f);

    /// <summary>
    /// Builds a single-quad <see cref="BillboardGeometry"/> for an image billboard, positioning the
    /// quad so the entity's world position maps to the requested <paramref name="anchor"/> within the
    /// quad (matching the anchoring convention used by <see cref="TextLayoutHelper"/>).
    /// </summary>
    /// <param name="width">The quad width (world-space units, or screen pixels when the owning billboard is fixed-size).</param>
    /// <param name="height">The quad height (world-space units, or screen pixels when the owning billboard is fixed-size).</param>
    /// <param name="anchor">The anchor point within the quad that maps to the entity's world position.</param>
    /// <param name="uvRect">The UV rectangle to sample from the texture. Defaults to the full texture.</param>
    /// <param name="color">
    /// Optional per-billboard color. When <see langword="null"/>, the quad uses the owning
    /// <see cref="BillboardDrawInfo.Color"/> uniform tint instead.
    /// </param>
    /// <returns>A <see cref="BillboardGeometry"/> containing one image quad.</returns>
    public static BillboardGeometry CreateImageGeometry(
        float width,
        float height,
        BillboardAnchor anchor = BillboardAnchor.Center,
        Vector4? uvRect = null,
        Color4? color = null
    )
    {
        var geo = new BillboardGeometry();
        Vector3 center = ComputeAnchoredCenter(width, height, anchor);
        Vector4 uv = uvRect ?? FullTextureUv;

        if (color.HasValue)
        {
            geo.Add(center, width, height, uv, color.Value);
        }
        else
        {
            // No per-billboard color: signals the compute shader to use the component's uniform color.
            geo.Add(center, width, height, uv);
        }

        return geo;
    }

    /// <summary>
    /// Creates a <see cref="BillboardDrawInfo"/> that draws a single textured image/icon quad using
    /// the default (textured) billboard material.
    /// </summary>
    /// <param name="texture">The bindless texture to sample (e.g. an icon loaded via the texture repository).</param>
    /// <param name="sampler">The bindless sampler used to sample <paramref name="texture"/>.</param>
    /// <param name="width">The quad width. Interpreted as world-space units, or as screen pixels when <paramref name="fixedSize"/> is <see langword="true"/>.</param>
    /// <param name="height">The quad height. Interpreted as world-space units, or as screen pixels when <paramref name="fixedSize"/> is <see langword="true"/>.</param>
    /// <param name="tint">The color multiplied with the sampled texture. Defaults to opaque white (no tint).</param>
    /// <param name="anchor">The anchor point within the quad that maps to the entity's world position. Defaults to <see cref="BillboardAnchor.Center"/>.</param>
    /// <param name="uvRect">The UV rectangle to sample. Defaults to the full texture (0,0,1,1).</param>
    /// <param name="fixedSize">When <see langword="true"/>, the quad keeps a constant screen-space pixel size (typical for editor icons).</param>
    /// <param name="cullDistance">The distance beyond which the billboard is culled (0 disables distance culling).</param>
    /// <param name="hitable">Whether the billboard can be selected via GPU picking. Defaults to <see langword="true"/>.</param>
    /// <param name="materialName">
    /// The billboard material name. Defaults to <see langword="null"/>, which selects the built-in
    /// default textured material. Supply a custom registered material name to override shading/blending.
    /// </param>
    /// <returns>A fully-configured <see cref="BillboardDrawInfo"/> for an image billboard.</returns>
    public static BillboardDrawInfo CreateImageBillboard(
        TextureRef texture,
        SamplerRef sampler,
        float width,
        float height,
        Color4? tint = null,
        BillboardAnchor anchor = BillboardAnchor.Center,
        Vector4? uvRect = null,
        bool fixedSize = false,
        float cullDistance = 0,
        bool hitable = true,
        string? materialName = null
    )
    {
        BillboardGeometry geo = CreateImageGeometry(width, height, anchor, uvRect);

        return new BillboardDrawInfo
        {
            BillboardGeometry = geo,
            Color = tint ?? new Color4(1f, 1f, 1f, 1f),
            Texture = texture,
            Sampler = sampler,
            // Null material name falls back to the default textured billboard material (id 0).
            BillboardMaterialName = materialName,
            Hitable = hitable,
            FixedSize = fixedSize,
            Anchor = anchor,
            CullDistance = cullDistance,
        };
    }

    /// <summary>
    /// Computes the center position of an anchored quad so the entity's world position maps to the
    /// requested <paramref name="anchor"/>. Mirrors the anchoring behavior of
    /// <see cref="TextLayoutHelper"/> for a single quad of the given dimensions.
    /// </summary>
    private static Vector3 ComputeAnchoredCenter(float width, float height, BillboardAnchor anchor)
    {
        float x = anchor switch
        {
            BillboardAnchor.BottomLeft or BillboardAnchor.CenterLeft or BillboardAnchor.TopLeft =>
                width * 0.5f,
            BillboardAnchor.BottomRight
            or BillboardAnchor.CenterRight
            or BillboardAnchor.TopRight => -width * 0.5f,
            _ => 0f, // horizontally centered anchors
        };

        float y = anchor switch
        {
            BillboardAnchor.BottomLeft
            or BillboardAnchor.BottomCenter
            or BillboardAnchor.BottomRight => height * 0.5f,
            BillboardAnchor.TopLeft or BillboardAnchor.TopCenter or BillboardAnchor.TopRight =>
                -height * 0.5f,
            _ => 0f, // vertically centered anchors
        };

        return new Vector3(x, y, 0f);
    }
}
