using Microsoft.UI.Xaml;

namespace HelixToolkit.Nex.WinUI;

public enum FrameworkPropertyMetadataOptions
{
    //     No options are specified; the dependency property uses the default behavior of
    //     the WPF property system.
    None = 0,

    //     The measure pass of layout compositions is affected by value changes to this
    //     dependency property.
    AffectsMeasure = 1,

    //     The arrange pass of layout composition is affected by value changes to this dependency
    //     property.
    AffectsArrange = 2,

    //     The measure pass on the parent element is affected by value changes to this dependency
    //     property.
    AffectsParentMeasure = 4,

    //     The arrange pass on the parent element is affected by value changes to this dependency
    //     property.
    AffectsParentArrange = 8,

    //     Some aspect of rendering or layout composition (other than measure or arrange)
    //     is affected by value changes to this dependency property.
    AffectsRender = 16,

    //     The values of this dependency property are inherited by child elements.
    Inherits = 32,

    //     The values of this dependency property span separated trees for purposes of property
    //     value inheritance.
    OverridesInheritanceBehavior = 64,

    //     Data binding to this dependency property is not allowed.
    NotDataBindable = 128,

    //     The System.Windows.Data.BindingMode for data bindings on this dependency property
    //     defaults to System.Windows.Data.BindingMode.TwoWay.
    BindsTwoWayByDefault = 256,

    //     The values of this dependency property should be saved or restored by journaling
    //     processes, or when navigating by Uniform resource identifiers (URIs).
    Journal = 1024,

    //     The subproperties on the value of this dependency property do not affect any
    //     aspect of rendering.
    SubPropertiesDoNotAffectRender = 2048,
}

public class FrameworkPropertyMetadata : PropertyMetadata
{
    public FrameworkPropertyMetadata(object? defaultValue)
        : base(defaultValue) { }

    public FrameworkPropertyMetadata(
        object? defaultValue,
        PropertyChangedCallback propertyChangedCallback
    )
        : base(defaultValue, propertyChangedCallback) { }

    public FrameworkPropertyMetadata(object? defaultValue, FrameworkPropertyMetadataOptions flags)
        : base(defaultValue) { }

    public FrameworkPropertyMetadata(
        object? defaultValue,
        FrameworkPropertyMetadataOptions flags,
        PropertyChangedCallback propertyChangedCallback
    )
        : base(defaultValue, propertyChangedCallback) { }
}

public static class HelixProperty
{
    public static DependencyProperty Register<TOwner, TValue>(
        string name,
        TValue defaultValue,
        bool isTwoWayBinding
    )
        where TOwner : DependencyObject
    {
        PropertyMetadata metadata;

        if (isTwoWayBinding)
        {
            metadata = new FrameworkPropertyMetadata(
                defaultValue,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault
            );
        }
        else
        {
            metadata = new FrameworkPropertyMetadata(defaultValue);
        }

        DependencyProperty property = DependencyProperty.Register(
            name,
            typeof(TValue),
            typeof(TOwner),
            metadata
        );

        return property;
    }

    public static DependencyProperty Register<TOwner, TValue>(
        string name,
        TValue defaultValue = default!,
        PropertyChangedCallback? changeCallback = null
    )
        where TOwner : DependencyObject
    {
        PropertyMetadata metadata;

        if (changeCallback is null)
        {
            metadata = new PropertyMetadata(defaultValue);
        }
        else
        {
            metadata = new PropertyMetadata(defaultValue, changeCallback);
        }

        DependencyProperty property = DependencyProperty.Register(
            name,
            typeof(TValue),
            typeof(TOwner),
            metadata
        );

        return property;
    }

    public static DependencyProperty RegisterAttached<TOwner, TValue>(
        string name,
        TValue defaultValue = default!,
        PropertyChangedCallback? changeCallback = null
    )
        where TOwner : DependencyObject
    {
        PropertyMetadata metadata;

        if (changeCallback is null)
        {
            metadata = new PropertyMetadata(defaultValue);
        }
        else
        {
            metadata = new PropertyMetadata(defaultValue, changeCallback);
        }

        DependencyProperty property = DependencyProperty.RegisterAttached(
            name,
            typeof(TValue),
            typeof(TOwner),
            metadata
        );

        return property;
    }
}
