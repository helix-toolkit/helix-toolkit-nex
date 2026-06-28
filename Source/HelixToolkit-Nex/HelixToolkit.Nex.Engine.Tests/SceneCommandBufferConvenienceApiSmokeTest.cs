using System.Reflection;
using System.Runtime.CompilerServices;
using HelixToolkit.Nex.ECS;
using HelixToolkit.Nex.Engine.Scene;
using HelixToolkit.Nex.Lights;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Engine.Tests;

/// <summary>
/// Convenience-API smoke test (feature: engine-node-command-buffer, task 8.4).
///
/// Asserts that the Engine layer ships its custom-node recording operations purely as
/// extension methods over the public <see cref="SceneCommandBuffer.RecordCreateNode{T}"/>
/// factory API, and that the Scene-layer <see cref="SceneCommandBuffer"/> exposes no
/// Engine-specific entry point — i.e. it references no Engine custom-node type and its only
/// generic creation entry points are <c>RecordCreateNode&lt;T&gt;</c> /
/// <c>TryRecordCreateNode&lt;T&gt;</c>. This is the runtime witness for the layering
/// constraint that the Engine convenience layer adds no surface to the Scene layer.
///
/// Validates: Requirements 4.3
/// </summary>
[TestClass]
public class SceneCommandBufferConvenienceApiSmokeTest
{
    /// <summary>
    /// The Engine convenience recording methods that must exist as extension methods over the
    /// public factory API.
    /// </summary>
    private static readonly string[] EngineRecordingMethodNames =
    [
        nameof(SceneCommandBufferEngineExtensions.RecordCreateMeshNode),
        nameof(SceneCommandBufferEngineExtensions.RecordCreateLineNode),
        nameof(SceneCommandBufferEngineExtensions.RecordCreateDirectionalLight),
        nameof(SceneCommandBufferEngineExtensions.RecordCreatePointLight),
        nameof(SceneCommandBufferEngineExtensions.RecordCreateSpotLight),
        nameof(SceneCommandBufferEngineExtensions.RecordCreateBillboardNode),
        nameof(SceneCommandBufferEngineExtensions.RecordCreatePointCloudNode),
    ];

    /// <summary>
    /// The concrete Engine custom-node types. None of these may appear in any method signature
    /// of <see cref="SceneCommandBuffer"/>.
    /// </summary>
    private static readonly Type[] EngineNodeTypes =
    [
        typeof(MeshNode),
        typeof(LineNode),
        typeof(DirectionalLightNode),
        typeof(PointLightNode),
        typeof(SpotLightNode),
        typeof(BillboardNode),
        typeof(PointCloudNode),
    ];

