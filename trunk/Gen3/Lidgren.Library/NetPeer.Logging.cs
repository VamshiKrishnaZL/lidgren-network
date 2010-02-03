﻿using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lidgren.Network
{
	public partial class NetPeer
	{
		[Conditional("DEBUG")]
		internal void LogVerbose(string message)
		{
			if (m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.VerboseDebugMessage))
				ReleaseMessage(CreateIncomingMessage(NetIncomingMessageType.VerboseDebugMessage, message));
		}

		[Conditional("DEBUG")]
		internal void LogDebug(string message)
		{
			if (m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.DebugMessage))
				ReleaseMessage(CreateIncomingMessage(NetIncomingMessageType.DebugMessage, message));
		}

		internal void LogWarning(string message)
		{
			if (m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.WarningMessage))
				ReleaseMessage(CreateIncomingMessage(NetIncomingMessageType.WarningMessage, message));
		}

		internal void LogError(string message)
		{
			if (m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.ErrorMessage))
				ReleaseMessage(CreateIncomingMessage(NetIncomingMessageType.ErrorMessage, message));
		}
	}
}
