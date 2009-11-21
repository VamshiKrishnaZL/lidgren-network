﻿using System;
using System.Collections.Generic;

namespace Lidgren.Network2
{
	public partial class NetPeer
	{
		internal void LogVerbose(string message)
		{
			NetIncomingMessage msg = CreateIncomingMessage(NetIncomingMessageType.VerboseDebugMessage, message.Length + (message.Length > 126 ? 2 : 1));
			msg.Write(message);
			ReleaseMessage(msg);
		}

		internal void LogDebug(string message)
		{
			NetIncomingMessage msg = CreateIncomingMessage(NetIncomingMessageType.DebugMessage, message.Length + (message.Length > 126 ? 2 : 1));
			msg.Write(message);
			ReleaseMessage(msg);
		}

		internal void LogWarning(string message)
		{
			NetIncomingMessage msg = CreateIncomingMessage(NetIncomingMessageType.WarningMessage, message.Length + (message.Length > 126 ? 2 : 1));
			msg.Write(message);
			ReleaseMessage(msg);
		}

		internal void LogError(string message)
		{
			NetIncomingMessage msg = CreateIncomingMessage(NetIncomingMessageType.ErrorMessage, message.Length + (message.Length > 126 ? 2 : 1));
			msg.Write(message);
			ReleaseMessage(msg);
		}
	}
}
