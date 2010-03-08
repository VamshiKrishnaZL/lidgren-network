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
	[DebuggerDisplay("RemoteEndpoint={m_remoteEndpoint} Status={m_status}")]
	public partial class NetConnection
	{
		private NetPeer m_owner;
		internal IPEndPoint m_remoteEndpoint;
		internal double m_lastHeardFrom;
		internal NetQueue<NetOutgoingMessage> m_unsentMessages;
		internal NetConnectionStatus m_status;
		private double m_lastSentUnsentMessages;
		private float m_throttleDebt;
		private NetPeerConfiguration m_peerConfiguration;
		internal NetConnectionStatistics m_statistics;
		private int m_lesserHeartbeats;
		private int m_nextFragmentGroupId;

		internal PendingConnectionStatus m_pendingStatus = PendingConnectionStatus.NotPending;
		internal string m_pendingDenialReason;

		public object Tag { get; set; }

		/// <summary>
		/// Statistics for this particular connection
		/// </summary>
		public NetConnectionStatistics Statistics { get { return m_statistics; } }

		internal NetConnection(NetPeer owner, IPEndPoint remoteEndpoint)
		{
			m_owner = owner;
			m_peerConfiguration = m_owner.m_configuration;
			m_remoteEndpoint = remoteEndpoint;
			m_unsentMessages = new NetQueue<NetOutgoingMessage>(16);
			m_status = NetConnectionStatus.None;

			double now = NetTime.Now;
			m_nextPing = now + 5.0f;
			m_nextKeepAlive = now + 6.0f + m_peerConfiguration.m_keepAliveDelay;
			m_lastSentUnsentMessages = now;
			m_lastSendRespondedTo = now;
			m_statistics = new NetConnectionStatistics(this);

			InitializeReliability();
		}

		// run on network thread
		internal void Heartbeat(double now)
		{
			m_owner.VerifyNetworkThread();

			m_lesserHeartbeats++;

			if (m_lesserHeartbeats >= 2)
			{
				//
				// Do greater heartbeat every third heartbeat
				//
				m_lesserHeartbeats = 0;

				// keepalive, timeout and ping stuff
				KeepAliveHeartbeat(now);

				if (m_connectRequested)
					SendConnect();

				// queue resends
				for (int i = 0; i < m_storedMessages.Length; i++)
				{
					if (m_storedMessagesNotEmpty.Get(i))
					{
						foreach (NetOutgoingMessage om in m_storedMessages[i])
						{
							if (now >= om.m_nextResendTime)
							{
								Resend(now, om);
								break; // need to break out here; collection may have been modified
							}
						}
					}
				}
			}

			// send unsent messages; high priority first
			byte[] buffer = m_owner.m_sendBuffer;
			int ptr = 0;

			float throttle = m_peerConfiguration.m_throttleBytesPerSecond;
			if (throttle > 0)
			{
				double frameLength = now - m_lastSentUnsentMessages;
				if (m_throttleDebt > 0)
					m_throttleDebt -= (float)(frameLength * throttle);
				m_lastSentUnsentMessages = now;
			}

			int mtu = m_peerConfiguration.MaximumTransmissionUnit;

			float throttleThreshold = m_peerConfiguration.m_throttlePeakBytes;
			if (m_throttleDebt < throttleThreshold)
			{
				//
				// Send new unsent messages
				//
				int numIncludedMessages = 0;
				while (m_unsentMessages.Count > 0)
				{
					if (m_throttleDebt >= throttleThreshold)
						break;

					NetOutgoingMessage msg = m_unsentMessages.TryDequeue();
					if (msg == null)
						continue;
					Interlocked.Decrement(ref msg.m_inQueueCount);

					int msgPayloadLength = msg.LengthBytes;
					msg.m_lastSentTime = now;

					if (ptr > 0 && (ptr + NetPeer.kMaxPacketHeaderSize + msgPayloadLength) > mtu)
					{
						// send packet and start new packet
						m_owner.SendPacket(ptr, m_remoteEndpoint, numIncludedMessages);
						m_statistics.PacketSent(ptr, numIncludedMessages);
						numIncludedMessages = 0;
						m_throttleDebt += ptr;
						ptr = 0;
					}

					//
					// encode message
					//

					ptr = msg.Encode(buffer, ptr, this);
					numIncludedMessages++;

					if (msg.m_type >= NetMessageType.UserReliableUnordered && msg.m_numSends == 1)
					{
						// message is sent for the first time, and is reliable, store for resend
						StoreReliableMessage(now, msg);
					}

					// room to piggyback some acks?
					if (m_acknowledgesToSend.Count > 0)
					{
						int payloadLeft = (mtu - ptr) - NetPeer.kMaxPacketHeaderSize;
						if (payloadLeft > 9)
						{
							// yes, add them as a regular message
							ptr = NetOutgoingMessage.EncodeAcksMessage(m_owner.m_sendBuffer, ptr, this, (payloadLeft - 3));

							if (m_acknowledgesToSend.Count < 1)
								m_nextForceAckTime = double.MaxValue;
						}
					}

					if (msg.m_type == NetMessageType.Library && msg.m_libType == NetMessageLibraryType.Disconnect)
					{
						FinishDisconnect();
						break;
					}

					if (msg.m_inQueueCount < 1)
						m_owner.Recycle(msg);
				}

				if (ptr > 0)
				{
					m_owner.SendPacket(ptr, m_remoteEndpoint, numIncludedMessages);
					m_statistics.PacketSent(ptr, numIncludedMessages);
					numIncludedMessages = 0;
					m_throttleDebt += ptr;
				}
			}
		}

		internal void HandleUserMessage(double now, NetMessageType mtp, bool isFragment, ushort channelSequenceNumber, int ptr, int payloadLength)
		{
			m_owner.VerifyNetworkThread();

			try
			{
				NetDeliveryMethod ndm = NetPeer.GetDeliveryMethod(mtp);

				//
				// Unreliable
				//
				if (ndm == NetDeliveryMethod.Unreliable)
				{
					AcceptMessage(mtp, isFragment, channelSequenceNumber, ptr, payloadLength);
					return;
				}

				//
				// UnreliableSequenced
				//
				if (ndm == NetDeliveryMethod.UnreliableSequenced)
				{
					bool reject = ReceivedSequencedMessage(mtp, channelSequenceNumber);
					if (!reject)
						AcceptMessage(mtp, isFragment, channelSequenceNumber, ptr, payloadLength);
					return;
				}

				//
				// Reliable delivery methods below
				//

				// queue ack
				m_acknowledgesToSend.Enqueue((int)channelSequenceNumber | ((int)mtp << 16));
				if (m_nextForceAckTime == double.MaxValue)
					m_nextForceAckTime = now + m_peerConfiguration.m_maxAckDelayTime;

				if (ndm == NetDeliveryMethod.ReliableSequenced)
				{
					bool reject = ReceivedSequencedMessage(mtp, channelSequenceNumber);
					if (!reject)
						AcceptMessage(mtp, isFragment, channelSequenceNumber, ptr, payloadLength);
					return;
				}

				// relate to all received up to
				int reliableSlot = (int)mtp - (int)NetMessageType.UserReliableUnordered;
				int diff = Relate(channelSequenceNumber, m_nextExpectedReliableSequence[reliableSlot]);

				if (diff > (ushort.MaxValue / 2))
				{
					// Reject out-of-window
					//m_statistics.CountDuplicateMessage(msg);
					m_owner.LogVerbose("Rejecting duplicate reliable " + mtp + " " + channelSequenceNumber);
					return;
				}

				if (diff == 0)
				{
					// Expected sequence number
					AcceptMessage(mtp, isFragment, channelSequenceNumber, ptr, payloadLength);
				
					ExpectedReliableSequenceArrived(reliableSlot);
					return;
				}

				//
				// Early reliable message - we must check if it's already been received
				//
				// DeliveryMethod is ReliableUnordered or ReliableOrdered here
				//

				// get bools list we must check
				NetBitVector recList = m_reliableReceived[reliableSlot];
				if (recList == null)
				{
					recList = new NetBitVector(NetConstants.NumSequenceNumbers);
					m_reliableReceived[reliableSlot] = recList;
				}

				if (recList[channelSequenceNumber])
				{
					// Reject duplicate
					//m_statistics.CountDuplicateMessage(msg);
					m_owner.LogVerbose("Rejecting duplicate reliable " + ndm.ToString() + channelSequenceNumber.ToString());
					return;
				}

				// It's an early reliable message
				recList[channelSequenceNumber] = true;

				m_owner.LogVerbose("Received early reliable message: " + channelSequenceNumber);

				//
				// It's not a duplicate; mark as received. Release if it's unordered, else withhold
				//

				if (ndm == NetDeliveryMethod.ReliableUnordered)
				{
					AcceptMessage(mtp, isFragment, channelSequenceNumber, ptr, payloadLength);
					return;
				}

				//
				// Only ReliableOrdered left here; withhold it
				//

				// Early ordered message; withhold
				const int orderedSlotsStart = ((int)NetMessageType.UserReliableOrdered - (int)NetMessageType.UserReliableUnordered);
				int orderedSlot = reliableSlot - orderedSlotsStart;

				List<NetIncomingMessage> wmList = m_withheldMessages[orderedSlot];
				if (wmList == null)
				{
					wmList = new List<NetIncomingMessage>();
					m_withheldMessages[orderedSlot] = wmList;
				}

				// create message
				NetIncomingMessage im = m_owner.CreateIncomingMessage(NetIncomingMessageType.Data, m_owner.m_receiveBuffer, ptr, payloadLength);
				im.m_messageType = mtp;
				im.m_sequenceNumber = channelSequenceNumber;
				im.m_senderConnection = this;
				im.m_senderEndpoint = m_remoteEndpoint;

				m_owner.LogVerbose("Withholding " + im + " (waiting for " + m_nextExpectedReliableSequence[reliableSlot] + ")");
				
				wmList.Add(im);

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

		private void AcceptMessage(NetMessageType mtp, bool isFragment, ushort seqNr, int ptr, int payloadLength)
		{
			byte[] buffer = m_owner.m_receiveBuffer;

			if (isFragment)
			{
				int fragmentGroup = buffer[ptr++] | (buffer[ptr++] << 8);
				int fragmentTotalCount = buffer[ptr++] | (buffer[ptr++] << 8);
				int fragmentNr = buffer[ptr++] | (buffer[ptr++] << 8);

				throw new NotImplementedException("Fragmentation still unfinished");
			}

			// release to application
			NetIncomingMessage im = m_owner.CreateIncomingMessage(NetIncomingMessageType.Data, buffer, ptr, payloadLength);
			im.m_messageType = mtp;
			im.m_sequenceNumber = seqNr;
			im.m_senderConnection = this;
			im.m_senderEndpoint = m_remoteEndpoint;

			m_owner.LogVerbose("Releasing " + im);
			m_owner.ReleaseMessage(im);
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
						HandleIncomingPong(m_owner.m_receiveBuffer[ptr]);
					else
						m_owner.LogWarning("Received malformed pong");
					break;
				case NetMessageLibraryType.Acknowledge:
					HandleIncomingAcks(ptr, payloadLength);
					break;
				default:
					throw new NotImplementedException("Unhandled library type: " + libType);
			}

			return;
		}

		public void SendMessage(NetOutgoingMessage msg, NetDeliveryMethod method)
		{
			if (msg.IsSent)
				throw new NetException("Message has already been sent!");
			msg.m_type = (NetMessageType)method;
			EnqueueOutgoingMessage(msg);
		}

		public void SendMessage(NetOutgoingMessage msg, NetDeliveryMethod method, int sequenceChannel)
		{
			if (msg.IsSent)
				throw new NetException("Message has already been sent!");
			msg.m_type = (NetMessageType)((int)method + sequenceChannel);
			EnqueueOutgoingMessage(msg);
		}

		// called by user and network thread
		internal void EnqueueOutgoingMessage(NetOutgoingMessage msg)
		{
			int msgLen = msg.LengthBytes;
			int mtu = m_owner.m_configuration.m_maximumTransmissionUnit;

			if (msgLen <= mtu)
			{
				Interlocked.Increment(ref msg.m_inQueueCount);
				m_unsentMessages.Enqueue(msg);
				return;
			}

			mtu -= NetConstants.FragmentHeaderSize; // size of fragmentation info

			// message must be fragmented
			int fgi = Interlocked.Increment(ref m_nextFragmentGroupId);

			int numFragments = (msgLen + mtu - 1) / mtu;

			for(int i=0;i<numFragments;i++)
			{
				int flen = (i == numFragments - 1 ? (msgLen - (mtu * (numFragments - 1))) : mtu);

				NetOutgoingMessage fm = m_owner.CreateMessage(flen);
				fm.m_fragmentGroupId = fgi;
				fm.m_fragmentNumber = i;
				fm.m_fragmentTotalCount = numFragments;

				fm.Write(msg.m_data, mtu * i, flen);
				fm.m_type = msg.m_type;
				Interlocked.Increment(ref fm.m_inQueueCount);
				m_unsentMessages.Enqueue(fm);
			}
		}

		public void Disconnect(string byeMessage)
		{
			// called on user thread
			if (m_status == NetConnectionStatus.None || m_status == NetConnectionStatus.Disconnected)
				return;

			m_owner.LogVerbose("Disconnect requested for " + this);
			m_disconnectByeMessage = byeMessage;

			// loosen up throttling
			m_throttleDebt = -m_owner.m_configuration.m_throttlePeakBytes;

			// shorten resend times
			for (int i = 0; i < m_storedMessages.Length; i++)
			{
				List<NetOutgoingMessage> list = m_storedMessages[i];
				if (list != null)
				{
					try
					{
						foreach (NetOutgoingMessage om in list)
							om.m_nextResendTime = (om.m_nextResendTime * 0.8) - 0.05;
					}
					catch (InvalidOperationException)
					{
						// ok, collection was modified, never mind then
					}
				}
			}

			NetOutgoingMessage bye = m_owner.CreateLibraryMessage(NetMessageLibraryType.Disconnect, byeMessage);
			EnqueueOutgoingMessage(bye);
		}

		public void Approve()
		{
			if (!m_peerConfiguration.IsMessageTypeEnabled(NetIncomingMessageType.ConnectionApproval))
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
			if (!m_peerConfiguration.IsMessageTypeEnabled(NetIncomingMessageType.ConnectionApproval))
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
			return "[NetConnection to " + m_remoteEndpoint + "]";
		}
	}
}
