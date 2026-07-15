using System;
using System.Buffers;

namespace BlitzRelay.FishNet
{
	/// <summary>
	/// Immutable game-data packet with optional pooled backing buffer. Implements
	/// IDisposable.
	/// </summary>
	public readonly struct DataPacket : IDisposable
	{
		/// <summary>
		/// The virtual connection ID of the sender or target.
		/// </summary>
		public readonly int ConnectionId;

		/// <summary>
		/// True if the backing buffer was rented from the array pool.
		/// </summary>
		private readonly bool _isPooled;

		/// <summary>
		/// The backing byte array. May be pooled.
		/// </summary>
		private readonly byte[] _data;

		/// <summary>
		/// Number of bytes in use. Maybe less than the backing array length.
		/// </summary>
		private readonly int _length;

		/// <summary>
		/// The Fish-Networking channel this packet was received on or should be sent on.
		/// </summary>
		public readonly byte ChannelId;

		private DataPacket(int connectionId, bool isPooled, byte[] data, int length, byte channelId)
		{
			ConnectionId = connectionId;

			_isPooled = isPooled;

			_data = data;

			_length = length;

			ChannelId = channelId;
		}

		/// <summary>
		/// Creates a non-pooled packet from an owned byte array.
		/// </summary>
		public DataPacket(int connectionId, byte[] data, int length, byte channelId) : this(connectionId, false, data, length, channelId) { }

		/// <summary>
		/// Creates a pooled packet by renting an array from the shared pool and copying
		/// the span into it.
		/// </summary>
		public DataPacket(int connectionId, Span<byte> span, byte channelId)
		{
			ConnectionId = connectionId;

			_isPooled = true;

			_data = ArrayPool<byte>.Shared.Rent(span.Length);

			span.CopyTo(_data);

			_length = span.Length;

			ChannelId = channelId;
		}

		/// <summary>
		/// Wraps an already-rented buffer without copying it.
		/// </summary>
		/// <remarks>
		/// Use this when the buffer was rented elsewhere and this packet should return it
		/// to the pool from <see cref="Dispose"/>.
		/// </remarks>
		/// <param name="connectionId">The virtual connection ID of the sender or target.</param>
		/// <param name="data">The rented buffer whose ownership is transferred to the packet.</param>
		/// <param name="length">The number of valid bytes in <paramref name="data"/>.</param>
		/// <param name="channelId">The channel associated with this packet.</param>
		/// <returns>A packet backed by <paramref name="data"/>.</returns>
		internal static DataPacket TakeRentedBuffer(int connectionId, byte[] data, int length, byte channelId)
		{
			return new DataPacket(connectionId, true, data, length, channelId);
		}

		/// <summary>
		/// Returns the packet data as an ArraySegment.
		/// </summary>
		public ArraySegment<byte> ToArraySegment()
		{
			return new ArraySegment<byte>(_data, 0, _length);
		}

		/// <summary>
		/// Returns the backing array to the pool if it was pooled; otherwise a no-op.
		/// </summary>
		public void Dispose()
		{
			if (_isPooled) ArrayPool<byte>.Shared.Return(_data);
		}
	}
}
