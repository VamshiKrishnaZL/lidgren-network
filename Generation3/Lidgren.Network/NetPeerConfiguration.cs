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
using System.Net;

namespace Lidgren.Network
{
	/// <summary>
	/// Partly immutable after NetPeer has been initialized
	/// </summary>
	public sealed class NetPeerConfiguration
	{
		private const string c_isLockedMessage = "You may not alter the NetPeerConfiguration after the NetPeer has been initialized!";

		private bool m_isLocked;
		internal bool m_acceptIncomingConnections;
		internal string m_appIdentifier;
		internal IPAddress m_localAddress;
		internal int m_port;
		internal int m_receiveBufferSize, m_sendBufferSize;
		internal int m_defaultOutgoingMessageCapacity;
		internal int m_maximumTransmissionUnit;
		internal float m_keepAliveDelay;
		internal float m_connectionTimeOut;
		internal int m_maximumConnections;
		internal NetIncomingMessageType m_disabledTypes;
		internal float m_pingFrequency;
		internal float m_maxDelayedMessageDuration;
		internal int m_throttleBytesPerSecond;
		internal int m_throttlePeakDivider;

		// reliability
		internal float[] m_resendRTTMultiplier;
		internal float[] m_resendBaseTime;
		internal float m_maxAckDelayTime;

		// bad network simulation
		internal float m_loss;
		internal float m_duplicates;
		internal float m_minimumOneWayLatency;
		internal float m_randomOneWayLatency;

		public NetPeerConfiguration(string appIdentifier)
		{
			if (string.IsNullOrEmpty(appIdentifier))
				throw new NetException("App identifier must be a string of at least one characters length");
			m_appIdentifier = appIdentifier;

			// defaults
			m_isLocked = false;
			m_acceptIncomingConnections = true;
			m_localAddress = IPAddress.Any;
			m_port = 0;
			m_receiveBufferSize = 131071;
			m_sendBufferSize = 131071;
			m_keepAliveDelay = 7.0f;
			m_connectionTimeOut = 25;
			m_maximumConnections = 8;
			m_defaultOutgoingMessageCapacity = 8;
			m_pingFrequency = 6.0f;
			m_maxDelayedMessageDuration = 0.25f;
			m_throttleBytesPerSecond = 1024 * 256;
			m_throttlePeakDivider = 16;
			m_maxAckDelayTime = 0.01f;

			m_loss = 0.0f;
			m_minimumOneWayLatency = 0.0f;
			m_randomOneWayLatency = 0.0f;

			// default disabled types
			m_disabledTypes = NetIncomingMessageType.ConnectionApproval | NetIncomingMessageType.UnconnectedData | NetIncomingMessageType.VerboseDebugMessage;

			// reliability
			m_resendRTTMultiplier = new float[]
			{
				1.0f,
				2.25f,
				3.5f,
				4.0f,
				4.0f,
				4.0f,
				4.0f,
				4.0f,
				4.0f
			};

			m_resendBaseTime = new float[]
			{
				0.025f, // just processing time + ack delay wait time
				0.045f, // just processing time + ack delay wait time
				0.2f, // 0.16 delay since last resend
				0.5f, // 0.3 delay 
				1.5f, // 1.0 delay
				3.0f, // 1.5 delay
				5.0f, // 2.0 delay
				7.5f, // 2.5 delay
				12.5f // 5.0 delay, obi wan you're my last hope
			};

			// Maximum transmission unit
			// The aim is for a max full packet to be 1440 bytes (30 x 48 bytes, lower than 1468)
			// 20 bytes ip header
			//  8 bytes udp header
			//  5 bytes lidgren header for one message
			// Totals 1440 minus 33 = 1407 bytes free for payload
			m_maximumTransmissionUnit = 1407;
		}

		public NetPeerConfiguration Clone()
		{
			NetPeerConfiguration retval = this.MemberwiseClone() as NetPeerConfiguration;
			retval.m_isLocked = false;
			return retval;
		}

		internal void VerifyAndLock()
		{
			// TODO: verify configuration here
			m_isLocked = true;
		}

#if DEBUG
		/// <summary>
		/// Gets or sets the simulated amount of sent packets lost from 0.0f to 1.0f
		/// </summary>
		public float SimulatedLoss
		{
			get { return m_loss; }
			set { m_loss = value; }
		}

		/// <summary>
		/// Gets or sets the minimum simulated amount of one way latency for sent packets in seconds
		/// </summary>
		public float SimulatedMinimumLatency
		{
			get { return m_minimumOneWayLatency; }
			set { m_minimumOneWayLatency = value; }
		}

		/// <summary>
		/// Gets or sets the simulated added random amount of one way latency for sent packets in seconds
		/// </summary>
		public float SimulatedRandomLatency
		{
			get { return m_randomOneWayLatency; }
			set { m_randomOneWayLatency = value; }
		}

		/// <summary>
		/// Gets the average simulated one way latency in seconds
		/// </summary>
		public float SimulatedAverageLatency
		{
			get { return m_minimumOneWayLatency + (m_randomOneWayLatency * 0.5f); }
		}

		/// <summary>
		/// Gets or sets the simulated amount of duplicated packets from 0.0f to 1.0f
		/// </summary>
		public float SimulatedDuplicatesChance
		{
			get { return m_duplicates; }
			set { m_duplicates = value; }
		}

#endif

