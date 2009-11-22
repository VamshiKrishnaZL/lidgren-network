﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace Lidgren.Network2
{
	public partial class NetConnection
	{
		private NetPeer m_owner;
		internal IPEndPoint m_remoteEndPoint;
		internal double m_lastHeardFrom;
		private Queue<NetOutgoingMessage>[] m_unsentMessages; // low, normal, high

		internal NetConnection(NetPeer owner, IPEndPoint remoteEndPoint)
		{
			m_owner = owner;
			m_remoteEndPoint = remoteEndPoint;
			m_unsentMessages = new Queue<NetOutgoingMessage>[3];
			m_unsentMessages[0] = new Queue<NetOutgoingMessage>(4);
			m_unsentMessages[1] = new Queue<NetOutgoingMessage>(8);
			m_unsentMessages[2] = new Queue<NetOutgoingMessage>(4);
			m_status = NetConnectionStatus.Disconnected;
			m_isPingInitialized = false;
		}

		// run on network thread
		internal void Heartbeat()
		{
			double now = NetTime.Now;

			if (m_connectRequested)
				SendConnect();

			if (m_disconnectRequested)
				SendDisconnect();

			// ping
			KeepAliveHeartbeat(now);

			// TODO: send ack messages

			// TODO: resend reliable messages

			// send unsent messages; high priority first
			byte[] buffer = m_owner.m_sendBuffer;
			int ptr = 0;
			bool isPacketReliable = false;
			int mtu = m_owner.m_configuration.MaximumTransmissionUnit;
			for (int i = 2; i >= 0; i--)
			{
				Queue<NetOutgoingMessage> queue = m_unsentMessages[i];
				if (queue.Count < 1)
					continue;

				NetOutgoingMessage msg;
				lock (queue)
					msg = queue.Dequeue();

				int msgPayloadLength = msg.LengthBytes;

				if (ptr + 3 + msgPayloadLength > mtu)
				{
					// send packet and start new packet
					m_owner.SendPacket(ptr, m_remoteEndPoint);
					ptr = 0;
				}

				if (ptr == 0)
				{
					// encode packet start
					ushort packetSequenceNumber = m_owner.GetSequenceNumber();
					buffer[ptr++] = (byte)((packetSequenceNumber & 127) << 1);
					buffer[ptr++] = (byte)((packetSequenceNumber << 7) & 255);
					isPacketReliable = false;
				}

				//
				// encode message
				//

				// set packet reliability flag
				if (!isPacketReliable && msg.m_type >= NetMessageType.UserReliableUnordered )
				{
					buffer[0] |= 1;
					isPacketReliable = true;
				}

				// flags
				if (msgPayloadLength < 256)
				{
					buffer[ptr++] = (byte)((int)msg.m_type << 2);
					buffer[ptr++] = (byte)msgPayloadLength;
				}
				else
				{
					buffer[ptr++] = (byte)(((int)msg.m_type << 2) | 1);
					buffer[ptr++] = (byte)(msgPayloadLength & 255);
					buffer[ptr++] = (byte)((msgPayloadLength << 8) & 255);
				}
				
				Buffer.BlockCopy(msg.m_data, 0, buffer, ptr, msgPayloadLength);
			}

			if (ptr > 0)
				m_owner.SendPacket(ptr, m_remoteEndPoint);
		}

		public void SendMessage(NetOutgoingMessage msg, NetMessagePriority priority)
		{

			if (msg.IsSent)
				throw new NetException("Message has already been sent!");
			EnqueueOutgoingMessage(msg, priority);
		}

		internal void EnqueueOutgoingMessage(NetOutgoingMessage msg, NetMessagePriority priority)
		{
			Queue<NetOutgoingMessage> queue = m_unsentMessages[(int)priority];
			lock (queue)
				queue.Enqueue(msg);
			Interlocked.Increment(ref msg.m_inQueueCount);
		}

		public void Disconnect(string byeMessage)
		{
			if (m_status == NetConnectionStatus.Disconnected)
				return;
			m_disconnectByeMessage = byeMessage;
			m_disconnectRequested = true;
		}

		internal void HandleIncomingData(NetMessageType mtp, byte[] payload, int payloadLength)
		{
			if (mtp < NetMessageType.LibraryNatIntroduction)
			{
				HandleIncomingLibraryData(mtp, payload, payloadLength);
				return;
			}

			// it's an application data message
			NetIncomingMessage im = m_owner.CreateIncomingMessage(NetIncomingMessageType.Data, payload, payloadLength);
			im.SenderConnection = this;
			im.SenderEndpoint = m_remoteEndPoint;

			//
			// TODO: do reliabilility, acks, sequence rejecting etc here
			//

			m_owner.LogVerbose("Releasing " + im);
			m_owner.ReleaseMessage(im);
		}

		private void HandleIncomingLibraryData(NetMessageType mtp, byte[] payload, int payloadLength)
		{
			switch (mtp)
			{
				case NetMessageType.Error:
					m_owner.LogWarning("Received NetMessageType.Error message!");
					break;
				case NetMessageType.LibraryConnect:
				case NetMessageType.LibraryConnectResponse:
				case NetMessageType.LibraryConnectionEstablished:
				case NetMessageType.LibraryDisconnect:
					HandleIncomingHandshake(mtp, payload, payloadLength);
					break;
				default:
					throw new NotImplementedException();
			}
		}
	}
}
