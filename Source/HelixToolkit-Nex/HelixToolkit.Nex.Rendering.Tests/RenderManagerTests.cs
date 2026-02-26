using HelixToolkit.Nex.DependencyInjection;
using HelixToolkit.Nex.Graphics;
using HelixToolkit.Nex.Graphics.Mock;
using HelixToolkit.Nex.Rendering;
using HelixToolkit.Nex.Repository;

namespace HelixToolkit.Nex.Tests.Rendering;

[TestClass]
public sealed class RenderManagerTests
{
    // Simple fake renderer used for tests.
    private sealed class FakeRenderer : RenderNode
    {
        public override RenderStages Stage { get; }
        public override string Name { get; }
        public int RenderCount { get; private set; }

        public FakeRenderer(RenderStages stage, string name)
        {
            Stage = stage;
            Name = name;
        }

        protected override bool OnSetup()
        {
            return true;
        }

        protected override void OnTeardown() { }

        protected override void OnRender(RenderContext context, ICommandBuffer cmdBuf)
        {
            RenderCount++;
        }
    }

    private static IServiceProvider? _serviceProvider;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        var services = new ServiceCollection
        {
            new ServiceDescriptor(typeof(IContext), typeof(MockContext), ServiceLifetime.Singleton),
            new ServiceDescriptor(
                typeof(IShaderRepository),
                typeof(ShaderRepository),
                ServiceLifetime.Singleton
            ),
        };
        // Register any necessary services here if needed for renderers
        _serviceProvider = services.BuildServiceProvider();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    [TestMethod]
    public void CreateAndDestroy()
    {
        var manager = new Renderer(_serviceProvider!);
        var context = _serviceProvider!.GetRequiredService<IContext>();
        var rBegin = new FakeRenderer(RenderStages.Begin, "BeginRenderer");
        var rOpaque = new FakeRenderer(RenderStages.Opaque, "OpaqueRenderer");
        var rUI = new FakeRenderer(RenderStages.UI, "UIRenderer");

        // AddPass renderers
        Assert.IsTrue(manager.AddNode(rBegin));
        Assert.IsTrue(manager.AddNode(rOpaque));
        Assert.IsTrue(manager.AddNode(rUI));

        // Duplicate add should return false
        Assert.IsFalse(manager.AddNode(rOpaque));

        var ctx = new RenderContext(_serviceProvider!);
        var cmdBuf = context.AcquireCommandBuffer();

        // First render - all enabled by default
        manager.Render(ctx, cmdBuf);
        Assert.AreEqual(1, rBegin.RenderCount);
        Assert.AreEqual(1, rOpaque.RenderCount);
        Assert.AreEqual(1, rUI.RenderCount);

        // Disable one renderer - it should not be called
        rOpaque.Enabled = false;
        manager.Render(ctx, cmdBuf);
        Assert.AreEqual(2, rBegin.RenderCount);
        Assert.AreEqual(1, rOpaque.RenderCount); // unchanged
        Assert.AreEqual(2, rUI.RenderCount);

        // RemovePass a renderer and ensure it no longer receives calls and is detached
        manager.RemoveNode(rBegin);
        Assert.IsFalse(rBegin.IsAttached);
        manager.Render(ctx, cmdBuf);
        Assert.AreEqual(2, rBegin.RenderCount); // not called after removal
        Assert.AreEqual(1, rOpaque.RenderCount); // still disabled
        Assert.AreEqual(3, rUI.RenderCount);

        // Clear should detach remaining renderers
        manager.Clear();
        Assert.IsFalse(rOpaque.IsAttached);
        Assert.IsFalse(rUI.IsAttached);

        // Dispose the manager
        manager.Dispose();
        Assert.IsTrue(manager.IsDisposed);
    }
}
