using System.Numerics;
using glTFLoader.Schema;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Shaders;
using Gltf = glTFLoader.Schema.Gltf;

namespace HelixToolkit.Nex.glTF.Internal;

/// <summary>
/// Reason a <see cref="InstanceTransformReader.TryRead"/> attempt failed. <see cref="None"/> is used
/// only on success.
/// </summary>
internal enum InstanceReadError
{
    /// <summary>Reading succeeded; no error.</summary>
    None,

    /// <summary>The <c>TRANSLATION</c> accessor is not VEC3 with component type FLOAT (Requirement 4.3).</summary>
    TranslationType,

    /// <summary>
    /// The <c>TRANSLATION</c> accessor data cannot be read because its backing buffer is unavailable
    /// or the read would extend beyond the available accessor data (Requirement 4.4).
    /// </summary>
    TranslationData,

    /// <summary>A read <c>TRANSLATION</c> component is non-finite (NaN, +∞, −∞) (Requirement 4.5).</summary>
    TranslationNonFinite,

    /// <summary>
    /// The <c>ROTATION</c> accessor's element type is not VEC4, or its component type is neither
    /// FLOAT, normalized signed BYTE, nor normalized signed SHORT (Requirement 5.4). Used by task 4.4.
    /// </summary>
    RotationType,

    /// <summary>The <c>SCALE</c> accessor is not VEC3 with component type FLOAT (Requirement 6.3). Used by task 4.10.</summary>
    ScaleType,
}

/// <summary>
/// Reads the per-instance <c>EXT_mesh_gpu_instancing</c> accessor data into a list of
/// <see cref="InstanceTransform"/> values, applying defaults for absent attributes. Validation
/// failures return <see langword="false"/> with a populated diagnostic so the caller can skip
/// instancing and fall back to the non-instanced path.
/// </summary>
/// <remarks>
/// This task (4.1) implements <c>TRANSLATION</c> reading and defaults only. <c>ROTATION</c>
/// (task 4.4), <c>SCALE</c> (task 4.10), and full composition (task 4.14) are added at the marked
/// extension points below. Until then, each produced transform uses the identity rotation and a
/// uniform scale of <c>1.0</c> as placeholders.
/// </remarks>
internal sealed class InstanceTransformReader
{
    private readonly AccessorReader _accessorReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceTransformReader"/> class.
    /// </summary>
    /// <param name="accessorReader">The accessor reader used to resolve and read accessor data.</param>
    public InstanceTransformReader(AccessorReader accessorReader)
    {
        _accessorReader = accessorReader ?? throw new ArgumentNullException(nameof(accessorReader));
    }

