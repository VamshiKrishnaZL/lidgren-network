
```
[Flags]
public enum NetMessageType
{
	None = 0,
	Data = 1 << 0,
	StatusChanged = 1 << 1,
	ServerDiscovered = 1 << 2,
	Receipt = 1 << 3,
	DebugMessage = 1 << 4,
	BadMessageReceived = 1 << 5,
	ConnectionRejected = 1 << 6,
}
```