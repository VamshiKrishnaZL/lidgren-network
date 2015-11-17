Used to simulate bad networking conditions on an otherwise perfect connection (such as when running both server and client on the same computer) Example:

```
NetClient client = new NetClient(config);
client.SimulatedMinimumLatency = 0.1f;
client.SimulatedLatencyVariance = 0.05f;
client.SimulatedLoss = 0.1f;
client.SimulatedDuplicates = 0.05f;
```

Numbers are in roundtrip milliseconds for latency and percent for loss and duplicates. Thus, this snippet will introduce a client to server latency of 50 to 75 milliseconds (ie. roundtrip 100 to 150 milliseconds).
In this snippet packetloss is very high, 0.1f means 10% - ie. only 9 out of 10 packets will arrive.
5% (0.05f) of all packets will be duplicated, ie. two or more copies of the same packet will arrive at the destination.

The settings will only affect outgoing traffic; so to get a good simulation you want to use the same simulation numbers on both Client and Server.

The effect of these settings will only be vary noticably on different net channels. For example, low to moderate loss will not be noticeable on `NetChannel.ReliableUnordered` except for some message reordering and slight delay.