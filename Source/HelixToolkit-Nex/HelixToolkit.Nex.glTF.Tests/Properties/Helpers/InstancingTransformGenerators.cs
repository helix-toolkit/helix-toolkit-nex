using System.Numerics;
using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Shaders;

namespace HelixToolkit.Nex.glTF.Tests.Properties.Helpers;

/// <summary>
/// FsCheck generators producing node-level transforms (TRS components or an equivalent 16-element
/// column-major matrix) and per-instance <see cref="InstanceTransform"/> values for the placement
/// composition property (Property 21): the effective placement of an instance equals the node's
/// resolved world transform composed with the instance transform.
/// </summary>
internal static class InstancingTransformGenerators
{
    /// <summary>The default translation magnitude bound.</summary>
    public const float TranslationRange = 1_000f;

    /// <summary>The default (positive) scale bound; scales stay positive to keep matrices decomposable.</summary>
    public const float MaxScale = 100f;

    /// <summary>
    /// A node transform expressed as TRS, plus the equivalent <see cref="Matrix4x4"/> so a test can
    /// drive either the TRS branch or the explicit-matrix branch of node transform resolution.
    /// </summary>
    /// <param name="Translation">The node translation.</param>
    /// <param name="Rotation">The node rotation as a unit quaternion.</param>
    /// <param name="Scale">The node uniform-or-nonuniform scale.</param>
    public readonly record struct NodeTransform(Vector3 Translation, Quaternion Rotation, Vector3 Scale)
    {
        /// <summary>Gets the row-major <see cref="Matrix4x4"/> equivalent of this TRS transform.</summary>
        public Matrix4x4 ToMatrix() =>
            Matrix4x4.CreateScale(Scale)
            * Matrix4x4.CreateFromQuaternion(Rotation)
            * Matrix4x4.CreateTranslation(Translation);

        /// <summary>
        /// Gets the 16-element column-major float array as a glTF node would store its <c>matrix</c>.
        /// </summary>
        public float[] ToGltfMatrixArray()
        {
            var m = ToMatrix();
            // glTF stores matrices column-major; System.Numerics is row-major, so transpose.
            return
            [
                m.M11, m.M21, m.M31, m.M41,
                m.M12, m.M22, m.M32, m.M42,
                m.M13, m.M23, m.M33, m.M43,
                m.M14, m.M24, m.M34, m.M44,
            ];
        }
    }

    /// <summary>Generates a finite translation vector within <see cref="TranslationRange"/>.</summary>
    public static Gen<Vector3> Translation() =>
        InstancingAccessorDataGenerators.Vector3Gen(TranslationRange);

    /// <summary>Generates a positive, finite scale vector in <c>(0, MaxScale]</c> on each axis.</summary>
    public static Gen<Vector3> Scale() =>
        from x in PositiveScale()
        from y in PositiveScale()
        from z in PositiveScale()
        select new Vector3(x, y, z);

    /// <summary>Generates a uniform (equal on all axes) positive scale vector.</summary>
    public static Gen<Vector3> UniformScale() => PositiveScale().Select(s => new Vector3(s));

    /// <summary>Generates a unit-quaternion rotation.</summary>
    public static Gen<Quaternion> Rotation() =>
        InstancingQuaternionGenerators.UnitQuaternion().Select(v => new Quaternion(v.X, v.Y, v.Z, v.W));

    /// <summary>
    /// Generates a node transform from random TRS components (rotation is a unit quaternion, scale is
    /// positive so the matrix remains cleanly decomposable).
    /// </summary>
    public static Gen<NodeTransform> NodeTrs() =>
        from translation in Translation()
        from rotation in Rotation()
        from scale in Scale()
        select new NodeTransform(translation, rotation, scale);

    /// <summary>
    /// Generates a single per-instance <see cref="InstanceTransform"/> with a finite translation, a
    /// unit-quaternion rotation, and a positive uniform scale.
    /// </summary>
    public static Gen<InstanceTransform> InstanceTransformGen() =>
        from translation in Translation()
        from rotation in InstancingQuaternionGenerators.UnitQuaternion()
        from scale in PositiveScale()
        select new InstanceTransform
        {
            Translation = translation,
            Quaternion = rotation,
            Scale = scale,
        };

    /// <summary>Generates an array of <paramref name="count"/> per-instance transforms.</summary>
    /// <param name="count">The number of instance transforms (must be at least 1).</param>
    public static Gen<InstanceTransform[]> InstanceTransforms(int count) =>
        Gen.ArrayOf(InstanceTransformGen(), Math.Max(1, count));

    /// <summary>
    /// Generates a node transform paired with a set of per-instance transforms, the joint input for
    /// the placement composition property (Property 21).
    /// </summary>
    /// <param name="instanceCount">The number of instances to generate (must be at least 1).</param>
    public static Gen<(NodeTransform Node, InstanceTransform[] Instances)> NodeWithInstances(
        int instanceCount
    ) =>
        from node in NodeTrs()
        from instances in InstanceTransforms(instanceCount)
        select (node, instances);

    private static Gen<float> PositiveScale() =>
        Gen.Choose(1, 100_000).Select(v => v / 100_000f * MaxScale);
}
