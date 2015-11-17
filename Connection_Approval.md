A `NetServer` or `NetPeer` can reject, or approve, connection attempts before the connection is formed (and added to the connections list).
This is done by enabling the `NetMessageType.ConnectionApproval` type like this:

```
myServer.SetMessageTypeEnabled(NetMessageType.ConnectionApproval, true);
```

When you enable this type of message you might receive a `ConnectionApproval` message when calling `ReadMessage`, and you can then examine the hail data contained in the NetBuffer returned by the call and the IP of the connecting party. You then have two options:

```
senderConnection.Approve();
```

... or...

```
senderConnection.Disapprove("Bye bye, you cannot connect!");
```

You will HAVE to call either of these; or the connection will keep hanging and the connector will eventually time out.

If you do now have the `ConnectionApproval` message type enabled, connections will automatically be approved.

If a connection is disapproved - the client will receive a ConnectionRejected message with the supplied string is its NetBuffer.