		/// <summary>
		/// Gets or sets the identifier of this application; the library can only connect to matching app identifier peers
		/// </summary>
		public string AppIdentifier
		{
			get { return m_appIdentifier; }
		}

		/// <summary>
		/// Enables receiving of the specified type of message
		/// </summary>
		public void EnableMessageType(NetIncomingMessageType tp)
		{
			m_disabledTypes &= (~tp);
		}

		/// <summary>
		/// Disables receiving of the specified type of message
		/// </summary>
		public void DisableMessageType(NetIncomingMessageType tp)
		{
			m_disabledTypes |= tp;
		}

		/// <summary>
		/// Enables or disables receiving of the specified type of message
		/// </summary>
		public void SetMessageTypeEnabled(NetIncomingMessageType tp, bool enabled)
		{
			if (enabled)
				m_disabledTypes &= (~tp);
			else
				m_disabledTypes |= tp;
		}

		/// <summary>
		/// Gets if receiving of the specified type of message is enabled
		/// </summary>
		public bool IsMessageTypeEnabled(NetIncomingMessageType tp)
		{
			return !((m_disabledTypes & tp) == tp);
		}

		/// <summary>
		/// Gets or sets the maximum amount of bytes to send in a single packet
		/// </summary>
		public int MaximumTransmissionUnit
		{
			get { return m_maximumTransmissionUnit; }
			set { m_maximumTransmissionUnit = value; }
		}

		/// <summary>
		/// Gets or sets the maximum amount of connections this peer can hold. Cannot be changed once NetPeer is initialized.
		/// </summary>
		public int MaximumConnections
		{
			get { return m_maximumTransmissionUnit; }
			set
			{
				if (m_isLocked)
					throw new NetException(c_isLockedMessage);
				m_maximumTransmissionUnit = value;
			}
		}

		/// <summary>
		/// Gets or sets if the NetPeer should accept incoming connections
		/// </summary>
		public bool AcceptIncomingConnections
		{
			get { return m_acceptIncomingConnections; }
			set { m_acceptIncomingConnections = value; }
		}

		/// <summary>
		/// Gets or sets the default capacity in bytes when NetPeer.CreateMessage() is called without argument
		/// </summary>
		public int DefaultOutgoingMessageCapacity
		{
			get { return m_defaultOutgoingMessageCapacity; }
			set { m_defaultOutgoingMessageCapacity = value; }
		}

		/// <summary>
		/// Gets or sets the local ip address to bind to. Defaults to IPAddress.Any. Cannot be changed once NetPeer is initialized.
		/// </summary>
		public IPAddress LocalAddress
		{
			get { return m_localAddress; }
			set
			{
				if (m_isLocked)
					throw new NetException(c_isLockedMessage);
				m_localAddress = value;
			}
		}

		/// <summary>
		/// Gets or sets the local port to bind to. Defaults to 0. Cannot be changed once NetPeer is initialized.
		/// </summary>
		public int Port
		{
			get { return m_port; }
			set
			{
				if (m_isLocked)
					throw new NetException(c_isLockedMessage);
				m_port = value;
			}
		}

		/// <summary>
		/// Gets or sets the size in bytes of the receiving buffer. Defaults to 131071 bytes. Cannot be changed once NetPeer is initialized.
		/// </summary>
		public int ReceiveBufferSize
		{
			get { return m_receiveBufferSize; }
			set
			{
				if (m_isLocked)
					throw new NetException(c_isLockedMessage);
				m_receiveBufferSize = value;
			}
		}

		/// <summary>
		/// Gets or sets the size in bytes of the sending buffer. Defaults to 131071 bytes. Cannot be changed once NetPeer is initialized.
		/// </summary>
		public int SendBufferSize
		{
			get { return m_sendBufferSize; }
			set
			{
				if (m_isLocked)
					throw new NetException(c_isLockedMessage);
				m_sendBufferSize = value;
			}
		}

		/// <summary>
		/// Gets or sets the number of seconds of inactivity before sending a dummy keepalive packet. This should be longer than ping interval.
		/// </summary>
		public float KeepAliveDelay
		{
			get { return m_keepAliveDelay; }
			set
			{
				m_keepAliveDelay = value;
			}
		}

		/// <summary>
		/// Gets or sets the number of seconds of non-response before disconnecting because of time out. Cannot be changed once NetPeer is initialized.
		/// </summary>
		public float ConnectionTimeOut
		{
			get { return m_connectionTimeOut; }
			set
			{
				if (m_isLocked)
					throw new NetException(c_isLockedMessage);
				m_connectionTimeOut = value;
			}
		}

		/// <summary>
		/// Gets or sets the maximum number of seconds to wait before forcing send of NetMessagePriority.Delayed messages
		/// </summary>
		public float MaxDelayedMessageDuration
		{
			get { return m_maxDelayedMessageDuration; }
			set { m_maxDelayedMessageDuration = value; }
		}

		/// <summary>
		/// Gets or sets the number of seconds between latency calculation (rtt) pings
		/// </summary>
		public float PingFrequency
		{
			get { return m_pingFrequency; }
			set { m_pingFrequency = value; }
		}

		/// <summary>
		/// Gets or sets the number of allowed bytes to be sent per second per connection; 0 means unlimited
		/// </summary>
		public int ThrottleBytesPerSecond
		{
			get { return m_throttleBytesPerSecond; }
			set { m_throttleBytesPerSecond = value; }
		}
	}
}
