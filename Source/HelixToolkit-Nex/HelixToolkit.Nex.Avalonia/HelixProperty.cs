using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;

namespace HelixToolkit.Nex.Avalonia;

/// <summary>
/// Argument shape mirroring the WinUI/WPF <c>DependencyPropertyChangedEventArgs</c> that the shared
/// <c>ViewportProperties.cs</c> partial class consumes through its <see cref="PropertyChangedCallback"/>
/// handlers (<c>e.OldValue</c> / <c>e.NewValue</c>).
/// </summary>
public sealed class DependencyPropertyChangedEventArgs
{
    /// <summary>The value the property held before the change.</summary>
    public object? OldValue { get; init; }

    /// <summary>The value the property holds after the change.</summary>
    public object? NewValue { get; init; }
}

/// <summary>
/// Signature-compatible replacement for the WinUI/WPF <c>PropertyChangedCallback</c> delegate used by
/// the shared partial class registrations.
/// </summary>
/// <param name="d">The object whose property changed.</param>
/// <param name="e">The old and new values.</param>
public delegate void PropertyChangedCallback(
    DependencyObject d,
    DependencyPropertyChangedEventArgs e);

/// <summary>
/// Base type the shared <c>HelixViewport</c> partial class derives from. It maps onto an Avalonia
/// <see cref="Control"/> so the control lives in the Avalonia visual tree while presenting the WPF/WinUI
/// dependency-object vocabulary the shared code expects.
/// </summary>
public abstract class DependencyObject : Control
{
}

/// <summary>
/// Metadata describing a registered property's default value and optional change callback. Mirrors the
/// WPF/WinUI <c>PropertyMetadata</c> type so the shared registration surface stays source-compatible.
/// </summary>
public class PropertyMetadata
{
    /// <summary>The default value assigned when the property is registered.</summary>
    public object? DefaultValue { get; }

    /// <summary>The change callback invoked when the property value changes, if any.</summary>
    public PropertyChangedCallback? Callback { get; }

    /// <summary>Creates metadata with a default value and no change callback.</summary>
    public PropertyMetadata(object? defaultValue)
        : this(defaultValue, null)
    {
    }

    /// <summary>Creates metadata with a default value and a change callback.</summary>
    public PropertyMetadata(object? defaultValue, PropertyChangedCallback? callback)
    {
        DefaultValue = defaultValue;
        Callback = callback;
    }
}

/// <summary>
/// Non-generic handle stored in the shared <c>static readonly DependencyProperty XxxDp</c> fields. It
/// wraps the underlying Avalonia <see cref="global::Avalonia.AvaloniaProperty"/> plus the optional
/// change callback so the adapter can bridge Avalonia's property system to the shared partial class.
/// </summary>
public sealed class DependencyProperty
{
    /// <summary>The underlying Avalonia styled property this handle wraps.</summary>
    internal global::Avalonia.AvaloniaProperty Avalonia { get; init; } = default!;

    /// <summary>The change callback associated with this property, if any.</summary>
    internal PropertyChangedCallback? Changed { get; init; }
}

/// <summary>
/// Maps Avalonia <see cref="global::Avalonia.AvaloniaProperty"/> instances back to the wrapping
/// <see cref="DependencyProperty"/> so the control base can look up and invoke the registered change
/// callback from <c>OnPropertyChanged</c>.
/// </summary>
internal static class DependencyPropertyRegistry
{
    private static readonly ConcurrentDictionary<global::Avalonia.AvaloniaProperty, DependencyProperty> Map = new();

    /// <summary>Records the mapping from an Avalonia property to its wrapping <see cref="DependencyProperty"/>.</summary>
    public static void Track(global::Avalonia.AvaloniaProperty avaloniaProperty, DependencyProperty dependencyProperty)
    {
        Map[avaloniaProperty] = dependencyProperty;
    }

    /// <summary>Attempts to resolve the wrapping <see cref="DependencyProperty"/> for an Avalonia property.</summary>
    public static bool TryGet(global::Avalonia.AvaloniaProperty avaloniaProperty, out DependencyProperty dependencyProperty)
        => Map.TryGetValue(avaloniaProperty, out dependencyProperty!);
}

/// <summary>
/// Avalonia <see cref="StyledProperty{TValue}"/>-backed adapter that exposes the same
/// <c>Register&lt;TOwner, TValue&gt;</c> surface the WinUI/WPF <c>HelixProperty</c> helpers do, so the
/// shared <c>ViewportProperties.cs</c> partial class compiles unchanged under the <c>HxAvalonia</c> symbol.
/// </summary>
public static class HelixProperty
{
    /// <summary>
    /// Registers a bindable property using Avalonia's styled-property system with the supplied default
    /// value and optional change callback. Signature-compatible with the WinUI/WPF overload the shared
    /// partial class calls.
    /// </summary>
    /// <typeparam name="TOwner">The owning control type.</typeparam>
    /// <typeparam name="TValue">The property value type.</typeparam>
    /// <param name="name">The property name.</param>
    /// <param name="defaultValue">The default value assigned when unset.</param>
    /// <param name="changeCallback">Optional callback invoked with old/new values on change.</param>
    /// <returns>A <see cref="DependencyProperty"/> handle wrapping the registered Avalonia property.</returns>
    public static DependencyProperty Register<TOwner, TValue>(
        string name,
        TValue defaultValue = default!,
        PropertyChangedCallback? changeCallback = null)
        where TOwner : DependencyObject
    {
        // AVP1001 warns that AvaloniaProperty.Register should only run in a static constructor or
        // static initializer. That constraint IS satisfied here: this adapter is always invoked from
        // the shared ViewportProperties.cs `static readonly DependencyProperty XxxDp = HelixProperty
        // .Register<...>()` field initializers, so registration happens once at type init. The
        // analyzer cannot see through this generic wrapper method, so the warning is a false positive
        // for this WPF/WinUI-compatibility shim and is suppressed at the call site.
#pragma warning disable AVP1001
        StyledProperty<TValue> avaloniaProperty =
            AvaloniaProperty.Register<TOwner, TValue>(name, defaultValue);
#pragma warning restore AVP1001

        var dependencyProperty = new DependencyProperty
        {
            Avalonia = avaloniaProperty,
            Changed = changeCallback,
        };

        DependencyPropertyRegistry.Track(avaloniaProperty, dependencyProperty);
        return dependencyProperty;
    }
}
