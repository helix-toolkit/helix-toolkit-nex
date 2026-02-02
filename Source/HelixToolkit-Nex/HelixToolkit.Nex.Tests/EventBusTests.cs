namespace HelixToolkit.Nex.Tests;

[TestClass]
public sealed class EventBusTests
{
    private EventBus? _eventBus;

    [TestInitialize]
    public void Setup()
    {
        // Do not capture the context for unit tests to ensure consistent behavior
        // (unless we specifically set up a context).
        _eventBus = new EventBus(captureMainThreadContext: false);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _eventBus?.Dispose();
    }

    [TestMethod]
    public void Subscribe_ShouldIncrementSubscriberCount()
    {
        Assert.IsNotNull(_eventBus);
        Assert.AreEqual(0, _eventBus.GetSubscriberCount<TestEvent>());

        using var subscription = _eventBus.Subscribe<TestEvent>((e) => { });

        Assert.AreEqual(1, _eventBus.GetSubscriberCount<TestEvent>());
    }

    [TestMethod]
    public void Unsubscribe_ShouldDecrementSubscriberCount()
    {
        Assert.IsNotNull(_eventBus);
        var subscription = _eventBus.Subscribe<TestEvent>((e) => { });
        Assert.AreEqual(1, _eventBus.GetSubscriberCount<TestEvent>());

        subscription.Dispose();

        Assert.AreEqual(0, _eventBus.GetSubscriberCount<TestEvent>());
    }

    [TestMethod]
    public void Publish_ShouldInvokeHandler_Synchronously()
    {
        bool handled = false;
        string? receivedMessage = null;
        Assert.IsNotNull(_eventBus);
        _eventBus.Subscribe<TestEvent>(e =>
        {
            handled = true;
            receivedMessage = e.Message;
        });

        var testEvent = new TestEvent { Message = "Hello" };
        _eventBus.Publish(testEvent);

        Assert.IsTrue(handled);
        Assert.AreEqual("Hello", receivedMessage);
    }

    [TestMethod]
    public void PublishAsync_ShouldInvokeHandler_Eventually()
    {
        using var signal = new ManualResetEventSlim(false);
        string? receivedMessage = null;
        Assert.IsNotNull(_eventBus);
        _eventBus.Subscribe<TestEvent>(e =>
        {
            receivedMessage = e.Message;
            signal.Set();
        });

        var testEvent = new TestEvent { Message = "Async Hello" };
        _eventBus.PublishAsync(testEvent);

        // Wait for the background thread to process the event
        bool signaled = signal.Wait(TimeSpan.FromSeconds(2));

        Assert.IsTrue(signaled, "Event handler was not invoked within timeout.");
        Assert.AreEqual("Async Hello", receivedMessage);
    }

    [TestMethod]
    public void Publish_ShouldHandleMultipleSubscribers()
    {
        int callCount = 0;
        Assert.IsNotNull(_eventBus);
        _eventBus.Subscribe<TestEvent>(e => callCount++);
        _eventBus.Subscribe<TestEvent>(e => callCount++);

        _eventBus.Publish(new TestEvent());

        Assert.AreEqual(2, callCount);
    }

    [TestMethod]
    public void Publish_ExceptionInSubscriber_ShouldNotCrashBus()
    {
        bool secondHandlerCalled = false;
        Assert.IsNotNull(_eventBus);
        _eventBus.Subscribe<TestEvent>(e =>
        {
            throw new InvalidOperationException("Test exception");
        });

        _eventBus.Subscribe<TestEvent>(e =>
        {
            secondHandlerCalled = true;
        });

        try
        {
            _eventBus.Publish(new TestEvent());
        }
        catch (Exception)
        {
            Assert.Fail("EventBus.Publish should swallow exceptions from subscribers.");
        }

        Assert.IsTrue(
            secondHandlerCalled,
            "Subsequent subscribers should still be invoked after an exception."
        );
    }

    [TestMethod]
    public void Dispose_ShouldThrowOnSubsequentCalls()
    {
        Assert.IsNotNull(_eventBus);
        _eventBus.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() =>
        {
            _eventBus.Publish(new TestEvent());
        });

        Assert.ThrowsException<ObjectDisposedException>(() =>
        {
            _eventBus.PublishAsync(new TestEvent());
        });

