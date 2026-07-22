namespace HelixToolkit.Nex.WinUI.Tests;

/// <summary>
/// Tests for the WinUI viewport's platform-independent lifecycle coordinator.
/// </summary>
[TestClass]
public sealed class ViewportLifecycleTests
{
    /// <summary>
    /// Verifies that a new lifecycle starts unloaded and owns no session.
    /// </summary>
    [TestMethod]
    public void Constructor_StartsUnloaded()
    {
        var lifecycle = CreateLifecycle();

        Assert.AreEqual(ViewportLifecycleState.Unloaded, lifecycle.State);
        Assert.IsNull(lifecycle.Session);
    }

    /// <summary>
    /// Verifies that unloading disposes the current session and a subsequent load
    /// creates a new one.
    /// </summary>
    [TestMethod]
    public void LoadUnloadLoad_RecreatesSession()
    {
        var activated = new List<TestSession?>();
        var deactivationCount = 0;
        var terminalDisposeCount = 0;
        var lifecycle = new ViewportLifecycle<TestSession>(
            activated.Add,
            () => deactivationCount++,
            () => terminalDisposeCount++
        );
        var first = new TestSession();
        var second = new TestSession();

        lifecycle.Load(() =>
        {
            Assert.AreEqual(ViewportLifecycleState.Loading, lifecycle.State);
            return first;
        });
        lifecycle.Unload();
        lifecycle.Load(() => second);

        Assert.AreEqual(ViewportLifecycleState.Loaded, lifecycle.State);
        Assert.AreSame(second, lifecycle.Session);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.AreEqual(0, second.DisposeCount);
        Assert.AreEqual(1, deactivationCount);
        Assert.AreEqual(0, terminalDisposeCount);
        CollectionAssert.AreEqual(
            new[] { first, second },
            activated
        );
    }

