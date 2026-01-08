using System.Numerics;

namespace HelixToolkit.Nex.Geometries.Tests;

[TestClass]
public sealed class GeometryBasics
{
    [TestMethod]
    public void TestPropertyChanged()
    {
        var geometry = new Geometry();
        bool propertyChangedRaised = false;
        geometry.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(Geometry.Vertices))
            {
                propertyChangedRaised = true;
            }
        };
        geometry.Vertices = [.. Enumerable.Repeat(new Vertex(new Vector3(1, 2, 3)), 10)];
        Assert.IsTrue(propertyChangedRaised, "PropertyChanged event was not raised for Vertices.");
    }
}
