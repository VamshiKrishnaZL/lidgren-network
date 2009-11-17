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
		private Thread m_networkThread;

		protected List<NetConnection> m_connections;
		private Dictionary<IPEndPoint, NetConnection> m_connectionLookup;

		public NetPeer(NetPeerConfiguration configuration)
		{
			m_configuration = configuration;
			m_connections = new List<NetConnection>();
			m_connectionLookup = new Dictionary<IPEndPoint, NetConnection>();
			m_senderRemote = (EndPoint)new IPEndPoint(IPAddress.Any, 0);

			SetupInternal();
		}

		/// <summary>
		/// Binds to socket
		/// </summary>
		public void Initialize()
		{
			lock (m_initializeLock)
			{
				if (m_isInitialized)
					return;
				m_configuration.Lock();

				// bind to socket
				IPEndPoint iep = null;
				try
				{
					iep = new IPEndPoint(m_configuration.LocalAddress, m_configuration.Port);
					EndPoint ep = (EndPoint)iep;

					m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
					m_socket.Blocking = false;
					m_socket.Bind(ep);

					m_receiveBuffer = new byte[m_configuration.ReceiveBufferSize];
					m_sendBuffer = new byte[m_configuration.SendBufferSize];

					// start network thread
					m_networkThread = new Thread(new ThreadStart(Run));
					m_networkThread.Name = "Lidgren network thread";
					m_networkThread.IsBackground = true;
					m_networkThread.Start();

					// only set initialized if everything succeeds
					m_isInitialized = true;
				}
				catch (SocketException sex)
				{
					if (sex.SocketErrorCode == SocketError.AddressAlreadyInUse)
						throw new NetException("Failed to bind to port " + (iep == null ? "Null" : iep.ToString()) + " - Address already in use!", sex);
					throw;
				}
				catch (Exception ex)
				{
					throw new NetException("Failed to bind to " + (iep == null ? "Null" : iep.ToString()), ex);
				}
			}
		}

		internal void SendPacket(int numBytes, IPEndPoint target)
		{
			int bytesSent = m_socket.SendTo(m_sendBuffer, 0, numBytes, SocketFlags.None, target);
			if (numBytes != bytesSent)
				LogWarning("Failed to send the full " + numBytes + "; only " + bytesSent + " bytes sent in packet!");

			// TODO: add to statistics
		}

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
		/// 
		/// </summary>
		public void Shutdown()
		{
			if (m_socket == null)
				return; // already shut down
			LogDebug("Shutdown requested");
			m_initiateShutdown = true;
		}
	}
}
