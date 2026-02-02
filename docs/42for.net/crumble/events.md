# Events supported in crumble

There are three types of events, no more are needed. By combining them effectively, most problems and complex orchestrations can be solved.

## Time event

Time trigger is specified by a CRON expression. At the same time, it can have a specific time zone to make it even more customized. 

```csharp
[TimeEvent(
    "*/15 * * * *",
    TimeZone = "Central European Standard Time")]
[Crumb]
public void Timed(DateTime triggerCetTime)
{
    Console.WriteLine($"Timed based crumb executed at {DateTime.UtcNow} and triggered at {triggerCetTime} (CET).");
}
```

## Message event

Message triggers represent async communication on any of the supported channels. Multiple sources can be registered simultaneously, each with a unique key.

To register a crumb for a specific queue:

```csharp
[MessageEvent(QueueKey = "MyQueue")]
[Crumb]
public void MessagedWithModel(MessageModel message)
{
    Console.WriteLine($"Message received: {message.MessageId}");
    Console.WriteLine(message.MessageText);
}
```

A crumb can receive a message in two different ways. The first is the example above, with a parameter of type `MessageModel`, which wraps the message ID and the body. The second option is just a string parameter that will contain the raw message.

## Volume event

The concept of **volume** and its triggers serves as an abstraction of a data source. **A virtual source of entities.** It is optimized to work with a large number of entities, which can be structured in a tree-shaped folder hierarchy. 

Multiple sources can be registered simultaneously, each with a unique key. The path filter is configurable by the `PathFilter` property.

```csharp
[VolumeEvent(
    VolumeKey = "MyVolume",
    PathFilter = "folder/*.json")]
[Crumb]
public void Filed(string filePath)
{
    Console.WriteLine($"File received: {filePath}");
}
```

A crumb from a volume event must have a single string parameter, the path to the file/entity.

## Connect it to 2S platform

If you don't want to control the events from your code, and be able to configure them without changing and redeploying applications. I recommend connecting it to the [2S platform](/platform/introduction). That way, you have much more flexibility, and the entire ecosystem is fully configurable.