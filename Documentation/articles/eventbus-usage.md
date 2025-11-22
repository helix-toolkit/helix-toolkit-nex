# EventBus Usage Guide

The EventBus provides a thread-safe publish/subscribe pattern implementation with generic template support for different event types. It supports asynchronous publishing and automatically dispatches subscribers to the main thread by default.

## Key Features

- **Generic Template Support**: Define custom event types using generics
- **Thread-Safe Publishing**: Async publish operations are queued and processed safely
- **Main Thread Dispatch**: Subscribers are invoked on the main thread by default (configurable)
- **Automatic Cleanup**: Disposable subscription handles for easy unsubscribe

## Basic Usage

### 1. Define Event Types

All events must implement the `IEvent` interface:

```csharp
using HelixToolkit.Nex;

// Simple event with no data
public class AppStartedEvent : IEvent { }

// Event with data
public class UserLoginEvent : IEvent
{
    public string Username { get; set; }
    public DateTime Timestamp { get; set; }
}

// Complex event with multiple properties
public class DataLoadedEvent : IEvent
{
    public string DataSource { get; set; }
    public int RecordCount { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
```

### 2. Create EventBus Instance

```csharp
// Capture current synchronization context as main thread
var eventBus = new EventBus(captureMainThreadContext: true);

// Or without main thread context
var eventBus = new EventBus(captureMainThreadContext: false);
```

### 3. Subscribe to Events

```csharp
// Subscribe with main thread dispatch (default)
var subscription = eventBus.Subscribe<UserLoginEvent>(evt =>
{
    Console.WriteLine($"User {evt.Username} logged in at {evt.Timestamp}");
});

// Subscribe without main thread dispatch (runs on publisher thread)
var directSubscription = eventBus.Subscribe<DataLoadedEvent>(evt =>
{
    Console.WriteLine($"Data loaded: {evt.RecordCount} records");
}, dispatchOnMainThread: false);
```

### 4. Publish Events

```csharp
// Asynchronous publish (thread-safe, queued)
eventBus.PublishAsync(new UserLoginEvent
{
    Username = "john.doe",
    Timestamp = DateTime.Now
});

// Synchronous publish (immediate, on calling thread)
eventBus.Publish(new DataLoadedEvent
{
    DataSource = "Database",
    RecordCount = 1000,
    Success = true
});
```

### 5. Unsubscribe from Events

```csharp
// Dispose the subscription to unsubscribe
subscription.Dispose();

// Or use using statement for automatic cleanup
using (var tempSubscription = eventBus.Subscribe<AppStartedEvent>(evt =>
{
    Console.WriteLine("App started!");
}))
{
    // Subscription active here
    eventBus.Publish(new AppStartedEvent());
} // Automatically unsubscribed here
```

## Complete Example

```csharp
using HelixToolkit.Nex;

// Define events
public class MessageEvent : IEvent
{
    public string Message { get; set; }
    public int Priority { get; set; }
}

public class ErrorEvent : IEvent
{
    public Exception Error { get; set; }
    public string Context { get; set; }
}

// Application class
public class Application
{
    private readonly EventBus _eventBus;
    private readonly List<IEventSubscription> _subscriptions = new();

    public Application()
    {
        _eventBus = new EventBus();
        SetupEventHandlers();
    }

    private void SetupEventHandlers()
    {
        // Subscribe to message events
        _subscriptions.Add(_eventBus.Subscribe<MessageEvent>(HandleMessage));
        
        // Subscribe to error events with direct dispatch
        _subscriptions.Add(_eventBus.Subscribe<ErrorEvent>(
            HandleError, 
            dispatchOnMainThread: false
        ));
    }

    private void HandleMessage(MessageEvent evt)
    {
        Console.WriteLine($"[Priority {evt.Priority}] {evt.Message}");
    }

    private void HandleError(ErrorEvent evt)
    {
        Console.WriteLine($"Error in {evt.Context}: {evt.Error.Message}");
    }

    public void Run()
    {
        // Publish events asynchronously
        _eventBus.PublishAsync(new MessageEvent
        {
            Message = "Application started",
            Priority = 1
        });

        // Simulate some work
        Thread.Sleep(100);

        try
        {
            // Some operation that might fail
            throw new InvalidOperationException("Test error");
        }
        catch (Exception ex)
        {
            _eventBus.PublishAsync(new ErrorEvent
            {
                Error = ex,
                Context = "Run method"
            });
        }
    }

    public void Cleanup()
    {
        // Unsubscribe all
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
        _subscriptions.Clear();

        // Dispose event bus
        _eventBus.Dispose();
    }
}

// Usage
var app = new Application();
app.Run();
Thread.Sleep(500); // Wait for async events to process
app.Cleanup();
```

