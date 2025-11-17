using HelixToolkit.Nex.Rendering;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelixToolkit.Nex.Tests.Rendering;

[TestClass]
public sealed class RenderManagerTests
{
    // Simple fake renderer used for tests.
    private sealed class FakeRenderer : Renderer
    {
        public override RenderStages Stage { get; }
        public override string Name { get; }
        public int RenderCount { get; private set; }
        public RendererManager? AttachedManager { get; private set; }

        public FakeRenderer(RenderStages stage, string name)
        {
            Stage = stage;
            Name = name;
        }

        protected override void OnAttach(RendererManager manager)
        {
            AttachedManager = manager;
        }

        protected override void OnDetach()
        {
            AttachedManager = null;
        }

        protected override void OnRender(RenderContext context)
        {
            RenderCount++;
        }
    }

    [TestMethod]
    public void CreateAndDestroy()
    {
        var manager = new RendererManager();

        var rBegin = new FakeRenderer(RenderStages.Begin, "BeginRenderer");
        var rOpaque = new FakeRenderer(RenderStages.Opaque, "OpaqueRenderer");
        var rUI = new FakeRenderer(RenderStages.UI, "UIRenderer");

        // Add renderers
        Assert.IsTrue(manager.AddRenderer(rBegin));
        Assert.IsTrue(manager.AddRenderer(rOpaque));
        Assert.IsTrue(manager.AddRenderer(rUI));

        // Duplicate add should return false
        Assert.IsFalse(manager.AddRenderer(rOpaque));

        var ctx = new RenderContext(null!);

        // First render - all enabled by default
        manager.Render(ctx);
        Assert.AreEqual(1, rBegin.RenderCount);
        Assert.AreEqual(1, rOpaque.RenderCount);
        Assert.AreEqual(1, rUI.RenderCount);

        // Disable one renderer - it should not be called
        rOpaque.Enabled = false;
        manager.Render(ctx);
        Assert.AreEqual(2, rBegin.RenderCount);
        Assert.AreEqual(1, rOpaque.RenderCount); // unchanged
        Assert.AreEqual(2, rUI.RenderCount);

        // Remove a renderer and ensure it no longer receives calls and is detached
        manager.RemoveRenderer(rBegin);
        Assert.IsFalse(rBegin.IsAttached);
        manager.Render(ctx);
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
