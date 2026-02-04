using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HelixToolkit.Nex;

internal static class StringHelper
{
    public const string EmptyStr = "";
}

/// <summary>
/// Attribute to mark fields for automatic conversion to observable properties with INotifyPropertyChanged pattern.
/// </summary>
/// <remarks>
/// When applied to a field, this attribute causes the source generator to create a public property
/// that implements the INotifyPropertyChanged pattern using the Set method from ObservableObject.
///
/// Example:
/// <code>
/// [Observable(Default = "Vector4.One")]
/// private Vector4 _albedo;
/// </code>
///
/// Will generate:
/// <code>
/// public Vector4 Albedo
/// {
///     get => _albedo;
///     set { Set(ref _albedo, value); }
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class ObservableAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the default value expression for the field initialization.
    /// </summary>
    /// <remarks>
    /// This should be a valid C# expression that will be used to initialize the field.
    /// For example: "Vector4.One", "0", "null", etc.
    /// </remarks>
    public string? Default { get; set; }
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public static EventBus EventBus => EventBus.Instance;
    private bool _disablePropertyChangedEvent = false;
    public bool DisablePropertyChangedEvent
    {
        set
        {
            if (_disablePropertyChangedEvent == value)
            {
                return;
            }
            _disablePropertyChangedEvent = value;
            RaisePropertyChanged();
        }
        get { return _disablePropertyChangedEvent; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged(
        [CallerMemberName] string propertyName = StringHelper.EmptyStr
    )
    {
        if (!DisablePropertyChangedEvent)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected void RaisePropertyChanged(PropertyChangedEventArgs args)
    {
        if (!DisablePropertyChangedEvent)
            PropertyChanged?.Invoke(this, args);
    }

    protected bool Set<T>(
        ref T backingField,
        T value,
        [CallerMemberName] string propertyName = StringHelper.EmptyStr
    )
    {
        if (EqualityComparer<T>.Default.Equals(backingField, value))
        {
            return false;
        }

        backingField = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    protected bool Set<T>(
        ref T backingField,
        T value,
        bool raisePropertyChanged,
        [CallerMemberName] string propertyName = StringHelper.EmptyStr
    )
    {
        if (EqualityComparer<T>.Default.Equals(backingField, value))
        {
            return false;
        }

        backingField = value;
        if (raisePropertyChanged)
        {
            RaisePropertyChanged(propertyName);
        }
        return true;
    }
}
