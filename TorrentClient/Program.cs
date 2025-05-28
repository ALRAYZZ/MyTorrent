using System.Text;
using TorrentClient.FileManagement;
using TorrentClient.src.Models;
using TorrentClient.src.Networking;
using TorrentClient.src.Parsing;

namespace TorrentClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: MyTorrent <torrent_file_path>");
                return;
            }

            try
            {
				// Read .torrent file bytes on local filesystem
				byte[] torrentData = File.ReadAllBytes(args[0]);
                var parser = new BencodeParser(); // Create an instance of the BencodeParser to parse the torrent file
				var torrentDict = parser.Parse(torrentData); // Since the torrent metadata is encoded in Bencode format, we need to parse it first

				// Extract metadata and create Torrent object
				if (!torrentDict.ContainsKey("announce") || !torrentDict.ContainsKey("info"))
				{
                    throw new FormatException("Missing required fields: announce or info");
				}
				// The "announce" field contains the URL of the tracker so we know where to connect to get peers
				string announce = (string)torrentDict["announce"];
                var info = (Dictionary<string, object>)torrentDict["info"];

                if (!info.ContainsKey("name") || !info.ContainsKey("piece length") || !info.ContainsKey("pieces") || 
					(!info.ContainsKey("length") && !info.ContainsKey("files")))
                {
                    throw new FormatException("Missing required fields in info dictionary: name, length, piece length, or pieces");
				}

				// Debug: Log info dictionary contents
				Console.WriteLine("Info dictionary contents:");
				foreach (var kvp in info)
				{
					string valueStr = kvp.Key == "pieces"
						? $"[binary data, {((string)kvp.Value).Length} bytes]"
						: kvp.Value switch
						{
						string s => s,
						long l => l.ToString(),
						List<object> list => $"List of {list.Count} items",
						Dictionary<string, object> dict => $"Dictionary with {dict.Count} items",
						_ => kvp.Value.ToString() ?? "null"
					};
					Console.WriteLine($" {kvp.Key}: {valueStr}");
				}


				// Extracting the values from the info dictionary and preparing them for the Torrent object
				string name = (string)info["name"];
				int pieceLength = (int)(long)info["piece length"];
				// The "pieces" field contains the SHA-1 hashes of each piece of the file, encoded as a string
				// We will use this to verify the integrity of the pieces we download doing a check for each piece
				byte[] pieceHashes = Encoding.ASCII.GetBytes((string)info["pieces"]); 
				byte[] infoHash = parser.ComputeInfoHash();

				// Handle single-file torrents or multi-file torrents
				long totalLength;
				List<(string path, long length)> files = new List<(string, long)>();
				if (info.ContainsKey("length"))
				{
					// Single-file torrent
					totalLength = (long)info["length"];
					files.Add((name, totalLength));
				}
				else if (info.ContainsKey("files"))
				{
					// Multi-file torrent
					var fileList = (List<object>)info["files"];
					totalLength = 0;
					foreach (Dictionary<string, object> file in fileList)
					{
						if (!file.ContainsKey("length") || !file.ContainsKey("path"))
						{
							throw new FormatException("Invalid file entry in multi-file torrent: missing length or path");
						}
						long length = (long)file["length"];
						var pathList = (List<object>)file["path"];
						string path = Path.Combine(pathList.Cast<string>().ToArray()); // Join path components
						files.Add((path, length));
						totalLength += length; // Sum up the total length of all files
					}
				}
				else
				{
					throw new FormatException("Invalid torrent file: missing length or files field in info dictionary");
				}

				// Create Torrent object with the extracted metadata
				var torrent = new Torrent(announce, name, totalLength, pieceLength, pieceHashes, infoHash, files);
				torrent.PrintMetadata(); // Print the torrent metadata to the console for debugging

				// Contact tracker to get peers
				var trackerClient = new TrackerClient(); // Create an instance of the TrackerClient to communicate with the tracker
				var peers = await trackerClient.GetPeers(torrent); // Get the list of peers from the tracker

				// Getting IPv4 peers from the list of peers to ensure we only connect to valid IPv4 addresses since BitTorrent protocol primarily uses IPv4
				// Maybe we will add IPv6 support later, but for now we will focus on IPv4
				var ipv4Peers = peers.Where(p => System.Net.IPAddress.TryParse(p.ip, out var addr) &&
					addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToList();

				Console.WriteLine("\nIPv4 Peers:");
				foreach (var peer in ipv4Peers)
				{
					Console.WriteLine($"  {peer.ip}:{peer.port}");
				}

				// Connect to the first peer using the PeerClient class and initiate a handshake
				if (peers.Count > 0)
				{
					var peerClient = new PeerClient(trackerClient.peerId);
					// We taking only the first peer from the list for simplicity. Will change later to connect to multiple peers
					var (ip, port, peerId) = await peerClient.ConnectAndHandshake(torrent, peers[0].ip, peers[0].port);
					Console.WriteLine($"\nHandshake successful with peer {ip}:{port}, Peer ID: {peerId}.");

					// Download the first 5 pieces from the peer
					int pieceCount = torrent.PieceHashes.Length / 20; // Each piece hash is 20 bytes
																	  // So we divide the total length of the piece hashes by 20 to get the number of pieces

					// We doing some match check to see if 5 or less pieces exist and then creating a range from 0 to the num we find as minimum
					var pieceIndices = Enumerable.Range(0, Math.Min(5, pieceCount)); // Download only the first 5 pieces for testing
					var fileManager = new FileManager(torrent.Name, pieceCount); // Create an instance of the FileManager to handle file operations
					var pieces = await peerClient.DownloadPieces(torrent, pieceIndices); // Download the pieces from the peer
											 
					// Write and verify pieces
					foreach (var (index, data) in pieces)
					{
						if (!fileManager.IsPieceDownloaded(index))
						{
							bool verified = fileManager.WriteAndVerifyPiece(torrent, index, data); // Write the piece to disk and verify its hash
							Console.WriteLine($"Piece {index} verification: {(verified ? "Success" : "Failed")}");
						}
						else
						{
							Console.WriteLine($"Piece {index} already downloaded, skipping");
						}
					}
				}
				else
				{
					Console.WriteLine("\nNo peers available for handshake.");
				}
			}
			catch (Exception ex)
            {
				Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
