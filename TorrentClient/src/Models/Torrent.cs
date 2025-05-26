namespace TorrentClient.src.Models
{
	// Represents a torrent's metadata from .torrent files
	public class Torrent
    {
		public string Announce { get; } // Tracker URL
		public string FileName { get; }
		public long FileLength { get; }
		public int PieceLength { get; }
		public byte[] PieceHashes { get; }
		public byte[] InfoHash { get; }


		public Torrent(string announce, string fileName, long fileLength, int pieceLength, byte[] pieceHashes, byte[] infoHash)
		{
			Announce = announce ?? throw new ArgumentNullException(nameof(announce));
			FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
			FileLength = fileLength >= 0 ? fileLength : throw new ArgumentException("File length must be non-negative", nameof(fileLength));
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
		}

		public void PrintMetadata()
		{
			Console.WriteLine("Torrent Metadata: ");
			Console.WriteLine($" Tracker URL: {Announce}");
			Console.WriteLine($" File Name: {FileName}");
			Console.WriteLine($" File Length: {FileLength} bytes");
			Console.WriteLine($" Piece Length: {PieceLength} bytes");
			Console.WriteLine($" Number of Pieces: {PieceHashes.Length / 20}");
			Console.WriteLine($" Info Hash: {BitConverter.ToString(InfoHash).Replace("-", "")}");
		}
	}
}
