using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using TorrentClient.FileManagement;
using TorrentClient.src.Models;

namespace TorrentClient.src.Networking
{
	public class PeerClient
	{
		private readonly string peerId;
		private TcpClient tcpClient;
		private NetworkStream stream;
		private readonly TimeSpan timeout = TimeSpan.FromSeconds(30); // Timeout for peer responses
		private BitArray peerBitfield; // Bitfield to track pieces available from the peer
		private bool isUnchoked; // Flag to check if we are unchoked by the peer

		public PeerClient(string peerId)
		{
			this.peerId = peerId ?? throw new ArgumentNullException(nameof(peerId));
			if (peerId.Length != 20)
			{
				throw new ArgumentException("Peer ID must be exactly 20 bytes long.", nameof(peerId));
			}
			isUnchoked = false; // Initially, we are not unchoked
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

			await ReceiveBitfield(torrent); // Receive and parse the bitfield message from the peer

			return (ip, port, peerIdStr);
		}

		// Receive and parse bitfield message from a peer
		private async Task ReceiveBitfield(Torrent torrent)
		{
			int pieceCount = torrent.PieceHashes.Length / 20; // Each piece hash is 20 bytes
			peerBitfield = new BitArray(pieceCount, false); // Initialize bitfield with all pieces not available

			// Expect bitfield message (ID 5) after handshake
			var (messageId, payload) = await ReceiveMessageWithTimeout();
			if (messageId != 5)
			{
				Console.WriteLine($"Expected bitfield message (ID 5), received ID {messageId}");
				return;
			}

			// Bitfield payload is a bit array where each bit represents a piece
			if (payload.Length < (pieceCount + 7) / 8) // Ensure payload is long enough to cover all pieces
			{
				throw new FormatException("Bitfield payload is too short.");
			}

			peerBitfield = new BitArray(payload);
			if (peerBitfield.Length < pieceCount)
			{
				throw new FormatException("Bitfield length does not match expected piece count.");
			}
			peerBitfield.Length = pieceCount; // Set the length to the number of pieces
		}

		public BitArray GetBitfield()
		{
			return peerBitfield;
		}

		// Download the first piece from the peer (Not in use)
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

