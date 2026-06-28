using HelixToolkit.Nex.Geometries;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Shaders;

namespace HelixToolkit.Nex.glTF.Tests.Mocks;

// Feature: consolidate-gltf-test-mocks (Task 3.1)
//
// Shared, reusable test double for IGeometryManager. This consolidates the many identical
// `private sealed class StubGeometryManager` copies that were inlined across the test project
// into a single shared type. Its observable behavior is byte-for-byte equivalent to the minimal
// sentinel variant captured by the preservation oracle (MockVariantPreservationPropertyTests):
// every member returns its sentinel and Objects/GetEnumerator throw NotImplementedException.

/// <summary>
/// Minimal shared <see cref="IGeometryManager"/> stub. All members return sentinel values; the
/// pool-enumeration members (<see cref="Objects"/> and <see cref="GetEnumerator"/>) throw
/// <see cref="NotImplementedException"/>, matching the inlined copies this type replaces.
/// </summary>
internal sealed class StubGeometryManager : IGeometryManager
{
    public IReadOnlyList<Pool<GeometryResourceType, Geometry>.PoolEntry> Objects =>
        throw new NotImplementedException();

    public int Count => 0;

    public int TotalStaticIndexCount => 0;

    public Handle<GeometryResourceType> Add(Geometry geometry) => Handle<GeometryResourceType>.Null;

    public Task<(bool Success, Handle<GeometryResourceType>)> AddAsync(Geometry geometry) =>
        Task.FromResult((false, Handle<GeometryResourceType>.Null));

    public bool Remove(Geometry geometry) => false;

    public void RemoveDeferred(Geometry geometry) => Remove(geometry);

    public void ProcessPendingRemovals() { }

    public bool UploadStaticMeshIndices(ref SafeWriteContext ctx) => true;

    public void Clear() { }

    public Geometry? GetGeometryById(uint index) => null;

    public Geometry? GetGeometry(Handle<GeometryResourceType> handle) => null;

    public Pool<GeometryResourceType, Geometry>.Enumerator GetEnumerator() =>
        throw new NotImplementedException();

    public int GetDirtyCount() => 0;

    public ResultCode UploadMeshInfoDynamic(ElementBuffer<MeshInfo> buffer) => ResultCode.Ok;

    public void Dispose() { }
}
