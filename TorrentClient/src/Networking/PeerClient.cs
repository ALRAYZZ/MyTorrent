using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using TorrentClient.src.Models;

namespace TorrentClient.src.Networking
{
	public class PeerClient
	{
		private readonly string peerId;
		private TcpClient tcpClient;
		private NetworkStream stream;

		public PeerClient(string peerId)
		{
			this.peerId = peerId ?? throw new ArgumentNullException(nameof(peerId));
			if (peerId.Length != 20)
			{
				throw new ArgumentException("Peer ID must be exactly 20 bytes long.", nameof(peerId));
			}
		}

		// Connect to a peer using the peer ID and initiate a handshake
		// Returns a tuple containing the peer's IP, port, and peer ID after successful handshake
		public async Task<(string ip, int port, string peerId)> ConnectAndHandshake(Torrent torrent, string ip, int port)
		{
			tcpClient = new TcpClient();
			// Connect to the peer's IP and port
			await tcpClient.ConnectAsync(ip, port);
			stream = tcpClient.GetStream();

			// Build and send the handshake message
			byte[] handshake = BuildHandshake(torrent.InfoHash, peerId);
			await stream.WriteAsync(handshake, 0, handshake.Length);

			// Receive handshake response
			byte[] response = new byte[68];
			int bytesRead = await stream.ReadAsync(response, 0, response.Length);

			// Validation checks on the response
			if (bytesRead < 68)
			{
				throw new FormatException($"Handshake response is too short: {bytesRead} bytes.");
			}

			// Verify protocol and info hash
			if (response[0] != 19 || Encoding.ASCII.GetString(response, 1, 19) != "BitTorrent protocol")
			{
				throw new FormatException("Invalid protocol in handshake respponse.");
			}

			byte[] responseInfoHash = new byte[20];
			Array.Copy(response, 28, responseInfoHash, 0, 20);
			if (!responseInfoHash.AsSpan().SequenceEqual(torrent.InfoHash))
			{
				throw new FormatException("Info hash mismatch in handshake response.");
			}

			// Extract peer ID from response
			byte[] responsePeerId = new byte[20];
			Array.Copy(response, 48, responsePeerId, 0, 20);
			string peerIdStr = Encoding.ASCII.GetString(responsePeerId);

			return (ip, port, peerIdStr);
		}

		// Download the first piece from the peer
		public async Task<byte[]> DownloadPiece(Torrent torrent, int pieceIndex)
		{
			// Send interested message to the peer
			await SendMessage(2, null); // ID 2: Interested message

			// Receive messages until unchoke and piece, using the torrent's piece length
			// So we keep looping until the piece is fully downloaded
			byte[] pieceData = new byte[torrent.PieceLength];
			int pieceOffset = 0;

			while (pieceOffset < pieceData.Length)
			{
				var (messageId, payload) = await ReceiveMessage();
				if (messageId == 1) // Unchoke, meaning we can download the pieces.
				{
					// Request piece
					byte[] requestPayload = new byte[12];
					BitConverter.GetBytes(pieceIndex).CopyTo(requestPayload, 0);
					BitConverter.GetBytes(0).CopyTo(requestPayload, 4); // Offset
					BitConverter.GetBytes(torrent.PieceLength).CopyTo(requestPayload, 8); // Length
					await SendMessage(6, requestPayload); // ID 6: Request message
				}
				else if (messageId == 7) // Piece
				{
					// Extract piece data from payload
					int index = BitConverter.ToInt32(payload, 0);
					int begin = BitConverter.ToInt32(payload, 4);
					if (index != pieceIndex || begin != 0)
					{
						throw new FormatException("Unexpected piece index or offset");
					}
					Array.Copy(payload, 8, pieceData, pieceOffset, payload.Length - 8);
					pieceOffset += payload.Length - 8;

				}
			}
			return pieceData; // Return the downloaded piece data
		}

		// Send a BitTorrent message: <length><id><payload>
		private async Task SendMessage(byte id, byte[] payload)
		{
			int length = 1 + (payload?.Length ?? 0); // Length of the message (1 byte for ID + payload length)
			byte[] message = new byte[4 + length]; // 4 bytes for length + ID + payload
			BitConverter.GetBytes(length).CopyTo(message, 0); // Copy length to the first 4 bytes
			message[4] = id; // Set the message ID
			if (payload != null)
			{
				Array.Copy(payload, 0, message, 5, payload.Length); // Copy payload to the message
			}
			await stream.WriteAsync(message, 0, message.Length); // Send the message over the network stream
		}

		// Receive a BitTorrent message
		private async Task<(byte id, byte[] payload)> ReceiveMessage()
		{
			byte[] lengthBytes = new byte[4];
			await stream.ReadAsync(lengthBytes, 0, 4); // Read the first 4 bytes for length
			int length = BitConverter.ToInt32(lengthBytes, 0); // Get the length of the message
			if (length == 0)
			{
				return (0, null); // Keep-alive message, no payload
			}
			byte[] message = new byte[length];
			await stream.ReadAsync(message, 0, length); // Read the rest of the message
			byte id = message[0]; // The first byte is the message ID
			byte[] payload = length > 1 ? new byte[length - 1] : null; // The rest is the payload
			if (payload != null)
			{
				Array.Copy(message, 1, payload, 0, length - 1); // Copy the payload from the message
			}
			return (id, payload); // Return the message ID and payload
		}

		// Build handshake message according to the BitTorrent protocol: <pstrlen><pstr><reserved><info_hash><peer_id>
		private byte[] BuildHandshake(byte[] infoHash, string peerId)
		{
			var handshake = new byte[68];
			handshake[0] = 19; // Protocol string length (pstrlen)
			var protocol = Encoding.ASCII.GetBytes("BitTorrent protocol");
			Array.Copy(protocol, 0, handshake, 1, 19); // Protocol string (pstr)
			// Reserved bytes (8 bytes, all zero)
			Array.Copy(infoHash, 0, handshake, 28, 20); // info_hash
			Array.Copy(Encoding.ASCII.GetBytes(peerId), 0, handshake, 48, 20); // peer_id
			return handshake;
		}
	}
}