## Advanced Scenarios

### Multiple Subscribers to Same Event

```csharp
var eventBus = new EventBus();

// Multiple subscribers can listen to the same event type
eventBus.Subscribe<MessageEvent>(evt => 
    Console.WriteLine($"Handler 1: {evt.Message}"));

eventBus.Subscribe<MessageEvent>(evt => 
    Console.WriteLine($"Handler 2: {evt.Message}"));

eventBus.Subscribe<MessageEvent>(evt => 
    Console.WriteLine($"Handler 3: {evt.Message}"));

// All three handlers will be invoked
eventBus.PublishAsync(new MessageEvent { Message = "Hello!" });
```

### Checking Subscriber Count

```csharp
var count = eventBus.GetSubscriberCount<MessageEvent>();
Console.WriteLine($"Number of message subscribers: {count}");
```

### Thread Safety Considerations

```csharp
// PublishAsync is thread-safe and can be called from any thread
Task.Run(() => eventBus.PublishAsync(new MessageEvent { Message = "From thread 1" }));
Task.Run(() => eventBus.PublishAsync(new MessageEvent { Message = "From thread 2" }));
Task.Run(() => eventBus.PublishAsync(new MessageEvent { Message = "From thread 3" }));

// All events will be queued and processed safely
```

### Main Thread vs Background Thread

```csharp
var eventBus = new EventBus();

// Main thread subscription (default) - good for UI updates
eventBus.Subscribe<MessageEvent>(evt =>
{
    // This runs on the main thread (if SynchronizationContext was captured)
    UpdateUI(evt.Message);
}, dispatchOnMainThread: true);

// Background thread subscription - good for heavy processing
eventBus.Subscribe<DataLoadedEvent>(evt =>
{
    // This runs on the publisher thread or event queue thread
    ProcessData(evt);
}, dispatchOnMainThread: false);
```

## Best Practices

1. **Always dispose subscriptions** when no longer needed to prevent memory leaks
2. **Use PublishAsync** when possible for better thread safety
3. **Keep event handlers lightweight** - dispatch heavy work to background tasks
4. **Handle exceptions** in subscribers (the EventBus catches them but logs to Debug)
5. **Dispose the EventBus** on application shutdown
6. **Use meaningful event types** that clearly describe what happened
7. **Consider event data immutability** to avoid threading issues

## Error Handling

The EventBus automatically catches exceptions in event handlers and writes them to `Debug` output. This prevents one failing handler from affecting others:

```csharp
eventBus.Subscribe<MessageEvent>(evt =>
{
    throw new Exception("Handler 1 failed");
});

eventBus.Subscribe<MessageEvent>(evt =>
{
    Console.WriteLine("Handler 2 still runs!");
});

// Both handlers are invoked, error is logged but doesn't crash the bus
eventBus.Publish(new MessageEvent { Message = "Test" });
```

## Performance Considerations

- **Async publishing** uses a background thread with a queue, minimal overhead
- **Main thread dispatch** uses SynchronizationContext.Post, suitable for UI applications
- **Thread-safe collections** (ConcurrentDictionary, locks) ensure safety without excessive locking
- **Snapshot pattern** prevents issues with concurrent subscription changes during publish
