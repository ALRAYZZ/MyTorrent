using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TorrentClient.src.Models;
using TorrentClient.src.Parsing;

namespace TorrentClient.src.Networking
{
	// No DI here. Possibly we can refactor this later to use dependency injection for HttpClient, BencodeParser, etc.
	public class TrackerClient
	{
		private readonly HttpClient httpClient;
		private readonly BencodeParser parser;
		public readonly string peerId;

		public TrackerClient()
		{
			httpClient = new HttpClient();
			parser = new BencodeParser();
			peerId = GeneratePeerId(); // Generate a unique peer ID for the client
		}

		private string GeneratePeerId()
		{
			var random = new Random();
			var bytes = new byte[12];
			random.NextBytes(bytes);
			return $"-MT0001-{ Convert.ToBase64String(bytes).Substring(0, 12) }";
		}

		// Send HTTP GET request to tracker and return list of peers
		// Returns a list of tuples containing IP addresses and ports of peers
		public async Task<List<(string ip, int port)>> GetPeers(Torrent torrent)
		{
			// Contstruct tracker request URL with parameters
			// Here we building the GET request query string based on the Bittorrent protocol specifications
			var query = new StringBuilder();
			query.Append($"{torrent.Announce}?");
			query.Append($"info_hash={Uri.EscapeDataString(Encoding.ASCII.GetString(torrent.InfoHash))}&");
			query.Append($"peer_id={Uri.EscapeDataString(peerId)}&");
			query.Append($"port=6881&"); // Default port for BitTorrent
			query.Append($"uploaded=0&");
			query.Append($"downloaded=0&");
			query.Append($"left={torrent.FileLength}");
			query.Append($"event=started&");

			// Send GET request with the constructed query string
			HttpResponseMessage response = await httpClient.GetAsync(query.ToString());
			response.EnsureSuccessStatusCode();
			byte[] responseData = await response.Content.ReadAsByteArrayAsync();

			// Parse Bencoded responseData
			Dictionary<string, object> responseDict = parser.Parse(responseData);

			if (!responseDict.ContainsKey("peers"))
			{
				throw new FormatException("Invalid tracker response: missing 'peers' key");
			}

			// Extract peers from response we got out of the tracker based on our string query
			// We need to get the bytes of the "peers" field, which is a string of IP addresses and ports
			// Because the peers are represented as a binary string in the response, we need to decode it
			// If we got just the string we would get gibberish, so we need to convert it to bytes first
			// So its like having a byte array containing all the response data and then inside the "peers" key we have a string of bytes
			var peersData = Encoding.ASCII.GetBytes((string)responseDict["peers"]);
			// Each peer is represented by 6 bytes: 4 for IP and 2 for port
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
