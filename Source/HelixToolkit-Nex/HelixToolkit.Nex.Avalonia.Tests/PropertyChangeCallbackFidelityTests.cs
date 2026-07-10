using FsCheck;
using FsCheck.Fluent;
using HelixToolkit.Nex.Interop;

namespace HelixToolkit.Nex.Avalonia.Tests;

/// <summary>
/// Feature: avalonia-interop, Property 1: Property change callback fidelity.
///
/// For any registered styled property and any pair of distinct old and new values, setting the
/// property to the new value invokes the associated change callback exactly once with
/// <c>OldValue</c> equal to the previous value and <c>NewValue</c> equal to the assigned value.
///
/// This exercises the real adapter surface end-to-end: <see cref="HelixProperty.Register{TOwner,TValue}"/>
/// registers a styled property (tracked in the internal registry) and the production
/// <c>HelixViewport.OnPropertyChanged</c> override resolves and dispatches the associated
/// <see cref="PropertyChangedCallback"/> with the old/new values.
///
/// **Validates: Requirements 2.3**
/// </summary>
[TestClass]
public sealed class PropertyChangeCallbackFidelityTests
{
    private static readonly Config FsCheckConfig = Config.Default.WithMaxTest(100);

    private readonly record struct Change(object? OldValue, object? NewValue);

    /// <summary>
    /// Records callback invocations. Tests run serially (see <c>MSTestSettings</c>), so a single
    /// static buffer is safe and lets the static <see cref="PropertyChangedCallback"/> route back.
    /// </summary>
    private static readonly List<Change> Recorded = [];

    private static void Record(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => Recorded.Add(new Change(e.OldValue, e.NewValue));

    // Register test properties once through the real adapter. Distinct names avoid collisions with
    // the shared control's own registrations while still flowing through the exact same code path.
    private static readonly DependencyProperty IntTestDp =
        HelixProperty.Register<HelixViewport, int>("__Prop1_IntTest", 0, Record);

    private static readonly DependencyProperty StringTestDp =
        HelixProperty.Register<HelixViewport, string?>("__Prop1_StringTest", null, Record);

    private static readonly DependencyProperty EnumTestDp =
        HelixProperty.Register<HelixViewport, ViewportMouseButton>(
            "__Prop1_EnumTest", ViewportMouseButton.None, Record);

    /// <summary>
    /// Drives one iteration of the property: set the property to <paramref name="oldValue"/>, clear
    /// the recorder, then assign <paramref name="newValue"/> and verify the callback fired exactly
    /// once with the correct old/new values.
    /// </summary>
    private static bool CallbackFiresExactlyOnce(DependencyProperty dp, object? oldValue, object? newValue)
    {
        var viewport = new HelixViewport();

        // Establish the "previous" value, then isolate the old -> new transition under test.
        viewport.SetValue(dp, oldValue);
        Recorded.Clear();

        viewport.SetValue(dp, newValue);

        if (Recorded.Count != 1)
        {
            return false;
        }

        Change change = Recorded[0];
        return Equals(change.OldValue, oldValue) && Equals(change.NewValue, newValue);
    }

    private static Gen<(int, int)> GenDistinctInts =>
        from a in Gen.Choose(-10_000, 10_000)
        from b in Gen.Choose(-10_000, 10_000)
        where a != b
        select (a, b);

    private static Gen<string?> GenString =>
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>("", "a", "b", "hello", "world", "helix"));

    private static Gen<(string?, string?)> GenDistinctStrings =>
        from a in GenString
        from b in GenString
        where !Equals(a, b)
        select (a, b);

    private static Gen<(ViewportMouseButton, ViewportMouseButton)> GenDistinctButtons =>
        from a in Gen.Elements(
            ViewportMouseButton.None,
            ViewportMouseButton.Left,
            ViewportMouseButton.Middle,
            ViewportMouseButton.Right)
        from b in Gen.Elements(
            ViewportMouseButton.None,
            ViewportMouseButton.Left,
            ViewportMouseButton.Middle,
            ViewportMouseButton.Right)
        where a != b
        select (a, b);

    /// <summary>
    /// Property 1 over an <see cref="int"/>-valued styled property.
    /// **Validates: Requirements 2.3**
    /// </summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    [TestCategory("Property 1")]
    public void ChangeCallback_FiresOnceWithCorrectValues_ForIntProperty()
    {
        Prop.ForAll(
                Arb.From(GenDistinctInts),
                ((int Old, int New) pair) =>
                    CallbackFiresExactlyOnce(IntTestDp, pair.Old, pair.New))
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 1 over a reference-typed (nullable <see cref="string"/>) styled property, covering
    /// null/non-null transitions.
    /// **Validates: Requirements 2.3**
    /// </summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    [TestCategory("Property 1")]
    public void ChangeCallback_FiresOnceWithCorrectValues_ForStringProperty()
    {
        Prop.ForAll(
                Arb.From(GenDistinctStrings),
                ((string? Old, string? New) pair) =>
                    CallbackFiresExactlyOnce(StringTestDp, pair.Old, pair.New))
            .Check(FsCheckConfig);
    }

    /// <summary>
    /// Property 1 over an enum-valued styled property (mirrors the control's mouse-button bindings).
    /// **Validates: Requirements 2.3**
    /// </summary>
    [TestMethod]
    [TestCategory("avalonia-interop")]
    [TestCategory("Property 1")]
    public void ChangeCallback_FiresOnceWithCorrectValues_ForEnumProperty()
    {
        Prop.ForAll(
                Arb.From(GenDistinctButtons),
                ((ViewportMouseButton Old, ViewportMouseButton New) pair) =>
                    CallbackFiresExactlyOnce(EnumTestDp, pair.Old, pair.New))
            .Check(FsCheckConfig);
    }
}
