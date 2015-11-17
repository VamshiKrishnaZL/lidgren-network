
```
public class Car
{
	private bool m_occupied;
	private float m_throttle;
	private uint m_gear;
	private uint m_score;

	public void Encode(NetBuffer into)
	{
		// Boolean uses just one bit of data
		into.Write(m_occupied);

		// m_gear can only be 0 to 5, so 3 bits covers the entire range
		into.Write(m_gear, 3);

		// m_throttle is always set to 0.0f to 1.0f so we can compress this unit single
		// using only 8 bits; giving a maximum error of 0.04
		into.WriteUnitSingle(m_throttle, 8);

		// 8 bits for value 0-127, 16 bits for value 128-16383 etc
		into.WriteVariableUInt32(m_score);
	}

	public void Decode(NetBuffer from)
	{
		m_occupied = from.ReadBoolean();
		m_gear = from.ReadUInt32(3);
		m_throttle = from.ReadUnitSingle(8);
		m_score = from.ReadVariableUInt32();
	}
}
```