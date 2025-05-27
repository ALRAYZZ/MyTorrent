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

                if (!info.ContainsKey("name") || !info.ContainsKey("length") || !info.ContainsKey("piece length") || !info.ContainsKey("pieces"))
                {
                    throw new FormatException("Missing required fields in info dictionary: name, length, piece length, or pieces");
				}


				// Extracting the values from the info dictionary and preparing them for the Torrent object
				string fileName = (string)info["name"];
				long fileLength = (long)info["length"];
				int pieceLength = (int)(long)info["piece length"];
				// The "pieces" field contains the SHA-1 hashes of each piece of the file, encoded as a string
				// We will use this to verify the integrity of the pieces we download doing a check for each piece
				byte[] pieceHashes = Encoding.ASCII.GetBytes((string)info["pieces"]); 
				byte[] infoHash = parser.ComputeInfoHash();

				// Torrent object encapsulates the metadata and provides methods to interact with the torrent
				var torrent = new Torrent(announce, fileName, fileLength, pieceLength, pieceHashes, infoHash);
                torrent.PrintMetadata();


				// Contact tracker to get the peers, this class comes with HTTP client to send requests to the tracker URL and get the list of peers
				var trackerClient = new TrackerClient();

				// List of tuples containing IP and port of each peer
				List<(string ip, int port)> peers = await trackerClient.GetPeers(torrent);
				Console.WriteLine("\nPeers:");
				foreach (var peer in peers)
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

					// Download the first piece from the peer
					byte[] pieceData = await peerClient.DownloadPiece(torrent, 0);
					Console.WriteLine("\nDownloaded piece 0");

					// Write and verify piece
					var fileManager = new FileManager(torrent.FileName);
					bool verified = fileManager.WriteAndVerifyPiece(torrent, 0, pieceData);
					Console.WriteLine($"Piece 0 verification: {(verified ? "Success" : "Failed")}");
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
