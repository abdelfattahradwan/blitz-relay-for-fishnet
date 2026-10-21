using BlitzRelay.FishNet.LiteNetLib;
using FishNet.Transporting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BlitzRelay.FishNet
{
	/// <summary>
	/// Abstract base for client and host relay sockets. Handles LiteNetLib NetManager
	/// lifecycle and state propagation to Fish-Networking.
	/// </summary>
	public abstract class SocketBase
	{
		/// <summary>
		/// The address and port of the relay server.
		/// </summary>
		protected string RelayAddress;

		/// <summary>
		/// The port of the relay server.
		/// </summary>
		protected ushort RelayPort;

		/// <summary>
		/// The connection key used to authenticate with the relay server.
		/// </summary>
		protected string RelayKey;

		/// <summary>
		/// The code of the room to join on the relay server.
		/// </summary>
		protected string RoomCode;

		/// <summary>
		/// The timeout duration, in milliseconds, for disconnecting idle connections. <see cref="LiteNetManager.DisconnectTimeout"/>
		/// </summary>
		protected int DisconnectTimeoutMilliseconds = 5000;

		/// <summary>
		/// MTU size per packet.
		/// </summary>
		protected int Mtu;

		/// <summary>
		/// The LiteNetLib peer that connects to the relay server.
		/// </summary>
		protected NetPeer RelayPeer;

		/// <summary>
		/// Queue of incoming packets.
		/// </summary>
		protected readonly ConcurrentQueue<DataPacket> IncomingPackets = new();

		/// <summary>
		/// Queue of outgoing packets.
		/// </summary>
		protected readonly Queue<DataPacket> OutgoingPackets = new();

		/// <summary>
		/// The parent Blitz Relay transport instance.
		/// </summary>
		protected BlitzRelayTransport Transport;

		/// <summary>
		/// Queue of connection state changes to be processed on the main thread.
		/// </summary>
		protected readonly ConcurrentQueue<LocalConnectionState> ConnectionStateChanges = new();

		/// <summary>
		/// True while <see cref="PollSocket"/> is dispatching LiteNetLib events.
		/// </summary>
		private bool _isPolling;

		/// <summary>
		/// The LiteNetLib NetManager instance. Null when the socket is stopped.
		/// </summary>
		public NetManager SocketNetManager { get; protected set; }

		/// <summary>
		/// Current state of this socket.
		/// </summary>
		public LocalConnectionState SocketConnectionState { get; private set; } = LocalConnectionState.Stopped;

		/// <summary>
		/// Initialises the socket.
		/// </summary>
		internal void Initialize(Transport transport, int unreliableMtu)
		{
			if (transport is not BlitzRelayTransport blitzRelay) throw new ArgumentException("Transport must be Blitz Relay.", nameof(transport));

			Transport = blitzRelay;

			Mtu = unreliableMtu;
		}

		/// <summary>
		/// Updates the LiteNetLib disconnect timeout. A value less than or equal to 0 uses the maximum possible timeout.
		/// </summary>
		internal void UpdateTimeout(int timeoutMilliseconds)
		{
			DisconnectTimeoutMilliseconds = timeoutMilliseconds <= 0 ? int.MaxValue : timeoutMilliseconds;

			if (SocketNetManager != null) SocketNetManager.DisconnectTimeout = DisconnectTimeoutMilliseconds;
		}

		/// <summary>
		/// Polls the LiteNetLib socket for events. Must be called every frame. A socket stopped
		/// by an event handler is stopped after polling completes.
		/// </summary>
		internal void PollSocket()
		{
			NetManager netManager = SocketNetManager;

			if (netManager == null) return;

			_isPolling = true;

			try
			{
				netManager.PollEvents();
			}
			finally
			{
				_isPolling = false;
			}

			if (SocketNetManager != netManager) netManager.Stop();
		}

		/// <summary>
		/// Updates the socket's connection state and notifies Fish-Networking.
		/// </summary>
		protected void SetConnectionState(LocalConnectionState newConnectionState, bool server)
		{
			if (SocketConnectionState == newConnectionState) return;

			SocketConnectionState = newConnectionState;

			if (server)
			{
				ServerConnectionStateArgs serverConnectionStateArgs = new(newConnectionState, Transport.Index);

				Transport.HandleServerConnectionState(serverConnectionStateArgs);
			}
			else
			{
				ClientConnectionStateArgs clientConnectionStateArgs = new(newConnectionState, Transport.Index);

				Transport.HandleClientConnectionState(clientConnectionStateArgs);
			}
		}

		/// <summary>
		/// Enqueues a packet for delivery. Silently ignored if the socket is not in the
		/// Started state.
		/// </summary>
		protected void Send(Queue<DataPacket> queue, byte channelId, ArraySegment<byte> segment, int connectionId, int mtu)
		{
			if (SocketConnectionState != LocalConnectionState.Started) return;

			DataPacket outgoingDataPacket = new(connectionId, segment, channelId);

			queue.Enqueue(outgoingDataPacket);
		}

		/// <summary>
		/// Clears all queued incoming and outgoing packets, disposing them, and resets the
		/// state changes queue.
		/// </summary>
		protected virtual void ClearQueues()
		{
			while (IncomingPackets.TryDequeue(out DataPacket packet))
			{
				packet.Dispose();
			}

			while (OutgoingPackets.TryDequeue(out DataPacket packet))
			{
				packet.Dispose();
			}

			while (ConnectionStateChanges.TryDequeue(out _))
			{
				//  ConcurrentQueue<T>.Clear allocates.
			}
		}

		/// <summary>
		/// Stops the LiteNetLib socket and resets the state.
		/// </summary>
		protected void StopSocket()
		{
			if (SocketNetManager == null) return;

			if (!_isPolling) SocketNetManager.Stop();

			SocketNetManager = null;

			if (SocketConnectionState != LocalConnectionState.Stopped) ConnectionStateChanges.Enqueue(LocalConnectionState.Stopped);
		}
	}
}
