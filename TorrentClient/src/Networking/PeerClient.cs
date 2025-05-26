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
			using var tcpClient = new TcpClient();
			// Connect to the peer's IP and port
			await tcpClient.ConnectAsync(ip, port);
			var stream = tcpClient.GetStream();

			// Build and send the handshake message
			byte[] handshake = BuildHandshake(torrent.InfoHash, peerId);
			await stream.WriteAsync(handshake, 0, handshake.Length);

			// Receive handshake response
			byte[] response = new byte[68];
			int bytesRead = await stream.ReadAsync(response, 0, response.Length);
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
