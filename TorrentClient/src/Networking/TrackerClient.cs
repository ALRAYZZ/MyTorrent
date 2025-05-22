using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TorrentClient.src.Models;
using TorrentClient.src.Parsing;

namespace TorrentClient.src.Networking
{
	public class TrackerClient
	{
		private readonly HttpClient httpClient;
		private readonly BencodeParser parser;
		private readonly string peerId;

		public TrackerClient()
		{
			httpClient = new HttpClient();
			parser = new BencodeParser();
			peerId = GeneratePeerId(); // Generate a unique peer ID for the client
		}

		private string? GeneratePeerId()
		{
			var random = new Random();
			var bytes = new byte[12];
			random.NextBytes(bytes);
			return $"-CS0001-{ Convert.ToBase64String(bytes).Substring(0, 12) }";
		}

		// Send HTTP GET request to tracker and return list of peers
		public async Task<List<(string ip, int port)>> GetPeers(Torrent torrent)
		{
			// Contstruct tracker request URL with parameters
			var query = new StringBuilder();
			query.Append($"{torrent.Announce}?");
			query.Append($"info_hash={Uri.EscapeDataString(Encoding.ASCII.GetString(torrent.InfoHash))}");
			query.Append($"peer_id={Uri.EscapeDataString(peerId)}");
			query.Append($"port=6881&"); // Default port for BitTorrent
			query.Append($"uploaded=0&");
			query.Append($"downloaded=0&");
			query.Append($"left={torrent.FileLength}");
			query.Append($"event=started&");

			// Send GET request
			var response = await httpClient.GetAsync(query.ToString());
			response.EnsureSuccessStatusCode();
			var responseData = await response.Content.ReadAsByteArrayAsync();

			// Parse Bencoded responseData
			var responseDict = parser.Parse(responseData);
			if (!responseDict.ContainsKey("peers"))
			{
				throw new FormatException("Invalid tracker response: missing 'peers' key");
			}

			// Extract peers from response
			var peersData = Encoding.ASCII.GetBytes((string)responseDict["peers"]);
			var peers = new List<(string ip, int port)>();
			for (int i = 0; i < peersData.Length; i += 6)
			{
				if (i + 6 > peersData.Length)
				{
					throw new FormatException("Invalid peer data length");
				}
				string ip = $"{peersData[i]}.{peersData[i + 1]}.{peersData[i + 2]}.{peersData[i + 3]}";

				int port = (peersData[i + 4] << 8) | peersData[i + 5]; // Combine high and low bytes to get port number
				peers.Add((ip, port));
			}

			return peers;

		}
	}
}