    [TestMethod]
    public void EngineRecordingMethods_AreExtensionMethodsOverPublicFactoryApi()
    {
        // Feature: engine-node-command-buffer, task 8.4. Validates: Requirements 4.3
        var extensionsType = typeof(SceneCommandBufferEngineExtensions);

        // The convenience methods live in the Engine assembly, not the Scene assembly.
        Assert.AreEqual(
            typeof(MeshNode).Assembly,
            extensionsType.Assembly,
            "Engine convenience methods must live in the Engine assembly.");
        Assert.AreNotEqual(
            typeof(SceneCommandBuffer).Assembly,
            extensionsType.Assembly,
            "Engine convenience methods must not live in the Scene assembly.");

        // A C# extension method's containing class is itself decorated with ExtensionAttribute.
        Assert.IsTrue(
            extensionsType.IsDefined(typeof(ExtensionAttribute), inherit: false),
            $"{extensionsType.Name} should be an extension-method container.");

        foreach (var methodName in EngineRecordingMethodNames)
        {
            var overloads = extensionsType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == methodName)
                .ToArray();

            Assert.IsTrue(
                overloads.Length > 0,
                $"Expected at least one '{methodName}' convenience method.");

            foreach (var method in overloads)
            {
                // (1) static EXTENSION method
                Assert.IsTrue(method.IsStatic, $"{methodName} must be static.");
                Assert.IsTrue(
                    method.IsDefined(typeof(ExtensionAttribute), inherit: false),
                    $"{methodName} must be an extension method (ExtensionAttribute).");

                // (2) first parameter is the extended SceneCommandBuffer
                var parameters = method.GetParameters();
                Assert.IsTrue(parameters.Length >= 1, $"{methodName} must take a receiver parameter.");
                Assert.AreEqual(
                    typeof(SceneCommandBuffer),
                    parameters[0].ParameterType,
                    $"{methodName} must extend SceneCommandBuffer (first parameter).");

                // (3) returns a typed deferred handle for an Engine node subtype
                Assert.IsTrue(
                    method.ReturnType.IsGenericType
                        && method.ReturnType.GetGenericTypeDefinition() == typeof(TypedDeferredNode<>),
                    $"{methodName} must return a TypedDeferredNode<T>.");
            }
        }
    }

    [TestMethod]
    public void SceneCommandBuffer_ExposesNoEngineSpecificEntryPoint()
    {
        // Feature: engine-node-command-buffer, task 8.4. Validates: Requirements 4.3
        var scbType = typeof(SceneCommandBuffer);
        var engineAssembly = typeof(MeshNode).Assembly;

        // The Scene layer must be a different assembly than the Engine layer.
        Assert.AreNotEqual(
            engineAssembly,
            scbType.Assembly,
            "SceneCommandBuffer must not live in the Engine assembly.");

        var engineNodeTypeSet = new HashSet<Type>(EngineNodeTypes);
        var engineRecordingNameSet = new HashSet<string>(EngineRecordingMethodNames);

        var declaredMethods = scbType.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach (var method in declaredMethods)
        {
            // (a) No Engine-specific named entry point (RecordCreateMeshNode, etc.).
            Assert.IsFalse(
                engineRecordingNameSet.Contains(method.Name),
                $"SceneCommandBuffer must not declare an Engine-specific entry point '{method.Name}'.");

            // (b) No method may reference a concrete Engine node type, nor any type defined in
            // the Engine assembly, in its return or parameter types.
            AssertNotEngineType(method.ReturnType, engineNodeTypeSet, engineAssembly, method.Name, "return");
            foreach (var parameter in method.GetParameters())
            {
                AssertNotEngineType(parameter.ParameterType, engineNodeTypeSet, engineAssembly, method.Name, "parameter");
            }
        }

        // (c) The only generic creation entry points are RecordCreateNode<T> /
        // TryRecordCreateNode<T>.
        var genericCreationMethodNames = declaredMethods
            .Where(m => m.IsGenericMethodDefinition && m.Name.Contains("Create"))
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { nameof(SceneCommandBuffer.RecordCreateNode), nameof(SceneCommandBuffer.TryRecordCreateNode) }
                .OrderBy(n => n)
                .ToArray(),
            genericCreationMethodNames,
            "SceneCommandBuffer's only generic creation entry points must be RecordCreateNode<T> / TryRecordCreateNode<T>.");
    }

    [TestMethod]
    public void ConvenienceMethod_RecordsAndFlushesNode_Successfully()
    {
        // Feature: engine-node-command-buffer, task 8.4. Validates: Requirements 4.3
        // Functional smoke check: a convenience extension method records a node that flushes and
        // materializes as the expected concrete Engine subtype.
        var world = World.CreateWorld();
        try
        {
            var scb = new SceneCommandBuffer();

            // Recorded through the Engine convenience extension method (no Scene-layer entry point).
            var handle = scb.RecordCreateMeshNode("smoke-mesh");
            Assert.IsTrue(handle.IsValid, "Convenience recording should return a valid handle.");
            Assert.AreEqual(1, scb.PendingCount);

            var flush = scb.Flush(world);
            Assert.IsTrue(flush.Success, "Flush of a convenience-recorded node should succeed.");

            var retrieve = scb.TryGetMaterializedNode(handle, out var node);
            Assert.AreEqual(ResultCode.Ok, retrieve);
            Assert.IsNotNull(node);
            Assert.IsInstanceOfType(node, typeof(MeshNode));
            Assert.AreEqual("smoke-mesh", node!.Name);
        }
        finally
        {
            world.Dispose();
        }
    }

    private static void AssertNotEngineType(
        Type type,
        HashSet<Type> engineNodeTypeSet,
        Assembly engineAssembly,
        string methodName,
        string position)
    {
        // Unwrap generic arguments (e.g. Func<World, MeshNode>, TypedDeferredNode<MeshNode>) so a
        // hidden Engine type reference inside a generic cannot slip through.
        foreach (var component in EnumerateTypeComponents(type))
        {
            Assert.IsFalse(
                engineNodeTypeSet.Contains(component),
                $"SceneCommandBuffer.{methodName} {position} type references Engine node type '{component.Name}'.");

            Assert.AreNotEqual(
                engineAssembly,
                component.Assembly,
                $"SceneCommandBuffer.{methodName} {position} type '{component.Name}' is defined in the Engine assembly.");
        }
    }

    private static IEnumerable<Type> EnumerateTypeComponents(Type type)
    {
        if (type.IsGenericParameter)
        {
            yield break;
        }

        yield return type;

        if (type.HasElementType)
        {
            var element = type.GetElementType();
            if (element is not null)
            {
                foreach (var inner in EnumerateTypeComponents(element))
                {
                    yield return inner;
                }
            }
        }

        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                foreach (var inner in EnumerateTypeComponents(arg))
                {
                    yield return inner;
                }
            }
        }
    }
}