    /// <summary>
    /// Verifies that duplicate load and unload notifications do not repeat lifecycle work.
    /// </summary>
    [TestMethod]
    public void DuplicateLoadAndUnload_AreIgnored()
    {
        var creationCount = 0;
        var deactivationCount = 0;
        var session = new TestSession();
        var lifecycle = new ViewportLifecycle<TestSession>(
            _ => { },
            () => deactivationCount++,
            () => { }
        );

        lifecycle.Load(() =>
        {
            creationCount++;
            return session;
        });
        lifecycle.Load(() =>
        {
            creationCount++;
            return new TestSession();
        });
        lifecycle.Unload();
        lifecycle.Unload();

        Assert.AreEqual(ViewportLifecycleState.Unloaded, lifecycle.State);
        Assert.AreEqual(1, creationCount);
        Assert.AreEqual(1, deactivationCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    /// <summary>
    /// Verifies that a loaded lifecycle without a session can unload and later acquire one.
    /// </summary>
    [TestMethod]
    public void LoadWithoutSession_CanUnloadAndReload()
    {
        var session = new TestSession();
        var lifecycle = CreateLifecycle();

        lifecycle.Load(() => null);
        Assert.AreEqual(ViewportLifecycleState.Loaded, lifecycle.State);
        Assert.IsNull(lifecycle.Session);

        lifecycle.Unload();
        lifecycle.Load(() => session);

        Assert.AreEqual(ViewportLifecycleState.Loaded, lifecycle.State);
        Assert.AreSame(session, lifecycle.Session);
    }

    /// <summary>
    /// Verifies that replacement disposes the previous session before creating its successor.
    /// </summary>
    [TestMethod]
    public void Replace_DisposesPreviousSessionBeforeCreatingReplacement()
    {
        var first = new TestSession();
        var second = new TestSession();
        var lifecycle = CreateLifecycle();
        lifecycle.Load(() => first);

        lifecycle.Replace(() =>
        {
            Assert.AreEqual(1, first.DisposeCount);
            Assert.AreEqual(ViewportLifecycleState.Replacing, lifecycle.State);
            return second;
        });

        Assert.AreEqual(ViewportLifecycleState.Loaded, lifecycle.State);
        Assert.AreSame(second, lifecycle.Session);
        Assert.AreEqual(0, second.DisposeCount);
    }

    /// <summary>
    /// Verifies that failed replacement creation disposes the previous session and
    /// faults the lifecycle.
    /// </summary>
    [TestMethod]
    public void Replace_WhenCreationFails_DisposesPreviousSessionAndFaults()
    {
        var first = new TestSession();
        var lifecycle = CreateLifecycle();
        lifecycle.Load(() => first);

        Assert.ThrowsException<InvalidOperationException>(() =>
            lifecycle.Replace(() => throw new InvalidOperationException("creation failed"))
        );

        Assert.AreEqual(ViewportLifecycleState.Faulted, lifecycle.State);
        Assert.IsNull(lifecycle.Session);
        Assert.AreEqual(1, first.DisposeCount);
    }

    /// <summary>
    /// Verifies that failed activation disposes the candidate session and faults the lifecycle.
    /// </summary>
    [TestMethod]
    public void Load_WhenActivationFails_DisposesCandidateAndFaults()
    {
        var session = new TestSession();
        var deactivationCount = 0;
        var lifecycle = new ViewportLifecycle<TestSession>(
            _ => throw new InvalidOperationException("activation failed"),
            () => deactivationCount++,
            () => { }
        );

        Assert.ThrowsException<InvalidOperationException>(() =>
            lifecycle.Load(() => session)
        );

        Assert.AreEqual(ViewportLifecycleState.Faulted, lifecycle.State);
        Assert.IsNull(lifecycle.Session);
        Assert.AreEqual(1, deactivationCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    /// <summary>
    /// Verifies that a deactivation failure does not prevent the session from being disposed.
    /// </summary>
    [TestMethod]
    public void Unload_WhenDeactivationFails_StillDisposesSession()
    {
        var session = new TestSession();
        var lifecycle = new ViewportLifecycle<TestSession>(
            _ => { },
            () => throw new InvalidOperationException("deactivation failed"),
            () => { }
        );
        lifecycle.Load(() => session);

        Assert.ThrowsException<InvalidOperationException>(lifecycle.Unload);

        Assert.AreEqual(ViewportLifecycleState.Faulted, lifecycle.State);
        Assert.IsNull(lifecycle.Session);
        Assert.AreEqual(1, session.DisposeCount);
    }

    /// <summary>
    /// Verifies that terminal cleanup runs once even when session disposal fails.
    /// </summary>
    [TestMethod]
    public void Dispose_WhenSessionFails_StillRunsTerminalCleanup()
    {
        var terminalDisposeCount = 0;
        var session = new TestSession { ThrowOnDispose = true };
        var lifecycle = new ViewportLifecycle<TestSession>(
            _ => { },
            () => { },
            () => terminalDisposeCount++
        );
        lifecycle.Load(() => session);

        Assert.ThrowsException<InvalidOperationException>(lifecycle.Dispose);
        lifecycle.Dispose();

        Assert.AreEqual(ViewportLifecycleState.Disposed, lifecycle.State);
        Assert.IsNull(lifecycle.Session);
        Assert.AreEqual(1, session.DisposeCount);
        Assert.AreEqual(1, terminalDisposeCount);
    }

    /// <summary>
    /// Verifies that a disposed lifecycle cannot create another session.
    /// </summary>
    [TestMethod]
    public void Load_AfterDispose_IsIgnored()
    {
        var creationCount = 0;
        var lifecycle = CreateLifecycle();
        lifecycle.Dispose();

        lifecycle.Load(() =>
        {
            creationCount++;
            return new TestSession();
        });

        Assert.AreEqual(ViewportLifecycleState.Disposed, lifecycle.State);
        Assert.AreEqual(0, creationCount);
    }

    /// <summary>
    /// Creates a lifecycle whose callbacks have no side effects.
    /// </summary>
    private static ViewportLifecycle<TestSession> CreateLifecycle()
    {
        return new ViewportLifecycle<TestSession>(_ => { }, () => { }, () => { });
    }

    /// <summary>
    /// Records lifecycle-owned disposal for the platform-independent tests.
    /// </summary>
    private sealed class TestSession : IDisposable
    {
        /// <summary>
        /// Gets the number of disposal attempts.
        /// </summary>
        public int DisposeCount { get; private set; }

        /// <summary>
        /// Gets whether disposal should fail after being recorded.
        /// </summary>
        public bool ThrowOnDispose { get; init; }

        /// <summary>
        /// Records and optionally fails a disposal attempt.
        /// </summary>
        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("session disposal failed");
            }
        }
    }
}
