﻿using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lidgren.Network
{
	[DebuggerDisplay("LengthBytes={LengthBytes}")]
	public sealed partial class NetOutgoingMessage
	{
		// reference count before message can be recycled
		internal int m_inQueueCount;
		internal NetMessageType m_type;
		internal double m_sentTime;
		internal NetMessageLibraryType m_libType;

		/// <summary>
		/// Returns true if this message has been passed to SendMessage() already
		/// </summary>
		public bool IsSent { get { return m_inQueueCount > 0; } }

		internal NetOutgoingMessage()
		{
			Reset();
		}

		internal void Reset()
		{
			m_type = NetMessageType.Error;
			m_inQueueCount = 0;
		}

		internal int Encode(byte[] buffer, int ptr, NetConnection conn)
		{
			// message type
			buffer[ptr++] = (byte)m_type;

			if (m_type == NetMessageType.Library)
				buffer[ptr++] =(byte)m_libType;

			// channel sequence number
			if (m_type >= NetMessageType.UserSequenced)
			{
				if (conn == null)
					throw new NetException("Trying to encode NetMessageType " + m_type + " to unconnected endpoint!");
				ushort seqNr = conn.GetSendSequenceNumber(m_type);
				buffer[ptr++] = (byte)seqNr;
				buffer[ptr++] = (byte)(seqNr >> 8);
			}

			// payload length
			int msgPayloadLength = LengthBytes;
			System.Diagnostics.Debug.Assert(msgPayloadLength < 32768);
			if (msgPayloadLength < 127)
			{
				buffer[ptr++] = (byte)msgPayloadLength;
			}
			else
			{
				buffer[ptr++] = (byte)((msgPayloadLength & 127) | 128);
				buffer[ptr++] = (byte)(msgPayloadLength >> 7);
			}

			// payload
			if (msgPayloadLength > 0)
			{
				Buffer.BlockCopy(m_data, 0, buffer, ptr, msgPayloadLength);
				ptr += msgPayloadLength;
			}

			return ptr;
		}
	}
}
