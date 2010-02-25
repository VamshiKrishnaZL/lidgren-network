﻿/* Copyright (c) 2010 Michael Lidgren

Permission is hereby granted, free of charge, to any person obtaining a copy of this software
and associated documentation files (the "Software"), to deal in the Software without
restriction, including without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom
the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
USE OR OTHER DEALINGS IN THE SOFTWARE.

*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace Lidgren.Network
{
	[DebuggerDisplay("RemoteEndPoint={m_remoteEndPoint} Status={m_status}")]
	public partial class NetConnection
	{
		private NetPeer m_owner;
		internal IPEndPoint m_remoteEndPoint;
		internal double m_lastHeardFrom;
		private NetQueue<NetOutgoingMessage> m_unsentMessages;
		internal NetConnectionStatus m_status;
		private double m_lastSentUnsentMessages;
		private float m_throttleDebt;

		internal PendingConnectionStatus m_pendingStatus = PendingConnectionStatus.NotPending;
		internal string m_pendingDenialReason;

		internal NetConnection(NetPeer owner, IPEndPoint remoteEndPoint)
		{
			m_owner = owner;
			m_remoteEndPoint = remoteEndPoint;
			m_unsentMessages = new NetQueue<NetOutgoingMessage>(16);
			m_status = NetConnectionStatus.None;

			double now = NetTime.Now;
			m_nextPing = now + 5.0f;
			m_nextKeepAlive = now + 5.0f + m_owner.m_configuration.m_keepAliveDelay;

			// "slow start"
			m_throttleDebt = m_owner.m_configuration.m_throttleBytesPerSecond;
		}

		internal ushort GetSendSequenceNumber(NetMessageType tp)
		{
			throw new NotImplementedException();
		}

		// run on network thread
		internal void Heartbeat(double now)
		{
			m_owner.VerifyNetworkThread();

			if (m_connectRequested)
				SendConnect();

			// keepalive
			KeepAliveHeartbeat(now);

			// send unsent messages; high priority first
			byte[] buffer = m_owner.m_sendBuffer;
			int ptr = 0;

			float throttle = m_owner.m_configuration.m_throttleBytesPerSecond;
			if (throttle > 0)
			{
				double frameLength = now - m_lastSentUnsentMessages;
				if (m_throttleDebt > 0)
					m_throttleDebt -= (float)(frameLength * throttle);
				if (m_throttleDebt < 0)
					m_throttleDebt = 0;
				m_lastSentUnsentMessages = now;
			}

			if (m_throttleDebt < throttle)
			{
				while (m_unsentMessages.Count > 0)
				{
					if (m_throttleDebt >= throttle)
						break;

					NetOutgoingMessage msg = m_unsentMessages.TryDequeue();
					if (msg == null)
						continue;

					int msgPayloadLength = msg.LengthBytes;
					msg.m_sentTime = now;

					if (ptr > 0 && (ptr + NetPeer.kMaxPacketHeaderSize + msgPayloadLength) > m_owner.m_configuration.MaximumTransmissionUnit)
					{
						// send packet and start new packet
						m_owner.SendPacket(ptr, m_remoteEndPoint);
						m_throttleDebt += ptr;
						ptr = 0;
					}

					//
					// encode message
					//

					ptr = msg.Encode(buffer, ptr, this);

					if (msg.m_type >= NetMessageType.UserReliableUnordered)
					{
						// message is reliable, store for resend
						StoreReliableMessage(msg);
					}
					else
					{
						Interlocked.Decrement(ref msg.m_inQueueCount);
					}

					if (msg.m_type == NetMessageType.Library && msg.m_libType == NetMessageLibraryType.Disconnect)
					{
						FinishDisconnect();
						break;
					}
				}

				if (ptr > 0)
				{
					m_owner.SendPacket(ptr, m_remoteEndPoint);
					m_throttleDebt += ptr;
				}
			}
		}

		internal void HandleUserMessage(double now, NetMessageType mtp, ushort channelSequenceNumber, int ptr, int payloadLength)
		{
			m_owner.VerifyNetworkThread();

			try
			{
				if (!m_owner.m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.Data))
					return;

				//
				// TODO: do reliabilility, sequence rejecting etc here using channelSequenceNumber
				//

				NetDeliveryMethod ndm = NetPeer.GetDeliveryMethod(mtp);

				// release to application
				NetIncomingMessage im = m_owner.CreateIncomingMessage(NetIncomingMessageType.Data, m_owner.m_receiveBuffer, ptr, payloadLength);
				im.m_deliveredMethod = ndm;
				im.m_senderConnection = this;
				im.m_senderEndPoint = m_remoteEndPoint;

				m_owner.LogVerbose("Releasing " + im);
				m_owner.ReleaseMessage(im);

				return;
			}
			catch (Exception ex)
			{
#if DEBUG
				throw new NetException("Message generated exception: " + ex, ex);
#else
				m_owner.LogError("Message generated exception: " + ex);
				ptr += payloadLength;
				return;
#endif
			}
		}

		internal void HandleLibraryMessage(double now, NetMessageLibraryType libType, int ptr, int payloadLength)
		{
			m_owner.VerifyNetworkThread();

			switch (libType)
			{
				case NetMessageLibraryType.Error:
					m_owner.LogWarning("Received NetMessageLibraryType.Error message!");
					break;
				case NetMessageLibraryType.Connect:
				case NetMessageLibraryType.ConnectResponse:
				case NetMessageLibraryType.ConnectionEstablished:
				case NetMessageLibraryType.Disconnect:
					HandleIncomingHandshake(libType, ptr, payloadLength);
					break;
				case NetMessageLibraryType.KeepAlive:
					// no operation, we just want the acks
					break;
				case NetMessageLibraryType.Ping:
					if (payloadLength > 0)
						HandleIncomingPing(m_owner.m_receiveBuffer[ptr]);
					else
						m_owner.LogWarning("Received malformed ping");
					break;
				case NetMessageLibraryType.Pong:
					if (payloadLength > 0)
						HandleIncomingPong(now, m_owner.m_receiveBuffer[ptr]);
					else
						m_owner.LogWarning("Received malformed pong");
					break;
				default:
					throw new NotImplementedException();
			}

			return;
		}

		public void SendMessage(NetOutgoingMessage msg, NetDeliveryMethod channel)
		{
			if (msg.IsSent)
				throw new NetException("Message has already been sent!");
			msg.m_type = (NetMessageType)channel;
			EnqueueOutgoingMessage(msg);
		}

		internal void EnqueueOutgoingMessage(NetOutgoingMessage msg)
		{
			m_unsentMessages.Enqueue(msg);

			Interlocked.Increment(ref msg.m_inQueueCount);
		}

		public void Disconnect(string byeMessage)
		{
			// called on user thread
			if (m_status == NetConnectionStatus.None || m_status == NetConnectionStatus.Disconnected)
				return;

			m_owner.LogVerbose("Disconnect requested for " + this);
			m_disconnectByeMessage = byeMessage;

			NetOutgoingMessage bye = m_owner.CreateLibraryMessage(NetMessageLibraryType.Disconnect, byeMessage);
			EnqueueOutgoingMessage(bye);
		}

		public void Approve()
		{
			if (!m_owner.m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.ConnectionApproval))
				m_owner.LogError("Approve() called but ConnectionApproval is not enabled in NetPeerConfiguration!");

			if (m_pendingStatus != PendingConnectionStatus.Pending)
			{
				m_owner.LogWarning("Approve() called on non-pending connection!");
				return;
			}
			m_pendingStatus = PendingConnectionStatus.Approved;
		}

		public void Deny(string reason)
		{
			if (!m_owner.m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.ConnectionApproval))
				m_owner.LogError("Deny() called but ConnectionApproval is not enabled in NetPeerConfiguration!");

			if (m_pendingStatus != PendingConnectionStatus.Pending)
			{
				m_owner.LogWarning("Deny() called on non-pending connection!");
				return;
			}
			m_pendingStatus = PendingConnectionStatus.Denied;
			m_pendingDenialReason = reason;
		}

		internal void Dispose()
		{
			m_owner = null;
			m_unsentMessages = null;
		}

		public override string ToString()
		{
			return "[NetConnection to " + m_remoteEndPoint + "]";
		}
	}
}
