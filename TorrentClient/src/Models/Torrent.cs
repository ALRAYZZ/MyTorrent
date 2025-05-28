namespace TorrentClient.src.Models
{
	// Represents a torrent's metadata from .torrent files
	public class Torrent
    {
		public string Announce { get; } // Tracker URL
		public string Name { get; }
		public long TotalLength { get; }
		public int PieceLength { get; }
		public byte[] PieceHashes { get; }
		public byte[] InfoHash { get; }
		public IReadOnlyList<(string path, long length)> Files { get; } // Optional: For multi-file torrents, this would contain the file paths and lengths


		public Torrent(string announce, string name, long fileLength, int pieceLength, byte[] pieceHashes, byte[] infoHash, IReadOnlyList<(string path, long length)> files)
		{
			Announce = announce ?? throw new ArgumentNullException(nameof(announce));
			Name = name ?? throw new ArgumentNullException(nameof(name));
			TotalLength = fileLength >= 0 ? fileLength : throw new ArgumentException("File length must be non-negative", nameof(fileLength));
			PieceLength = pieceLength > 0 ? pieceLength : throw new ArgumentException("Piece length must be positive", nameof(pieceLength));

			PieceHashes = pieceHashes ?? throw new ArgumentNullException(nameof(pieceLength));
			if (pieceHashes.Length % 20 != 0)
			{
				throw new ArgumentException("Piece hashes must be a multiple of 20", nameof(pieceHashes));
			}

			InfoHash = infoHash ?? throw new ArgumentNullException(nameof(infoHash));
			if (infoHash.Length != 20)
			{
				throw new ArgumentException("Info hash must be 20 bytes", nameof(infoHash));
			}

			Files = files ?? throw new ArgumentNullException(nameof(files));
		}

		public void PrintMetadata()
		{
			Console.WriteLine("Torrent Metadata: ");
			Console.WriteLine($" Tracker URL: {Announce}");
			Console.WriteLine($" File Name: {Name}");
			Console.WriteLine($" File Length: {TotalLength} bytes");
			Console.WriteLine($" Piece Length: {PieceLength} bytes");
			Console.WriteLine($" Number of Pieces: {PieceHashes.Length / 20}");
			Console.WriteLine($" Info Hash: {BitConverter.ToString(InfoHash).Replace("-", "")}");
			if (Files.Count > 0)
			{
				Console.WriteLine($" Files ({Files.Count}):");
				foreach (var file in Files)
				{
					Console.WriteLine($"  {file.path} ({file.length} bytes)");
				}
			}
		}
	}
}
