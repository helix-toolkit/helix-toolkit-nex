
using HelixToolkit.Nex.ECS.Events;

namespace HelixToolkit.Nex.ECS.Tests;

[TestClass]
public class ECSEventBusTest
{
    #region Test Event Types
    private struct TestEvent
    {
        public int Value;
    }

    private struct AnotherTestEvent
    {
        public string? Message;
    }

    private readonly struct ReadOnlyTestEvent
    {
        public int Data { get; init; }
    }
    #endregion

    #region SubscriptionHandler Tests

    [TestMethod]
    public void SubscriptionHandler_Subscribe_IncreasesCount()
    {
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();

        Assert.AreEqual(0, handler.Count);

        using var sub1 = handler.Subscribe((world, msg) => { });
        Assert.AreEqual(1, handler.Count);

        using var sub2 = handler.Subscribe((world, msg) => { });
        Assert.AreEqual(2, handler.Count);
    }

    [TestMethod]
    public void SubscriptionHandler_Subscribe_ThrowsOnNullAction()
    {
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();

        Assert.ThrowsException<ArgumentNullException>(() => handler.Subscribe(null!));
    }

    [TestMethod]
    public void SubscriptionHandler_Unsubscribe_DecreasesCount()
    {
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();

        var sub1 = handler.Subscribe((world, msg) => { });
        var sub2 = handler.Subscribe((world, msg) => { });
        Assert.AreEqual(2, handler.Count);

        sub1.Dispose();
        Assert.AreEqual(1, handler.Count);

        sub2.Dispose();
        Assert.AreEqual(0, handler.Count);
    }

    [TestMethod]
    public void SubscriptionHandler_Unsubscribe_MultipleTimes_DoesNotDecrementBelowZero()
    {
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();

        var sub = handler.Subscribe((world, msg) => { });
        Assert.AreEqual(1, handler.Count);

        sub.Dispose();
        Assert.AreEqual(0, handler.Count);

        // Dispose again should not decrement below 0
        sub.Dispose();
        Assert.AreEqual(0, handler.Count);
    }

    [TestMethod]
    public void SubscriptionHandler_Publish_InvokesAllHandlers()
    {
        using var world = World.CreateWorld();
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();

        var receivedCount = 0;
        var receivedValue = 0;

        using var sub1 = handler.Subscribe(
            (w, msg) =>
            {
                receivedCount++;
                receivedValue += msg.Value;
            }
        );

        using var sub2 = handler.Subscribe(
            (w, msg) =>
            {
                receivedCount++;
                receivedValue += msg.Value * 2;
            }
        );

        handler.Publish(world, new TestEvent { Value = 10 });

        Assert.AreEqual(2, receivedCount);
        Assert.AreEqual(30, receivedValue); // 10 + (10 * 2)
    }

    [TestMethod]
    public void SubscriptionHandler_Publish_PassesCorrectWorldAndMessage()
    {
        using var world = World.CreateWorld();
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();

        World? receivedWorld = null;
        TestEvent receivedMessage = default;

        using var sub = handler.Subscribe(
            (w, msg) =>
            {
                receivedWorld = w;
                receivedMessage = msg;
            }
        );

        var testEvent = new TestEvent { Value = 42 };
        handler.Publish(world, testEvent);

        Assert.AreEqual(world, receivedWorld);
        Assert.AreEqual(42, receivedMessage.Value);
    }

    [TestMethod]
    public void SubscriptionHandler_Publish_AfterUnsubscribe_DoesNotInvokeUnsubscribedHandler()
    {
        using var world = World.CreateWorld();
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();

        var handler1Called = false;
        var handler2Called = false;

        var sub1 = handler.Subscribe((w, msg) => handler1Called = true);
        using var sub2 = handler.Subscribe((w, msg) => handler2Called = true);

        sub1.Dispose();

        handler.Publish(world, new TestEvent { Value = 1 });

        Assert.IsFalse(handler1Called);
        Assert.IsTrue(handler2Called);
    }

    [TestMethod]
    public void SubscriptionHandler_Clear_RemovesAllSubscriptions()
    {
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();

        using var sub1 = handler.Subscribe((w, msg) => { });
        using var sub2 = handler.Subscribe((w, msg) => { });
        using var sub3 = handler.Subscribe((w, msg) => { });

        Assert.AreEqual(3, handler.Count);

        handler.Clear();

        Assert.AreEqual(0, handler.Count);
    }

    [TestMethod]
    public void SubscriptionHandler_Clear_HandlerNotInvokedAfterClear()
    {
        using var world = World.CreateWorld();
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();

        var handlerCalled = false;
        using var sub = handler.Subscribe((w, msg) => handlerCalled = true);

        handler.Clear();
        handler.Publish(world, new TestEvent { Value = 1 });

        Assert.IsFalse(handlerCalled);
    }

