using System.Linq;
using System.Reflection;
using HelixToolkit.Nex.Scene;

namespace HelixToolkit.Nex.Scene.Tests;

/// <summary>
/// Architectural layering smoke test (feature: engine-node-command-buffer, task 8.3).
///
/// Requirement 4.2: THE Scene_Layer SHALL contain no compile-time or runtime reference to the
/// Engine_Layer, verifiable by dependency-graph inspection producing no edge from Scene_Layer
/// to Engine_Layer.
///
/// This test inspects the <c>HelixToolkit.Nex.Scene</c> assembly (the assembly that contains
/// <see cref="SceneCommandBuffer"/>) and asserts that none of its referenced assemblies is the
/// Engine assembly (<c>HelixToolkit.Nex.Engine</c> — the assembly that contains
/// <c>MeshNode</c>).
///
/// IMPORTANT: This test project deliberately does NOT take a reference on the Engine assembly.
/// Doing so would defeat the purpose of the check (and the Engine assembly may not even be
/// loaded). The Engine assembly is therefore identified by its simple name only.
/// </summary>
[TestClass]
public class SceneLayerLayeringTest
{
    /// <summary>Simple (non-fully-qualified) name of the Scene assembly under test.</summary>
    private const string SceneAssemblyName = "HelixToolkit.Nex.Scene";

    /// <summary>Simple (non-fully-qualified) name of the Engine assembly that must not be referenced.</summary>
    private const string EngineAssemblyName = "HelixToolkit.Nex.Engine";

    [TestMethod]
    public void SceneAssembly_HasExpectedSimpleName()
    {
        // Feature: engine-node-command-buffer, task 8.3
        // Sanity-check that the assembly containing SceneCommandBuffer is the Scene assembly.
        // Validates: Requirements 4.2
        var sceneAssembly = typeof(SceneCommandBuffer).Assembly;
        var simpleName = sceneAssembly.GetName().Name;

        Assert.AreEqual(SceneAssemblyName, simpleName,
            "The assembly containing SceneCommandBuffer must be the Scene assembly.");
    }

    [TestMethod]
    public void SceneAssembly_DoesNotReferenceEngineAssembly()
    {
        // Feature: engine-node-command-buffer, task 8.3
        // The Scene assembly must have no referenced-assembly edge to the Engine assembly.
        // Identify the Engine assembly by simple name so this project never needs to reference it.
        // Validates: Requirements 4.2
        var sceneAssembly = typeof(SceneCommandBuffer).Assembly;

        AssemblyName[] referenced = sceneAssembly.GetReferencedAssemblies();

        bool referencesEngine = referenced.Any(
            r => string.Equals(r.Name, EngineAssemblyName, System.StringComparison.Ordinal));

        Assert.IsFalse(referencesEngine,
            $"The Scene assembly '{SceneAssemblyName}' must not reference the Engine assembly " +
            $"'{EngineAssemblyName}'. Referenced assemblies: " +
            string.Join(", ", referenced.Select(r => r.Name)));
    }
}