    /// <summary>
    /// Reads exactly <see cref="InstancingExtensionData.InstanceCount"/> <see cref="InstanceTransform"/>
    /// values in accessor element order, applying defaults for absent attributes (translation
    /// <c>(0,0,0)</c>, identity rotation, scale <c>1.0</c>). Returns <see langword="false"/> on
    /// type/buffer/finiteness validation failure, recording exactly one Error diagnostic identifying
    /// the node and the offending accessor.
    /// </summary>
    /// <param name="model">The deserialized glTF model.</param>
    /// <param name="data">The validated extension data for the node.</param>
    /// <param name="nodeIndex">The glTF node index, used in diagnostics.</param>
    /// <param name="output">The list to populate with the produced instance transforms.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    /// <param name="error">On failure, the reason; otherwise <see cref="InstanceReadError.None"/>.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public bool TryRead(
        Gltf model,
        InstancingExtensionData data,
        int nodeIndex,
        FastList<InstanceTransform> output,
        List<ImportDiagnostic> diagnostics,
        out InstanceReadError error
    )
    {
        error = InstanceReadError.None;
        int count = data.InstanceCount;

        // --- TRANSLATION (Requirements 4.1–4.5) ---
        // When present, read InstanceCount VEC3 FLOAT elements; when absent, every instance gets
        // (0,0,0).
        Vector3[]? translations = null;
        if (
            data.Translation is int translationAccessor
            && !TryReadTranslations(
                model,
                translationAccessor,
                nodeIndex,
                count,
                diagnostics,
                out translations,
                out error
            )
        )
        {
            return false;
        }

        // --- ROTATION (Requirements 5.1–5.6) ---
        // When present, read InstanceCount VEC4 rotations (dequantizing normalized integers and
        // normalizing near-unit quaternions); when absent, every instance gets the identity
        // quaternion.
        Quaternion[]? rotations = null;
        if (
            data.Rotation is int rotationAccessor
            && !TryReadRotations(
                model,
                rotationAccessor,
                nodeIndex,
                count,
                diagnostics,
                out rotations,
                out error
            )
        )
        {
            return false;
        }

        // --- SCALE (Requirements 6.1–6.4) ---
        // When present, read InstanceCount VEC3 FLOAT elements and use the X component of each as the
        // uniform scale (emitting a per-instance Information when an element is non-uniform); when
        // absent, every instance gets a uniform scale of 1.0.
        float[]? scales = null;
        if (
            data.Scale is int scaleAccessor
            && !TryReadScales(
                model,
                scaleAccessor,
                nodeIndex,
                count,
                diagnostics,
                out scales,
                out error
            )
        )
        {
            return false;
        }

        // --- Composition (Requirement 8, completed in task 4.14) ---
        // Produce exactly InstanceCount transforms in accessor element order. Defaults from
        // InstanceTransformExts.Identity cover any absent attribute (translation (0,0,0), identity
        // rotation, scale 1.0).
        for (int i = 0; i < count; i++)
        {
            var transform = InstanceTransformExts.Identity;

            if (translations is not null)
            {
                transform = transform.SetTranslation(translations[i]);
            }

            if (rotations is not null)
            {
                transform = transform.SetRotation(rotations[i]);
            }

            if (scales is not null)
            {
                transform = transform.SetScale(scales[i]);
            }

            output.Add(transform);
        }

        return true;
    }

    /// <summary>
    /// Reads the <c>TRANSLATION</c> accessor as <paramref name="count"/> VEC3 FLOAT elements in
    /// accessor element order. Validates element/component type (Requirement 4.3), readability
    /// (Requirement 4.4), and finiteness (Requirement 4.5), recording exactly one Error diagnostic on
    /// failure.
    /// </summary>
    private bool TryReadTranslations(
        Gltf model,
        int accessorIndex,
        int nodeIndex,
        int count,
        List<ImportDiagnostic> diagnostics,
        out Vector3[]? translations,
        out InstanceReadError error
    )
    {
        translations = null;
        error = InstanceReadError.None;

        var accessor = model.Accessors![accessorIndex];

        // Requirement 4.3: element type must be VEC3 and component type must be FLOAT.
        if (
            accessor.Type != Accessor.TypeEnum.VEC3
            || accessor.ComponentType != Accessor.ComponentTypeEnum.FLOAT
        )
        {
            error = InstanceReadError.TranslationType;
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Node {nodeIndex} EXT_mesh_gpu_instancing TRANSLATION accessor {accessorIndex} "
                        + $"must be VEC3 FLOAT but is {accessor.Type} {accessor.ComponentType}. Skipping instancing.",
                    "Node",
                    nodeIndex
                )
            );
            return false;
        }

        // Requirement 4.4: the accessor data must be readable within buffer bounds.
        if (!_accessorReader.ValidateAccessor(accessorIndex, out string? validationError))
        {
            error = InstanceReadError.TranslationData;
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Node {nodeIndex} EXT_mesh_gpu_instancing TRANSLATION accessor {accessorIndex} "
                        + $"could not be read: {validationError} Skipping instancing.",
                    "Node",
                    nodeIndex
                )
            );
            return false;
        }

        var result = new Vector3[count];
        var buffer = _accessorReader.GetBuffer(accessorIndex);

        if (buffer is null)
        {
            // Accessor without a buffer view yields all-zero data per the glTF spec.
            translations = result;
            return true;
        }

        int byteOffset = _accessorReader.GetByteOffset(accessorIndex);
        int stride = _accessorReader.GetStride(accessorIndex);

        for (int i = 0; i < count; i++)
        {
            int offset = byteOffset + i * stride;
            float x = BitConverter.ToSingle(buffer, offset);
            float y = BitConverter.ToSingle(buffer, offset + 4);
            float z = BitConverter.ToSingle(buffer, offset + 8);

            // Requirement 4.5: reject any non-finite component (NaN, +∞, −∞).
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z))
            {
                error = InstanceReadError.TranslationNonFinite;
                diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Node {nodeIndex} EXT_mesh_gpu_instancing TRANSLATION accessor {accessorIndex} "
                            + $"contains a non-finite component at element {i}. Skipping instancing.",
                        "Node",
                        nodeIndex
                    )
                );
                return false;
            }

            result[i] = new Vector3(x, y, z);
        }

        translations = result;
        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>
    /// Reads the <c>SCALE</c> accessor as <paramref name="count"/> VEC3 FLOAT elements in accessor
    /// element order, using the X component of each element as the uniform scale of the corresponding
    /// instance (Requirement 6.1). Validates element/component type (Requirement 6.3) and readability
    /// (consistent with the TRANSLATION pattern), recording exactly one Error diagnostic on failure.
    /// When an element is non-uniform (the maximum absolute pairwise difference among its three
    /// components exceeds <c>1e-6</c>), the X component is still used and exactly one Information
    /// diagnostic identifying the node and that instance index is recorded (Requirement 6.4). Unlike
    /// TRANSLATION, SCALE has no non-finite rejection requirement.
    /// </summary>
    private bool TryReadScales(
        Gltf model,
        int accessorIndex,
        int nodeIndex,
        int count,
        List<ImportDiagnostic> diagnostics,
        out float[]? scales,
        out InstanceReadError error
    )
    {
        scales = null;
        error = InstanceReadError.None;

        var accessor = model.Accessors![accessorIndex];

        // Requirement 6.3: element type must be VEC3 and component type must be FLOAT.
        if (
            accessor.Type != Accessor.TypeEnum.VEC3
            || accessor.ComponentType != Accessor.ComponentTypeEnum.FLOAT
        )
        {
            error = InstanceReadError.ScaleType;
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Node {nodeIndex} EXT_mesh_gpu_instancing SCALE accessor {accessorIndex} "
                        + $"must be VEC3 FLOAT but is {accessor.Type} {accessor.ComponentType}. Skipping instancing.",
                    "Node",
                    nodeIndex
                )
            );
            return false;
        }

        // Readability validation, consistent with TRANSLATION (Requirement 4.4 pattern).
        if (!_accessorReader.ValidateAccessor(accessorIndex, out string? validationError))
        {
            error = InstanceReadError.ScaleType;
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Node {nodeIndex} EXT_mesh_gpu_instancing SCALE accessor {accessorIndex} "
                        + $"could not be read: {validationError} Skipping instancing.",
                    "Node",
                    nodeIndex
                )
            );
            return false;
        }

        var result = new float[count];
        var buffer = _accessorReader.GetBuffer(accessorIndex);

        if (buffer is null)
        {
            // Accessor without a buffer view yields all-zero data per the glTF spec.
            scales = result;
            return true;
        }

        int byteOffset = _accessorReader.GetByteOffset(accessorIndex);
        int stride = _accessorReader.GetStride(accessorIndex);

        for (int i = 0; i < count; i++)
        {
            int offset = byteOffset + i * stride;
            float x = BitConverter.ToSingle(buffer, offset);
            float y = BitConverter.ToSingle(buffer, offset + 4);
            float z = BitConverter.ToSingle(buffer, offset + 8);

            // Requirement 6.1: the uniform scale is the X component of the element.
            result[i] = x;

            // Requirement 6.4: when the element is non-uniform (max absolute pairwise difference
            // among X, Y, Z exceeds 1e-6), still use X but record a per-instance Information.
            float maxPairwiseDiff = MathF.Max(
                MathF.Abs(x - y),
                MathF.Max(MathF.Abs(y - z), MathF.Abs(x - z))
            );
            if (maxPairwiseDiff > 1e-6f)
            {
                diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Information,
                        $"Node {nodeIndex} EXT_mesh_gpu_instancing SCALE instance {i} is non-uniform "
                            + $"({x}, {y}, {z}); using the X component {x} as the uniform scale.",
                        "Node",
                        nodeIndex
                    )
                );
            }
        }

        scales = result;
        return true;
    }

    /// <summary>
    /// Reads the <c>ROTATION</c> accessor as <paramref name="count"/> VEC4 quaternion elements in
    /// accessor element order. Validates element/component type (Requirement 5.4) and readability
    /// (consistent with the TRANSLATION pattern), dequantizing normalized signed integers
    /// (Requirement 5.3), normalizing near-unit quaternions (Requirement 5.5), and substituting the
    /// identity quaternion for degenerate quaternions with a per-instance Warning (Requirement 5.6).
    /// </summary>
    private bool TryReadRotations(
        Gltf model,
        int accessorIndex,
        int nodeIndex,
        int count,
        List<ImportDiagnostic> diagnostics,
        out Quaternion[]? rotations,
        out InstanceReadError error
    )
    {
        rotations = null;
        error = InstanceReadError.None;

        var accessor = model.Accessors![accessorIndex];

        // Requirement 5.4: element type must be VEC4 and component type must be FLOAT, normalized
        // signed BYTE, or normalized signed SHORT. Normalization is required for the integer forms.
        bool isFloat = accessor.ComponentType == Accessor.ComponentTypeEnum.FLOAT;
        bool isNormalizedByte =
            accessor.ComponentType == Accessor.ComponentTypeEnum.BYTE && accessor.Normalized;
        bool isNormalizedShort =
            accessor.ComponentType == Accessor.ComponentTypeEnum.SHORT && accessor.Normalized;

        if (
            accessor.Type != Accessor.TypeEnum.VEC4
            || !(isFloat || isNormalizedByte || isNormalizedShort)
        )
        {
            error = InstanceReadError.RotationType;
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Node {nodeIndex} EXT_mesh_gpu_instancing ROTATION accessor {accessorIndex} "
                        + $"must be VEC4 FLOAT or normalized signed BYTE/SHORT but is "
                        + $"{accessor.Type} {accessor.ComponentType} (normalized={accessor.Normalized}). "
                        + "Skipping instancing.",
                    "Node",
                    nodeIndex
                )
            );
            return false;
        }

        // Readability validation, consistent with TRANSLATION (Requirement 4.4 pattern). An
        // unreadable accessor is treated as a wrong-type/data failure for rotation.
        if (!_accessorReader.ValidateAccessor(accessorIndex, out string? validationError))
        {
            error = InstanceReadError.RotationType;
            diagnostics.Add(
                new ImportDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Node {nodeIndex} EXT_mesh_gpu_instancing ROTATION accessor {accessorIndex} "
                        + $"could not be read: {validationError} Skipping instancing.",
                    "Node",
                    nodeIndex
                )
            );
            return false;
        }

        var result = new Quaternion[count];
        var buffer = _accessorReader.GetBuffer(accessorIndex);
        int byteOffset = buffer is null ? 0 : _accessorReader.GetByteOffset(accessorIndex);
        int stride = buffer is null ? 0 : _accessorReader.GetStride(accessorIndex);

        for (int i = 0; i < count; i++)
        {
            // Read the raw VEC4 components in (x, y, z, w) order, preserving component order. A
            // buffer-less accessor yields all-zero data per the glTF spec (handled as degenerate).
            float x,
                y,
                z,
                w;
            if (buffer is null)
            {
                x = y = z = w = 0f;
            }
            else
            {
                int offset = byteOffset + i * stride;
                x = ReadRotationComponent(buffer, offset, accessor.ComponentType, 0);
                y = ReadRotationComponent(buffer, offset, accessor.ComponentType, 1);
                z = ReadRotationComponent(buffer, offset, accessor.ComponentType, 2);
                w = ReadRotationComponent(buffer, offset, accessor.ComponentType, 3);
            }

            float magnitude = MathF.Sqrt(x * x + y * y + z * z + w * w);

            if (magnitude < 1e-6f)
            {
                // Requirement 5.6: degenerate quaternion → identity, plus a per-instance Warning.
                result[i] = Quaternion.Identity;
                diagnostics.Add(
                    new ImportDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"Node {nodeIndex} EXT_mesh_gpu_instancing ROTATION instance {i} has a "
                            + "degenerate (near-zero) quaternion; using the identity rotation.",
                        "Node",
                        nodeIndex
                    )
                );
            }
            else if (MathF.Abs(magnitude - 1.0f) > 1e-6f)
            {
                // Requirement 5.5: near-unit (or otherwise non-unit) quaternion → normalize.
                float inv = 1.0f / magnitude;
                result[i] = new Quaternion(x * inv, y * inv, z * inv, w * inv);
            }
            else
            {
                result[i] = new Quaternion(x, y, z, w);
            }
        }

        rotations = result;
        return true;
    }

    /// <summary>
    /// Reads a single rotation component at <paramref name="componentIndex"/> (0..3) from the element
    /// starting at <paramref name="elementOffset"/>, dequantizing normalized signed integers per the
    /// glTF convention (Requirement 5.3): signed BYTE → <c>max(v / 127, -1.0)</c>, signed SHORT →
    /// <c>max(v / 32767, -1.0)</c>, each clamped to <c>[-1.0, 1.0]</c>.
    /// </summary>
    private static float ReadRotationComponent(
        byte[] buffer,
        int elementOffset,
        Accessor.ComponentTypeEnum componentType,
        int componentIndex
    )
    {
        switch (componentType)
        {
            case Accessor.ComponentTypeEnum.FLOAT:
                return BitConverter.ToSingle(buffer, elementOffset + componentIndex * 4);

            case Accessor.ComponentTypeEnum.BYTE:
            {
                sbyte raw = unchecked((sbyte)buffer[elementOffset + componentIndex]);
                return Math.Clamp(MathF.Max(raw / 127.0f, -1.0f), -1.0f, 1.0f);
            }

            case Accessor.ComponentTypeEnum.SHORT:
            {
                short raw = BitConverter.ToInt16(buffer, elementOffset + componentIndex * 2);
                return Math.Clamp(MathF.Max(raw / 32767.0f, -1.0f), -1.0f, 1.0f);
            }

            default:
                throw new InvalidOperationException(
                    $"Unsupported ROTATION component type: {componentType}"
                );
        }
    }
}