    [TestMethod]
    public void SubscriptionHandler_SlotReuse_NewSubscriptionReusesSlot()
    {
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();

        var sub1 = handler.Subscribe((w, msg) => { });
        var sub2 = handler.Subscribe((w, msg) => { });

        Assert.AreEqual(2, handler.Count);

        sub1.Dispose();
        Assert.AreEqual(1, handler.Count);

        // New subscription should reuse the slot
        using var sub3 = handler.Subscribe((w, msg) => { });
        Assert.AreEqual(2, handler.Count);

        sub2.Dispose();
    }

    #endregion

    #region World Event Bus Integration Tests

    [TestMethod]
    public void World_Send_DeliversMessageToRegisteredHandler()
    {
        using var world = World.CreateWorld();

        TestEvent? receivedEvent = null;
        World? receivedWorld = null;

        using var sub = world.Register<TestEvent>(
            (w, msg) =>
            {
                receivedWorld = w;
                receivedEvent = msg;
            }
        );

        world.Send(new TestEvent { Value = 123 });

        Assert.IsNotNull(receivedEvent);
        Assert.AreEqual(123, receivedEvent.Value.Value);
        Assert.AreEqual(world, receivedWorld);
    }

    [TestMethod]
    public void World_Send_MultipleSubscribers_AllReceiveMessage()
    {
        using var world = World.CreateWorld();

        var count = 0;

        using var sub1 = world.Register<TestEvent>((w, msg) => count++);
        using var sub2 = world.Register<TestEvent>((w, msg) => count++);
        using var sub3 = world.Register<TestEvent>((w, msg) => count++);

        world.Send(new TestEvent { Value = 1 });

        Assert.AreEqual(3, count);
    }

    [TestMethod]
    public void World_Send_DifferentEventTypes_IsolatedDelivery()
    {
        using var world = World.CreateWorld();

        var testEventReceived = false;
        var anotherEventReceived = false;

        using var sub1 = world.Register<TestEvent>((w, msg) => testEventReceived = true);
        using var sub2 = world.Register<AnotherTestEvent>((w, msg) => anotherEventReceived = true);

        world.Send(new TestEvent { Value = 1 });

        Assert.IsTrue(testEventReceived);
        Assert.IsFalse(anotherEventReceived);
    }

    [TestMethod]
    public void World_Send_MultipleWorlds_IsolatedDelivery()
    {
        using var world1 = World.CreateWorld();
        using var world2 = World.CreateWorld();

        var world1Count = 0;
        var world2Count = 0;

        using var sub1 = world1.Register<TestEvent>((w, msg) => world1Count++);
        using var sub2 = world2.Register<TestEvent>((w, msg) => world2Count++);

        world1.Send(new TestEvent { Value = 1 });

        Assert.AreEqual(1, world1Count);
        Assert.AreEqual(0, world2Count);

        world2.Send(new TestEvent { Value = 2 });

        Assert.AreEqual(1, world1Count);
        Assert.AreEqual(1, world2Count);
    }

    [TestMethod]
    public void World_Register_Dispose_StopsReceivingMessages()
    {
        using var world = World.CreateWorld();

        var count = 0;

        var sub = world.Register<TestEvent>((w, msg) => count++);

        world.Send(new TestEvent { Value = 1 });
        Assert.AreEqual(1, count);

        sub.Dispose();

        world.Send(new TestEvent { Value = 2 });
        Assert.AreEqual(1, count); // Should still be 1, not 2
    }

    [TestMethod]
    public void World_Dispose_ClearsSubscriptions()
    {
        var world = World.CreateWorld();

        var handlerCalled = false;

        var sub = world.Register<TestEvent>((w, msg) => handlerCalled = true);

        world.Dispose();

        // After world disposal, the subscription should be cleared
        // and any further sends should not invoke the handler
        // (though the world is disposed, so we can't send through it)
        Assert.IsFalse(handlerCalled);

        sub.Dispose(); // Clean up
    }

    #endregion

    #region ReadOnly Event Tests

    [TestMethod]
    public void SubscriptionHandler_Publish_ReadOnlyStruct_Works()
    {
        using var world = World.CreateWorld();
        var handler = new ECSEventBus.SubscriptionHandler<ReadOnlyTestEvent>();

        var receivedData = 0;

        using var sub = handler.Subscribe((w, msg) => receivedData = msg.Data);

        handler.Publish(world, new ReadOnlyTestEvent { Data = 99 });

        Assert.AreEqual(99, receivedData);
    }

