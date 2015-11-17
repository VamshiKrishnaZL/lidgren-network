# Quick start #

### How to set up a Server ###
```

// create a configuration
NetConfiguration config = new NetConfiguration("myAppName"); // needs to be same on client and server!
config.MaxConnections = 32;
config.Port = 12345;

NetServer server = new NetServer(config);
server.Start();
```

### How to create a client and connect to a server ###
```

NetConfiguration config = new NetConfiguration("myAppName"); // needs to be same on client and server!
NetClient client = new NetClient(config);

client.Connect("127.0.0.1", 12345);
```

### How to read messages on the client ###
```
NetBuffer buffer = client.CreateBuffer();

bool keepGoing = true;
while (keepGoing)
{
	NetMessageType type;
	while (client.ReadMessage(buffer, out type))
	{
		switch (type)
		{
			case NetMessageType.DebugMessage:
				Console.WriteLine(buffer.ReadString());
				break;

			case NetMessageType.StatusChanged:
				Console.WriteLine("New status: " + client.Status + " (Reason: " + buffer.ReadString() + ")");
				break;

			case NetMessageType.Data:
				// Handle data in buffer here
				break;
		}
	}
}
```

### How to send a message ###
```
NetBuffer buffer = client.CreateBuffer();

buffer.Write("Hello server!");

client.SendMessage(buffer, NetChannel.ReliableUnordered);
```

There are 4 types of channels:
  * `UnreliableUnordered` - Messages may or may not arrive
  * `UnreliableInOrder` - Messages may or may not arrive. If a message arrives late, ie. a newer message has already arrived, the late packet will be dropped
  * `ReliableUnordered` - Messages will arrive, but not necessarily in the order they were sent.
  * `ReliableInOrder` - Messages will arrive, in the same order they were sent.
All types have duplicate detection (even the unreliable type) which ensures that a packet will not arrive multiple times.

### How to shutdown gracefully ###
```
server.Shutdown("Bye everyone");

or

client.Shutdown("Bye server");
```

All connections will be closed and the supplied string given as reason.