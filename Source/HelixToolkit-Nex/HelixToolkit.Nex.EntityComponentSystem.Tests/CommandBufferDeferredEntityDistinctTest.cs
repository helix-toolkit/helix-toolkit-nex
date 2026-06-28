using FsCheck;
using FsCheck.Fluent;

namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class CommandBufferDeferredEntityDistinctTest
{
    [TestMethod]
    public void Property1_DeferredEntities_AreDistinctAndValid()
    {
        // Feature: ecs-command-buffer, Property 1: Deferred entities are distinct and valid
        // For any sequence of N entity-creation recordings into a single command buffer, the
        // N returned DeferredEntity handles are each valid and pairwise distinct.
        // Validates: Requirements 1.2, 1.3
        Prop.ForAll(
                Arb.From(Gen.Choose(0, 200)),
                (int n) =>
                {
                    var cb = new CommandBuffer();
                    var handles = new DeferredEntity[n];

                    for (var i = 0; i < n; i++)
                    {
                        handles[i] = cb.RecordCreateEntity();
                    }

                    // Every returned handle must be valid.
                    for (var i = 0; i < n; i++)
                    {
                        if (!handles[i].IsValid)
                        {
                            return false;
                        }
                    }

                    // All handles must be pairwise distinct.
                    var seen = new HashSet<DeferredEntity>();
                    for (var i = 0; i < n; i++)
                    {
                        if (!seen.Add(handles[i]))
                        {
                            return false;
                        }
                    }

                    return seen.Count == n;
                }
            )
            .QuickCheckThrowOnFailure();
    }
}
