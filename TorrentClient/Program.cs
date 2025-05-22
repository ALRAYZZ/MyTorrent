using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TorrentClient.src.Models;
using TorrentClient.src.Networking;
using TorrentClient.src.Parsing;

namespace TorrentClient
{
    class Program
    {
        static async void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: MyTorrent <torrent_file_path>");
                return;
            }

            try
            {
                // Read .torrent file bytes
                byte[] torrentData = File.ReadAllBytes(args[0]);
                var parser = new BencodeParser();
                var torrentDict = parser.Parse(torrentData);

				// Extract metadata and create Torrent object
                if (!torrentDict.ContainsKey("announce") || !torrentDict.ContainsKey("info"))
				{
                    throw new FormatException("Missing required fields: announce or info");
				}

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
                byte[] pieceHashes = Encoding.ASCII.GetBytes((string)info["pieces"]);
                byte[] infoHash = parser.ComputeInfoHash();

                var torrent = new Torrent(announce, fileName, fileLength, pieceLength, pieceHashes, infoHash);
                torrent.PrintMetadata();


                // Contact tracker to get the peers
                var trackerClient = new TrackerClient();
                var peers = await trackerClient.GetPeers(torrent);
				Console.WriteLine("\nPeers:");
				foreach (var peer in peers)
				{
					Console.WriteLine($"  {peer.ip}:{peer.port}");
				}
			}
			catch (Exception ex)
            {
				Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
