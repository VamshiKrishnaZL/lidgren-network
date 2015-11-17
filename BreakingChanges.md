  * No `Heartbeat()` method. It still exists, but it's being called internally on a separate thread.

  * `NetBase.ReadMessage()` is very different. First of all it doesn't return a data object. Instead you pass a buffer to the method which will be filled with data. This means you can reuse the same buffer every time and save on allocations. Secondly it may not only return data messages, but library messages too. You must examine the `NetMessageType` out parameter in the method to determine if it's a 'Data' message or `StatusChanged`, `ServerDiscovered`, `Receipt` or `DebugMessage` - See [MessageTypes](MessageTypes.md).

  * Thus, Log messages, `StatusChanged` notification, `ServerDiscovered` doesn't use events anymore, they're retrieved using `ReadMessage()`.

  * Encryption isn't implemented yet. It will probably work very differently; i'm currently looking into implementing SRP (http://srp.stanford.edu) and XTEA.

  * When sending a message, you won't create a new `NetMessage` object. Instead you should call `NetBase.CreateBuffer()` and use the returned object. The library pools objects internally to reduce memory allocations.

  * No `ConnectionRequest` event implemented yet. Events across threads would require application thread safe code and I want to avoid requiring that.

  * No String Table support yet.