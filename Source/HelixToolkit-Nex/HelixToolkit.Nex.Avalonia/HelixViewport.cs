using Avalonia;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Avalonia host control for the Vulkan-native HelixToolkit.Nex engine. This file hosts the base
/// partial declaration and the dependency-property bridging that lets the shared
/// <c>ViewportCommon.cs</c> / <c>ViewportProperties.cs</c> partial class (compiled under the
/// <c>HxAvalonia</c> symbol) map onto Avalonia's styled-property system.
/// </summary>
/// <remarks>
/// The shared partial class registers properties through <see cref="HelixProperty"/> and accesses
/// their values through the instance <see cref="GetValue(DependencyProperty)"/> /
/// <see cref="SetValue(DependencyProperty, object?)"/> members declared here. Change notification is
/// bridged by overriding <see cref="OnPropertyChanged(AvaloniaPropertyChangedEventArgs)"/>, which
/// resolves the registered <see cref="PropertyChangedCallback"/> and invokes it with the old and new
/// values.
/// </remarks>
public partial class HelixViewport : DependencyObject
{
    /// <summary>
    /// Reads the current value of a registered <see cref="DependencyProperty"/> by delegating to the
    /// underlying Avalonia styled-property system. Mirrors the WPF/WinUI <c>GetValue</c> surface the
    /// shared partial class expects.
    /// </summary>
    /// <param name="dp">The dependency-property handle wrapping an Avalonia property.</param>
    /// <returns>The current value stored for the property, or its default when unset.</returns>
    public object? GetValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return base.GetValue(dp.Avalonia);
    }

    /// <summary>
    /// Assigns a value to a registered <see cref="DependencyProperty"/> by delegating to the underlying
    /// Avalonia styled-property system. Mirrors the WPF/WinUI <c>SetValue</c> surface the shared partial
    /// class expects.
    /// </summary>
    /// <param name="dp">The dependency-property handle wrapping an Avalonia property.</param>
    /// <param name="value">The value to assign.</param>
    public void SetValue(DependencyProperty dp, object? value)
    {
        ArgumentNullException.ThrowIfNull(dp);
        base.SetValue(dp.Avalonia, value);
    }

    /// <summary>
    /// Bridges Avalonia's property-change notification to the shared partial class by resolving the
    /// wrapping <see cref="DependencyProperty"/> for the changed Avalonia property and invoking its
    /// registered <see cref="PropertyChangedCallback"/> with the old and new values.
    /// </summary>
    /// <param name="change">The Avalonia property-change notification.</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (DependencyPropertyRegistry.TryGet(change.Property, out DependencyProperty dp)
            && dp.Changed is not null)
        {
            dp.Changed(this, new DependencyPropertyChangedEventArgs
            {
                OldValue = change.OldValue,
                NewValue = change.NewValue,
            });
        }
    }
}
