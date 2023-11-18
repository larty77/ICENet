<p align="center">
<img src="https://private-user-images.githubusercontent.com/125078218/243773703-1f791210-e92b-44b4-ab6d-a5afc3d89d34.png?jwt=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJnaXRodWIuY29tIiwiYXVkIjoicmF3LmdpdGh1YnVzZXJjb250ZW50LmNvbSIsImtleSI6ImtleTEiLCJleHAiOjE3MDAyOTMzMzcsIm5iZiI6MTcwMDI5MzAzNywicGF0aCI6Ii8xMjUwNzgyMTgvMjQzNzczNzAzLTFmNzkxMjEwLWU5MmItNDRiNC1hYjZkLWE1YWZjM2Q4OWQzNC5wbmc_WC1BbXotQWxnb3JpdGhtPUFXUzQtSE1BQy1TSEEyNTYmWC1BbXotQ3JlZGVudGlhbD1BS0lBSVdOSllBWDRDU1ZFSDUzQSUyRjIwMjMxMTE4JTJGdXMtZWFzdC0xJTJGczMlMkZhd3M0X3JlcXVlc3QmWC1BbXotRGF0ZT0yMDIzMTExOFQwNzM3MTdaJlgtQW16LUV4cGlyZXM9MzAwJlgtQW16LVNpZ25hdHVyZT0zMDgyOTIyMjY4ZDI1M2FmNzYyYWI4YjBhNzE2MGFiYjFkMDk1OTZhZmE1NjlmZWQzOGIwN2U0ZGNmNDRmZDg5JlgtQW16LVNpZ25lZEhlYWRlcnM9aG9zdCZhY3Rvcl9pZD0wJmtleV9pZD0wJnJlcG9faWQ9MCJ9.67WiBR5g5zC7B4c7ZwDY_kFeamUegzaKSHUvxLt1Y0w" width="150" height="150"></p>

<h1 align="center" tabindex="-1" dir="auto"><a class="anchor" aria-hidden="true"></a>ICENet</h1>

ICENet is as simple as possible written in c#. This implementation can be used in applications in the .NET environment, as well as in Unity.

<h2 tabindex="-1" dir="auto"><a class="anchor" aria-hidden="true"></a>My future additions</h2>

At the moment, the library implements a fairly convenient and clear system of package deserialization and deserialization. It has a convenient logging system.

<h3>The library now has:</h3>

<ul>
  <li style="font-size: smaller;">Installation and support connections (RUDP functionality)</li>
  <li style="font-size: smaller;">Handle/Send reliable packets (RUDP functionality)</li>
  <li style="font-size: smaller;">Convenient logging system</li>
  <li style="font-size: smaller;">Smart packet serialization</li>
  <li style="font-size: smaller;">Ability to change the low-level transport</li>
</ul>

<h3>In the library I want to implement:</h3>

<ul>
  <li style="font-size: smaller;">Thread safety</li>
  <li style="font-size: smaller;">Optimized and productive code</li>
  <li style="font-size: smaller;">Needed more simple and good library architecture. So far it seems strange</li>
</ul>

<h2 tabindex="-1" dir="auto"><a class="anchor" aria-hidden="true"></a>How it works in Unity</h2>

It just works. The main problem is that I do not know how to work with multi-threading, I will be honest, it causes me some incomprehension and difficulty. From the code in my library it is easy to understand that it runs in multiple threads in parallel, sometimes because of timers, sometimes because of asynchronous packet processing. Unity, unfortunately, has some problems with this. For example, Unity will complain if some of its methods are will execute from the main thread (e.g. Debug.Log has problems with this). But.... There is a solution to this problem, namely calling methods from a foreign thread in the main thread. There is a concept that describes this - Dispatcher. There are a lot of its implementations, for example for WPF applications. By the way, in unity this problem can be solved with coroutines (<a href = "https://github.com/PimDeWitte/UnityMainThreadDispatcher/blob/master/Runtime/UnityMainThreadDispatcher.cs">Good Example</a>). I have already experimented with it and I managed to make a wrapper over the library and remove all(maybe not all) problems associated with multithreading in Unity.

<h2 tabindex="-1" dir="auto"><a class="anchor" aria-hidden="true"></a>Usage</h2>

<h3>Client:</h3>

```csharp
private static void InitializeClient()
{
    Console.Write("Remote Port: ");
    int port = int.Parse(Console.ReadLine()!);

    IceClient client = new IceClient(null!);

    client.Traffic.AddHandler<WelcomePacket>(HandleWelcome);

    client.TryConnect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), port), new IPEndPoint(0, 0));

    Thread.Sleep(1000);

    for (int i = 0; i < 20; i++)
        client.Send(new WelcomePacket { Message = $"MSG FROM CLIENT {i}" });
}

public static void HandleWelcome(Packet welcomePacket)
{
    Console.WriteLine((welcomePacket as WelcomePacket)!.Message);
}
```

<h3>Server:</h3>

```csharp
private static void InitializeServer()
{
    IceServer server = new IceServer(null!, 3);

    server.Traffic.AddHandler<WelcomePacket>(HandleWelcome);

    server.TryStart(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 0));

    Thread.Sleep(20000);

    server.Connections[0].Send(new WelcomePacket { Message = "MSG FROM SERVER!" });
}

public static void HandleWelcome(Packet welcomePacket, IceConnection connection)
{
    Console.WriteLine($"{(welcomePacket as WelcomePacket)!.Message!} <- {connection.RemoteEndPoint}[{connection.Ping}]");
}
```

<h3>Common:</h3>

```csharp

public class WelcomePacket : Packet
{
    public override int Id => 51;

    public override bool IsReliable => false;

    public string? Message;

    protected override void Write(ref Data data) 
    { 
        data.Write(Message!);
    }

    protected override void Read(ref Data data) 
    { 
        Message = data.ReadString();
    }
}

```


