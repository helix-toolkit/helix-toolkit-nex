namespace HelixToolkit.Nex.Material.Tests;

[TestClass]
public sealed class MaterialProperties
{
    [TestInitialize]
    public void TestInit()
    {
        // This method is called before each test method.
    }

    [TestCleanup]
    public void TestCleanup()
    {
        // This method is called after each test method.
    }

    [TestMethod]
    public void TestPropertyChanged()
    {
        var material = new UnlitMaterialProperties();
        bool propertyChangedRaised = false;
        material.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(UnlitMaterialProperties.Albedo))
            {
                propertyChangedRaised = true;
            }
        };
        material.Albedo = new Color4(1, 0, 0, 1);
        Assert.IsTrue(
            propertyChangedRaised,
            "PropertyChanged event was not raised for DiffuseColor."
        );
    }

    [TestMethod]
    public void TestPropertyDefaultValue()
    {
        var material = new UnlitMaterialProperties();
        Assert.AreEqual(
            new Color4(1, 1, 1, 1),
            material.Albedo,
            "Default value of Albedo property is incorrect."
        );
        Assert.AreEqual(
            TextureResource.Null,
            material.AlbedoTexture,
            "Default value of AlbedoTexture property is incorrect."
        );
    }
}
