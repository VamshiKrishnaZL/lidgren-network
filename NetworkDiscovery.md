Discovering remote servers and remote peers is done by emitting a discovery signal and waiting for one or more responses. There are a two ways of emitting the discovery signal:

`NetClient.DiscoverLocalServers(port)` and `NetPeer.DiscoverLocalPeers(port)` or...

`NetClient.DiscoverKnownServer(host, port)` and `NetPeer.DiscoverKnownPeer(host, port)`

The first method(s) will emit a discovery signal on your local subnet. All servers/peers listening on the specified port will send a discovery response to you.
The second method will emit a discovery signal to a particular host/port. This can be useful if the user types in a hostname to connect to - you can then send a discovery signal before attempting to connect.

When a discovery response arrives you can read it as usual via `ReadMessage()`. The type will be `NetMessageType.ServerDiscovered` and the content of the buffer will hold the `IPEndPoint` of the responding party. Use `myBuffer.ReadIPEndPoint()` to retrieve it.

The ChatClient sample uses local discovery to find a ChatServer to connect to.