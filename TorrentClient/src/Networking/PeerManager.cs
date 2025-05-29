using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.Serialization.Formatters;
using System.Text;
using System.Threading.Tasks;
using TorrentClient.FileManagement;
using TorrentClient.src.Models;

namespace TorrentClient.src.Networking
{
	public class PeerManager
	{
		private readonly string peerId;
		private readonly List<(PeerClient client, string ip, int port)> activePeers;
		private readonly int maxPeers;
		private readonly Dictionary<int, int> pieceAvailability; // Tracks piece availability across peers


		public PeerManager(string peerId, int maxPeers = 10)
		{
			this.peerId = peerId ?? throw new ArgumentNullException(nameof(peerId));
			this.maxPeers = maxPeers;
			activePeers = new List<(PeerClient, string, int)>(maxPeers);
			pieceAvailability = new Dictionary<int, int>(); // Initialize piece availability dictionary
		}

		// Connect to multiple peers and initialize bitfields
		public async Task ConnectToPeers(Torrent torrent, List<(string ip, int port)> peers)
		{
			var connectTasks = new List<Task<(PeerClient client, string ip, int port, string peerId)>>();
			foreach (var (ip, port) in peers.Take(maxPeers))
			{
				var peerClient = new PeerClient(peerId);
				connectTasks.Add(peerClient.ConnectAndHandshake(torrent, ip, port)
					.ContinueWith(t => (client: peerClient, ip, port, peerId: t.Result.peerId)));
			}

			var results = await Task.WhenAll(connectTasks.Where(t => !t.IsFaulted));
			foreach (var (client, ip, port, peerId) in results)
			{
				Console.WriteLine($"Connected to peer {ip}:{port}, Peer ID: {peerId}");
				activePeers.Add((client, ip, port));
			}

			// Aggregate bitfields
			int pieceCount = torrent.PieceHashes.Length / 20; // Each piece hash is 20 bytes
			for (int i = 0; i < pieceCount; i++)
			{
				pieceAvailability[i] = 0; // Initialize piece availability count
			}

			foreach (var (client, _, _) in activePeers)
			{
				var bitfield = client.GetBitfield();
				for (int i = 0; i < bitfield.Length; i++)
				{
					if (bitfield[i])
					{
						pieceAvailability[i] = pieceAvailability.GetValueOrDefault(i, 0) + 1; // Increment availability count for this piece
					}
				}
			}
		}

		// Download all pieces using rarest-first algorithm
		public async Task<List<(int index, byte[] data)>> DownloadAllPieces(Torrent torrent, FileManager fileManager)
		{
			var results = new List<(int index, byte[] data)>(); // Store downloaded pieces
			int pieceCount = torrent.PieceHashes.Length / 20; // Each piece hash is 20 bytes
			var remainingPieces = Enumerable.Range(0, pieceCount)
				.Where(i => pieceAvailability.ContainsKey(i) && pieceAvailability[i] > 0 && !fileManager.IsPieceDownloaded(i))
				.OrderBy(i => pieceAvailability[i]) // Sort by availability (rarest first)
				.ToList();

			if (!remainingPieces.Any())
			{
				Console.WriteLine("All pieces already downloaded.");
				return results; // Return empty list if all pieces are downloaded
			}

			var downloadTasks = new List<Task<List<(int index, byte[] data)>>>();
			foreach (var (client, _, _) in activePeers)
			{
				// Assign pieces to each peer, prioritizing rarest pieces
				var peerPieces = remainingPieces
					.Where(i => client.GetBitfield()[i]) // Only assign pieces that the peer has
					.Take(pieceCount / activePeers.Count + 1) // Distribute pieces evenly among peers
					.ToList();

				if (peerPieces.Any())
				{
					downloadTasks.Add(client.DownloadPieces(torrent, peerPieces, fileManager));
				}
			}

			var peerResults = await Task.WhenAll(downloadTasks);
			foreach (var peerResult in peerResults)
			{
				results.AddRange(peerResult); // Combine results from all peers
				// Update remaining pieces after each download
				remainingPieces = remainingPieces.Except(peerResult.Select(r => r.index)).ToList();
			}

			return results.OrderBy(r => r.index).ToList(); // Sort by piece index before returning
		}

		public int ActivePeerCount => activePeers.Count; // Get the count of active peers
	}
}