		// Download multiple pieces from the peer
		public async Task<List<(int index, byte[] data)>> DownloadPieces(Torrent torrent, IEnumerable<int> pieceIndices, FileManager fileManager)
		{
			var results = new List<(int index, byte[] data)>(); // Holds the downloaded pieces as tuples of (index, data)
			const int maxPendingRequests = 10; // Maximum number of piece requests to pipeline at once
			var pendingRequests = new List<int>(); // Tracks pieces asked but not yet received

			// Filter pieceIndice to only those the peer has and not yet downloaded
			var availablePieces = pieceIndices
				.Where(i => i>= 0 && i < peerBitfield.Length && peerBitfield[i] && !fileManager.IsPieceDownloaded(i))
				.OrderBy(i => i) // Sort indices for consistent order
				.ToList();

			if (!availablePieces.Any())
			{
				Console.WriteLine("No piece available from this peer that haven't been downloaded.");
				return results; // If no pieces are available, return empty results
			}
			// Send interested message to the peer
			await SendMessage(2, null); // ID 2: Interested message


			foreach (int pieceIndex in availablePieces)
			{
				try
				{
					// Calculate piece size (last piece may be smaller)
					// Making sure we are not going out of bounds of the torrent.PieceHashes array
					long pieceSize = pieceIndex == (torrent.PieceHashes.Length / 20 - 1)
						? torrent.TotalLength % torrent.PieceLength
						: torrent.PieceLength;
					if (pieceSize == 0)
					{
						pieceSize = torrent.PieceLength; // Ensure piece size is at least the piece length
					}

					// Handling choke/unchoke messages
					while (!isUnchoked) // While we are not unchoked, we keep waiting for the unchoke message
					{
						var (messageId, _) = await ReceiveMessageWithTimeout();
						if (messageId == 0) // Choke
						{
							isUnchoked = false;
						}
						else if (messageId == 1) // Unchoke
						{
							isUnchoked = true;
						}
					}

					// Pipeline request (send up to maxPendingRequests)
					// If we have less than maxPendingRequests, we send a request for the piece
					if (pendingRequests.Count < maxPendingRequests)
					{
						byte[] requestPayload = new byte[12];
						BitConverter.GetBytes(pieceIndex).CopyTo(requestPayload, 0); // Piece index
						BitConverter.GetBytes(0).CopyTo(requestPayload, 4); // Offset
						BitConverter.GetBytes((int)pieceSize).CopyTo(requestPayload, 8); // Length
						await SendMessage(6, requestPayload); // ID 6: Request message. Here we ar sending the request
						pendingRequests.Add(pieceIndex); // We fill the list of pending requests with the piece index we just requested
														 // We iterate untill we reach the maxPendingRequests limit
					}

					// Process responses if max pending requests reached
					// Once we filled the pending requests list, we start processing the responses
					while (pendingRequests.Any())
					{
						var (messageId, payload) = await ReceiveMessageWithTimeout(); // Here we are waiting and receiving the actual data from the peer
						if (messageId == 7) // Piece
						{
							int index = BitConverter.ToInt32(payload, 0);
							int begin = BitConverter.ToInt32(payload, 4);

							// Check if the piece index and offset match the request
							if (!pendingRequests.Contains(index) || begin != 0)
							{
								Console.WriteLine($"Unexpected piece index {index} or offset {begin}");
								continue;
							}

							// If the piece index and offset match, we proceed to process the piece data
							long expectedSize = index == (torrent.PieceHashes.Length / 20 - 1)
								? torrent.TotalLength % torrent.PieceLength
								: torrent.PieceLength;
							if (expectedSize == 0)
							{
								expectedSize = torrent.PieceLength; // Ensure piece size is at least the piece length
							}

							// Checks passed, we can now extract the piece data from the payload and add it to the results
							// Also we remove the piece index from the pending requests list
							byte[] pieceData = new byte[expectedSize];
							Array.Copy(payload, 8, pieceData, 0, payload.Length - 8); // Copy piece data from payload
							results.Add((index, pieceData)); // Add downloaded piece to results
							pendingRequests.Remove(index); // Remove from pending requests

							// Send new requests if more pieces remain
							if (pendingRequests.Count < maxPendingRequests)
							{
								var nextPiece = pieceIndices.FirstOrDefault(i => !pendingRequests.Contains(i) &&
								!results.Any(r => r.index == i));
								if (nextPiece >= 0 && nextPiece < torrent.PieceHashes.Length / 20)
								{
									byte[] newRequest = new byte[12];
									BitConverter.GetBytes(nextPiece).CopyTo(newRequest, 0); // Piece index
									BitConverter.GetBytes(0).CopyTo(newRequest, 4); // Offset
									long nextPieceSize = nextPiece == (torrent.PieceHashes.Length / 20 - 1)
										? torrent.TotalLength % torrent.PieceLength
										: torrent.PieceLength;
									if (nextPieceSize == 0)
									{
										nextPieceSize = torrent.PieceLength; // Ensure piece size is at least the piece length
									}
									BitConverter.GetBytes((int)nextPieceSize).CopyTo(newRequest, 8); // Length
									await SendMessage(6, newRequest); // ID 6: Request message
									pendingRequests.Add(nextPiece); // Add to pending requests
								}
							}
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Failed to download piece {pieceIndex}: {ex.Message}");
				}
			}
			return results; // Return the list of downloaded pieces
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
		private async Task<(byte id, byte[] payload)> ReceiveMessage(CancellationToken cancellationToken = default)
		{
			byte[] lengthBytes = new byte[4];
			await stream.ReadAsync(lengthBytes, 0, 4, cancellationToken); // Read the first 4 bytes for length
			int length = BitConverter.ToInt32(lengthBytes, 0); // Get the length of the message
			if (length == 0)
			{
				return (0, null); // Keep-alive message, no payload
			}
			byte[] message = new byte[length];
			await stream.ReadAsync(message, 0, length, cancellationToken); // Read the rest of the message
			byte id = message[0]; // The first byte is the message ID
			byte[] payload = length > 1 ? new byte[length - 1] : null; // The rest is the payload
			if (payload != null)
			{
				Array.Copy(message, 1, payload, 0, length - 1); // Copy the payload from the message
			}
			return (id, payload); // Return the message ID and payload
		}
		private async Task<(byte id, byte[] payload)> ReceiveMessageWithTimeout()
		{
			using var cts = new CancellationTokenSource(timeout);
			try
			{
				return await ReceiveMessage(cts.Token);
			}
			catch (OperationCanceledException)
			{
				throw new TimeoutException("Timed out waiting for peer response.");
			}
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