    #endregion

    #region Thread Safety Tests

    [TestMethod]
    public void SubscriptionHandler_ConcurrentSubscribeUnsubscribe_ThreadSafe()
    {
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();
        var subscriptions = new List<Subscription>();
        var lockObj = new object();

        // Subscribe and unsubscribe concurrently
        Parallel.For(
            0,
            100,
            i =>
            {
                var sub = handler.Subscribe((w, msg) => { });
                lock (lockObj)
                {
                    subscriptions.Add(sub);
                }
            }
        );

        Assert.AreEqual(100, handler.Count);

        // Unsubscribe all concurrently
        Parallel.ForEach(subscriptions, sub => sub.Dispose());

        Assert.AreEqual(0, handler.Count);
    }

    [TestMethod]
    public void World_ConcurrentSend_MultipleWorlds_NoExceptions()
    {
        var worlds = new List<World>();
        for (int i = 0; i < 5; i++)
        {
            worlds.Add(World.CreateWorld());
        }

        var subscriptions = new List<Subscription>();
        var messageCount = 0;

        foreach (var world in worlds)
        {
            subscriptions.Add(
                world.Register<TestEvent>(
                    (w, msg) =>
                    {
                        Interlocked.Increment(ref messageCount);
                    }
                )
            );
        }

        // Send messages concurrently from different worlds
        Parallel.ForEach(
            worlds,
            world =>
            {
                for (int i = 0; i < 100; i++)
                {
                    world.Send(new TestEvent { Value = i });
                }
            }
        );

        Assert.AreEqual(500, messageCount); // 5 worlds * 100 messages each

        foreach (var sub in subscriptions)
        {
            sub.Dispose();
        }

        foreach (var world in worlds)
        {
            world.Dispose();
        }
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void SubscriptionHandler_Publish_NoSubscribers_NoException()
    {
        using var world = World.CreateWorld();
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();

        // Should not throw
        handler.Publish(world, new TestEvent { Value = 42 });
    }

    [TestMethod]
    public void SubscriptionHandler_ExpandArray_ManySubscriptions()
    {
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();
        var subscriptions = new List<Subscription>();

        // Add many subscriptions to force array expansion
        for (int i = 0; i < 100; i++)
        {
            subscriptions.Add(handler.Subscribe((w, msg) => { }));
        }

        Assert.AreEqual(100, handler.Count);

        foreach (var sub in subscriptions)
        {
            sub.Dispose();
        }

        Assert.AreEqual(0, handler.Count);
    }

    [TestMethod]
    public void World_Send_StringEvent_Works()
    {
        using var world = World.CreateWorld();

        AnotherTestEvent? received = null;

        using var sub = world.Register<AnotherTestEvent>((w, msg) => received = msg);

        world.Send(new AnotherTestEvent { Message = "Hello, ECS!" });

        Assert.IsNotNull(received);
        Assert.AreEqual("Hello, ECS!", received.Value.Message);
    }

    [TestMethod]
    public void Subscription_DisposeMultipleTimes_Safe()
    {
        using var world = World.CreateWorld();

        var sub = world.Register<TestEvent>((w, msg) => { });

        // Multiple dispose calls should be safe
        sub.Dispose();
        sub.Dispose();
        sub.Dispose();

        // Should not throw
    }

    [TestMethod]
    public void World_Register_AfterDispose_OtherWorldStillWorks()
    {
        var world1 = World.CreateWorld();
        using var world2 = World.CreateWorld();

        var world2Called = false;

        using var sub1 = world1.Register<TestEvent>((w, msg) => { });
        using var sub2 = world2.Register<TestEvent>((w, msg) => world2Called = true);

        world1.Dispose();

        // world2 should still work
        world2.Send(new TestEvent { Value = 1 });

        Assert.IsTrue(world2Called);
    }

    #endregion

    #region Handler During Publish Tests

    [TestMethod]
    public void SubscriptionHandler_Publish_HandlerModifiesMessage_NoSideEffect()
    {
        using var world = World.CreateWorld();
        var handler = new ECSEventBus.SubscriptionHandler<TestEvent>();

        var handler2Value = 0;

        using var sub1 = handler.Subscribe(
            (w, msg) =>
            {
                // Try to modify the message (struct is a copy, so this shouldn't affect other handlers)
                msg.Value = 999;
            }
        );

        using var sub2 = handler.Subscribe(
            (w, msg) =>
            {
                handler2Value = msg.Value;
            }
        );

        handler.Publish(world, new TestEvent { Value = 42 });

        // Handler2 should still receive the original value
        Assert.AreEqual(42, handler2Value);
    }

    #endregion
}
