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
			return $"-MT0001-" + BitConverter.ToString(bytes).Replace("-", "").Substring(0, 12);
		}

		// Send HTTP GET request to tracker and return list of peers
		// Returns a list of tuples containing IP addresses and ports of peers
		public async Task<List<(string ip, int port)>> GetPeers(Torrent torrent)
		{
			// Contstruct tracker request URL with parameters
			// Here we building the GET request query string based on the Bittorrent protocol specifications
			var query = new StringBuilder();
			query.Append($"{torrent.Announce}?");
			query.Append($"info_hash={UrlEncode(torrent.InfoHash)}&");
			query.Append($"peer_id={Uri.EscapeDataString(peerId)}&");
			query.Append($"port=6881&"); // Default port for BitTorrent
			query.Append($"uploaded=0&");
			query.Append($"downloaded=0&");
			query.Append($"left={torrent.TotalLength}&");
			query.Append($"event=started");

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

			// Each peer is represented by 6 bytes: 4 for IP and 2 for port
			var peers = new List<(string ip, int port)>();
			var peersObj = responseDict["peers"];

			if (peersObj is string peersString)
			{
				var peersData = Encoding.ASCII.GetBytes(peersString);

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
			}
			else if (peersObj is List<object> peersList)
			{
				foreach (var peerEntry in peersList)
				{
					if (peerEntry is Dictionary<string, object> peerDict &&
						peerDict.TryGetValue("ip", out var ipObj) &&
						peerDict.TryGetValue("port", out var portObj))
					{
						string ip = ipObj as string;
						int port = Convert.ToInt32(portObj);
						peers.Add((ip, port));
					}
				}
			}
			else
			{
				throw new FormatException("Unkown peers format in tracker response");
			}

			return peers;
		}

		// Helper to precent-encode a byte array for BitTorrent protocol
		// On the HTTP GET when contacting the Tracker and passing Info_hash we need to pass safely special characters
		private static string UrlEncode(byte[] bytes)
		{
			var sb = new StringBuilder();
			foreach (byte b in bytes)
			{
				sb.Append('%');
				sb.Append(b.ToString("X2")); // Convert byte to hex and append
			}
			return sb.ToString();
		}
	}
}
