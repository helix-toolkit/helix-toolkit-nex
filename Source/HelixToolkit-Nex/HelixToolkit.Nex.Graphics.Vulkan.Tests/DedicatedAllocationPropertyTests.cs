using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Interop.DirectX;
using Vortice.Vulkan;

namespace HelixToolkit.Nex.Tests.Vulkan;

/// <summary>
/// Feature: wpf-winui-integration, Property 2: Dedicated allocation when DedicatedOnlyBit is set
/// Validates: Requirements 1.4
/// </summary>
[TestClass]
public class DedicatedAllocationPropertyTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    /// <summary>
    /// All defined VkExternalMemoryFeatureFlags values for generating combinations.
    /// DedicatedOnly = 0x1, Exportable = 0x2, Importable = 0x4
    /// </summary>
    private static readonly VkExternalMemoryFeatureFlags[] KnownFlags =
    [
        VkExternalMemoryFeatureFlags.DedicatedOnly,
        VkExternalMemoryFeatureFlags.Exportable,
        VkExternalMemoryFeatureFlags.Importable,
    ];

    /// <summary>
    /// Generator that produces random VkExternalMemoryFeatureFlags by combining
    /// a random subset of the known flag values.
    /// </summary>
    private static Gen<VkExternalMemoryFeatureFlags> GenFeatureFlags =>
        from subset in Gen.SubListOf(KnownFlags)
        select subset.Aggregate((VkExternalMemoryFeatureFlags)0, (acc, f) => acc | f);

    /// <summary>
    /// Property 2: For any VkExternalMemoryFeatureFlags value,
    /// ShouldUseDedicatedAllocation returns true iff DedicatedOnly is set.
    /// **Validates: Requirements 1.4**
    /// </summary>
    [TestMethod]
    public void DedicatedAllocation_ReturnsTrueIffDedicatedOnlyIsSet()
    {
        Prop.ForAll(
                Arb.From(GenFeatureFlags),
                (VkExternalMemoryFeatureFlags flags) =>
                {
                    bool expected = flags.HasFlag(VkExternalMemoryFeatureFlags.DedicatedOnly);
                    bool actual = VulkanExternalMemoryImporter.ShouldUseDedicatedAllocation(flags);
                    return expected == actual;
                }
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 2 (dedicated-only set): When DedicatedOnly is present in any flag combination,
    /// dedicated allocation is always required.
    /// **Validates: Requirements 1.4**
    /// </summary>
    [TestMethod]
    public void DedicatedAllocation_AlwaysTrueWhenDedicatedOnlyIsSet()
    {
        var genWithDedicated =
            from flags in GenFeatureFlags
            select flags | VkExternalMemoryFeatureFlags.DedicatedOnly;

        Prop.ForAll(
                Arb.From(genWithDedicated),
                (VkExternalMemoryFeatureFlags flags) =>
                    VulkanExternalMemoryImporter.ShouldUseDedicatedAllocation(flags)
            )
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 2 (dedicated-only cleared): When DedicatedOnly is absent,
    /// dedicated allocation is never required.
    /// **Validates: Requirements 1.4**
    /// </summary>
    [TestMethod]
    public void DedicatedAllocation_AlwaysFalseWhenDedicatedOnlyIsCleared()
    {
        var genWithoutDedicated =
            from flags in GenFeatureFlags
            select flags & ~VkExternalMemoryFeatureFlags.DedicatedOnly;

        Prop.ForAll(
                Arb.From(genWithoutDedicated),
                (VkExternalMemoryFeatureFlags flags) =>
                    !VulkanExternalMemoryImporter.ShouldUseDedicatedAllocation(flags)
            )
            .Check(FsCheckConfig);
    }
}
