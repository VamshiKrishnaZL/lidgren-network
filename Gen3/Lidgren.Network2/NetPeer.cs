﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.Net.Sockets;

namespace Lidgren.Network2
{
	//
	// This partial file holds public netpeer methods accessible to the application
	//
	public partial class NetPeer
	{
		private bool m_isInitialized;
		private bool m_initiateShutdown;
		private object m_initializeLock = new object();
		
		internal NetPeerConfiguration m_configuration;
		private NetPeerStatistics m_statistics;
		private Thread m_networkThread;

		protected List<NetConnection> m_connections;
		private Dictionary<IPEndPoint, NetConnection> m_connectionLookup;
		
		/// <summary>
		/// Gets a copy of the list of connections
		/// </summary>
		public List<NetConnection> Connections
		{
			get
			{
				lock (m_connections)
				{
					return new List<NetConnection>(m_connections);
				}
			}
		}

		/// <summary>
		/// Returns the number of active connections
		/// </summary>
		public int ConnectionsCount
		{
			get { return m_connections.Count; }
		}

		public NetPeer(NetPeerConfiguration configuration)
		{
			m_configuration = configuration;
			m_connections = new List<NetConnection>();
			m_connectionLookup = new Dictionary<IPEndPoint, NetConnection>();
			m_senderRemote = (EndPoint)new IPEndPoint(IPAddress.Any, 0);
			m_statistics = new NetPeerStatistics();

			// NetPeer.Recycling stuff
			m_storagePool = new List<byte[]>();
			m_incomingMessagesPool = new Queue<NetIncomingMessage>();

			// NetPeer.Internal stuff
			m_releasedIncomingMessages = new Queue<NetIncomingMessage>();
			m_unsentUnconnectedMessage = new Queue<NetOutgoingMessage>();
			m_unsentUnconnectedRecipients = new Queue<IPEndPoint>();
		}

		/// <summary>
		/// Binds to socket
		/// </summary>
		public void Start()
		{
			InternalStart();

			// start network thread
			m_networkThread = new Thread(new ThreadStart(Run));
			m_networkThread.Name = "Lidgren network thread";
			m_networkThread.IsBackground = true;
			m_networkThread.Start();

			// allow some time for network thread to start up in case they call Connect() immediately
			Thread.Sleep(3);
		}

		internal void SendPacket(int numBytes, IPEndPoint target)
		{
			try
			{
#if DEBUG
				ushort packetNumber = (ushort)(m_sendBuffer[0] | (m_sendBuffer[1] << 8));
				LogVerbose("Sending packet P#" + packetNumber + " (" + numBytes + " bytes)");
#endif

				// TODO: Use SendToAsync()?
				int bytesSent = m_socket.SendTo(m_sendBuffer, 0, numBytes, SocketFlags.None, target);
				if (numBytes != bytesSent)
					LogWarning("Failed to send the full " + numBytes + "; only " + bytesSent + " bytes sent in packet!");

				if (bytesSent >= 0)
				{
					m_statistics.m_sentPackets++;
					m_statistics.m_sentBytes += bytesSent;
				}
			}
			catch (Exception ex)
			{
				LogError("Failed to send packet: " + ex);
			}
		}

		public void SendMessage(NetOutgoingMessage msg, NetConnection recipient, NetMessageChannel channel, NetMessagePriority priority)
		{
			if (msg.IsSent)
				throw new NetException("Message has already been sent!");
			msg.m_type = (NetMessageType)channel;
			recipient.EnqueueOutgoingMessage(msg, priority);
		}

		public void SendMessage(NetOutgoingMessage msg, IEnumerable<NetConnection> recipients, NetMessageChannel channel, NetMessagePriority priority)
		{
			if (msg.IsSent)
				throw new NetException("Message has already been sent!");
			msg.m_type = (NetMessageType)channel;
			foreach (NetConnection conn in recipients)
				conn.EnqueueOutgoingMessage(msg, priority);
		}

		public void SendUnconnectedMessage(NetOutgoingMessage msg, IPEndPoint recipient)
		{
			if (msg.IsSent)
				throw new NetException("Message has already been sent!");
			EnqueueUnconnectedMessage(msg, recipient);
		}

		public void SendUnconnectedMessage(NetOutgoingMessage msg, IEnumerable<IPEndPoint> recipients)
		{
			if (msg.IsSent)
				throw new NetException("Message has already been sent!");
			foreach (IPEndPoint ipe in recipients)
				EnqueueUnconnectedMessage(msg, ipe);
		}

		/// <summary>
		/// Read a pending message from any connection, if any
		/// </summary>
		public NetIncomingMessage ReadMessage()
		{
			if (m_releasedIncomingMessages.Count < 1)
				return null;

			lock (m_releasedIncomingMessages)
			{
				if (m_releasedIncomingMessages.Count < 1)
					return null;
				return m_releasedIncomingMessages.Dequeue();
			}
		}

		/// <summary>
		/// Create a connection to a remote endpoint
		/// </summary>
		public NetConnection Connect(string host, int port)
		{
			return Connect(new IPEndPoint(NetUtility.Resolve(host), port));
		}

		/// <summary>
		/// Create a connection to a remote endpoint
		/// </summary>
		public virtual NetConnection Connect(IPEndPoint remoteEndPoint)
		{
			if (!m_isInitialized)
				throw new NetException("Must call Start() first");

			if (m_connectionLookup.ContainsKey(remoteEndPoint))
				throw new NetException("Already connected to that endpoint!");

			NetConnection conn = new NetConnection(this, remoteEndPoint);

			// handle on network thread
			conn.m_connectRequested = true;
			conn.m_connectionInitiator = true;

			lock (m_connections)
			{
				m_connections.Add(conn);
				m_connectionLookup[remoteEndPoint] = conn;
			}

			return conn;
		}

		[System.Diagnostics.Conditional("DEBUG")]
		internal void VerifyNetworkThread()
		{
			if (System.Threading.Thread.CurrentThread != m_networkThread)
				throw new NetException("Executing on wrong thread! Should be library system thread!");
		}

		public void Shutdown(string bye)
		{
			// called on user thread

			if (m_socket == null)
				return; // already shut down

			LogDebug("Shutdown requested");

			// disconnect all connections
			lock (m_connections)
			{
				foreach (NetConnection conn in m_connections)
					conn.Disconnect(bye);
			}

			m_initiateShutdown = true;
		}
	}
}
