using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Material;
using HelixToolkit.Nex.Shaders;

namespace HelixToolkit.Nex.glTF.Tests.Mocks;

// Feature: consolidate-gltf-test-mocks (Task 3.1)
//
// Shared, reusable test double for IPBRMaterialPropertyManager. This consolidates the identical
// `private sealed class StubMaterialPropertyManager` copies that were inlined across the test
// project into a single shared type. Every copy wrapped a real PBRMaterialPropertyManager and
// delegated to it (with the two UploadDynamic overloads returning ResultCode.Ok), so this single
// body covers all current call sites. Its observable behavior is preserved exactly.

/// <summary>
/// Shared <see cref="IPBRMaterialPropertyManager"/> stub that delegates to a real
/// <see cref="PBRMaterialPropertyManager"/> instance. The <c>UploadDynamic</c> overloads return
/// <see cref="ResultCode.Ok"/> (no GPU upload), and <see cref="Dispose"/> disposes the inner
/// manager, matching the inlined copies this type replaces.
/// </summary>
internal sealed class StubMaterialPropertyManager : IPBRMaterialPropertyManager
{
    private readonly PBRMaterialPropertyManager _inner = new();

    public int Count => _inner.Count;

    public PBRMaterialProperties Create(string materialName) => _inner.Create(materialName);

    public PBRMaterialProperties Create(string materialName, ref PBRProperties properties) =>
        _inner.Create(materialName, ref properties);

    public void Clear() => _inner.Clear();

    public IReadOnlyList<Pool<MaterialPropertyResource, PBRProperties>.PoolEntry> Objects =>
        _inner.Objects;

    public ref PBRProperties At(int index) => ref _inner.At(index);

    public ResultCode UploadDynamic(ElementBuffer<PBRProperties> buffer) => ResultCode.Ok;

    public ResultCode UploadDynamic(
        ElementBuffer<PBRProperties> buffer,
        IEnumerable<uint> indices
    ) => ResultCode.Ok;

    public void Dispose() => _inner.Dispose();
}