        Assert.ThrowsException<ObjectDisposedException>(() =>
        {
            _eventBus.Subscribe<TestEvent>(e => { });
        });
    }

    [TestMethod]
    public void Subscribe_NullHandler_ShouldThrowArgumentNullException()
    {
        Assert.IsNotNull(_eventBus);
        Assert.ThrowsException<ArgumentNullException>(() =>
        {
            _eventBus.Subscribe<TestEvent>(null!);
        });
    }

    [TestMethod]
    public void Publish_NullEvent_ShouldThrowArgumentNullException()
    {
        Assert.IsNotNull(_eventBus);
        Assert.ThrowsException<ArgumentNullException>(() =>
        {
            _eventBus.Publish<TestEvent>(null!);
        });
    }

    [TestMethod]
    public void SingletonInstance_ShouldNotBeNull()
    {
        Assert.IsNotNull(EventBus.Instance);
    }

    [TestMethod]
    public void Publish_ShouldDispatchToSynchronizationContext_WhenConfigured()
    {
        var oldContext = SynchronizationContext.Current;
        var testContext = new TestSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(testContext);

        try
        {
            // Create a new bus that captures the current (test) context
            using var bus = new EventBus(captureMainThreadContext: true);

            bool handled = false;
            bus.Subscribe<TestEvent>(e => handled = true, dispatchOnMainThread: true);

            bus.Publish(new TestEvent());

            Assert.IsTrue(handled);
            // Verify that Post was called on our custom context
            Assert.AreEqual(1, testContext.PostCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }

    [TestMethod]
    public void PublishAsync_ShouldRunOnBackgroundThread_WhenNotDispatchingToMain()
    {
        Assert.IsNotNull(_eventBus);
        using var signal = new ManualResetEventSlim(false);
        bool isBackgroundThread = false;
        string threadName = string.Empty;

        // dispatchOnMainThread: false means it should stay on the publisher thread
        _eventBus.Subscribe<TestEvent>(
            e =>
            {
                isBackgroundThread = Thread.CurrentThread.IsBackground;
                threadName = Thread.CurrentThread.Name is not null ? Thread.CurrentThread.Name : "";
                signal.Set();
            },
            dispatchOnMainThread: false
        );

        _eventBus.PublishAsync(new TestEvent());

        Assert.IsTrue(signal.Wait(TimeSpan.FromSeconds(2)));
        Assert.IsTrue(isBackgroundThread, "Handler should run on background thread");
        Assert.AreEqual("EventBus Publisher Thread", threadName);
    }

    [TestMethod]
    public void PublishAsync_ShouldDispatchToSynchronizationContext()
    {
        var oldContext = SynchronizationContext.Current;
        var testContext = new TestSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(testContext);

        try
        {
            using var bus = new EventBus(captureMainThreadContext: true);
            using var signal = new ManualResetEventSlim(false);

            bus.Subscribe<TestEvent>(
                e =>
                {
                    signal.Set();
                },
                dispatchOnMainThread: true
            );

            bus.PublishAsync(new TestEvent());

            // Wait for the handler to be executed
            Assert.IsTrue(signal.Wait(TimeSpan.FromSeconds(2)));

            // Verify Post was called
            Assert.AreEqual(1, testContext.PostCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(oldContext);
        }
    }

    [TestMethod]
    public void Publish_NoSubscribers_ShouldNotThrow()
    {
        Assert.IsNotNull(_eventBus);
        try
        {
            _eventBus.Publish(new TestEvent());
            _eventBus.PublishAsync(new TestEvent());
        }
        catch (Exception ex)
        {
            Assert.Fail($"Publish with no subscribers threw exception: {ex}");
        }
    }

    [TestMethod]
    public void Publish_ReentrantSubscription_ShouldNotExecuteNewSubscriberInSamePublish()
    {
        Assert.IsNotNull(_eventBus);
        int callCount = 0;

        _eventBus.Subscribe<TestEvent>(e =>
        {
            callCount++;
            // Subscribe a new handler during execution
            _eventBus.Subscribe<TestEvent>(e2 => callCount++);
        });

        _eventBus.Publish(new TestEvent());

        // Should only be called once for the first subscriber
        // The second subscriber was added after snapshot was taken
        Assert.AreEqual(1, callCount);

        // Next publish should hit both
        _eventBus.Publish(new TestEvent());
        Assert.AreEqual(3, callCount); // 1 (prev) + 2 (now) = 3
    }

    [TestMethod]
    public void Publish_ReentrantUnsubscription_ShouldStillExecuteUnsubscribedHandlerInSamePublish()
    {
        Assert.IsNotNull(_eventBus);
        // This test documents the "snapshot" behavior where unsubscribing during a publish
        // does not prevent the unsubscribed handler from running if it was already in the snapshot.
        int callCount = 0;
        IEventSubscription sub2 = null!;

        _eventBus.Subscribe<TestEvent>(e =>
        {
            callCount++;
            // Unsubscribe sub2
            if (sub2 != null)
                sub2.Dispose();
        });

        sub2 = _eventBus.Subscribe<TestEvent>(e =>
        {
            callCount++;
        });

        _eventBus.Publish(new TestEvent());

        // Expect 2 because snapshot was taken before unsubscribe
        Assert.AreEqual(2, callCount);

        // Next publish should only hit first subscriber
        _eventBus.Publish(new TestEvent());
        Assert.AreEqual(3, callCount); // 2 (prev) + 1 (now) = 3
    }

    [TestMethod]
    public void GetSubscriberCount_ShouldReturnCorrectCount()
    {
        Assert.IsNotNull(_eventBus);
        // Initial count
        Assert.AreEqual(0, _eventBus.GetSubscriberCount<TestEvent>());

        // After adding one
        var sub1 = _eventBus.Subscribe<TestEvent>(e => { });
        Assert.AreEqual(1, _eventBus.GetSubscriberCount<TestEvent>());

        // After adding another
        var sub2 = _eventBus.Subscribe<TestEvent>(e => { });
        Assert.AreEqual(2, _eventBus.GetSubscriberCount<TestEvent>());

        // After removing one
        sub1.Dispose();
        Assert.AreEqual(1, _eventBus.GetSubscriberCount<TestEvent>());

        // After removing last
        sub2.Dispose();
        Assert.AreEqual(0, _eventBus.GetSubscriberCount<TestEvent>());

        // Ensure disposing again doesn't break count
        sub2.Dispose();
        Assert.AreEqual(0, _eventBus.GetSubscriberCount<TestEvent>());
    }

    [TestMethod]
    public void Subscribe_DifferentEventTypes_ShouldBeIndependent()
    {
        Assert.IsNotNull(_eventBus);
        _eventBus.Subscribe<TestEvent>(e => { });
        _eventBus.Subscribe<OtherEvent>(e => { });

        Assert.AreEqual(1, _eventBus.GetSubscriberCount<TestEvent>());
        Assert.AreEqual(1, _eventBus.GetSubscriberCount<OtherEvent>());
    }

    [TestMethod]
    public void ConcurrentSubscription_ShouldBeThreadSafe()
    {
        Assert.IsNotNull(_eventBus);
        // Test parallel subscription adding
        Parallel.For(
            0,
            100,
            i =>
            {
                _eventBus.Subscribe<TestEvent>(e => { });
            }
        );

        Assert.AreEqual(100, _eventBus.GetSubscriberCount<TestEvent>());
    }

    [TestMethod]
    public void ConcurrentPublish_ShouldBeThreadSafe()
    {
        Assert.IsNotNull(_eventBus);
        int totalCalls = 0;
        object lockObj = new object();

        _eventBus.Subscribe<TestEvent>(
            e =>
            {
                lock (lockObj)
                    totalCalls++;
            },
            dispatchOnMainThread: false
        );

        int numberOfThreads = 10;
        int publishesPerThread = 100;

        Parallel.For(
            0,
            numberOfThreads,
            i =>
            {
                for (int j = 0; j < publishesPerThread; j++)
                {
                    _eventBus.Publish(new TestEvent());
                }
            }
        );

        Assert.AreEqual(numberOfThreads * publishesPerThread, totalCalls);
    }

    // Helper class for testing
    public class TestEvent : IEvent
    {
        public string Message { get; set; } = string.Empty;
    }

    // Helper class for testing
    public class OtherEvent : IEvent { }

    // Helper context to verify UI thread dispatching
    private class TestSynchronizationContext : SynchronizationContext
    {
        private int _postCount;
        public int PostCount => _postCount;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            d(state);
        }
    }
}
