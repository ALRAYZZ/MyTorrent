using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TorrentClient.src.Models;

namespace TorrentClient.FileManagement
{
	// Manages writing and verifying torrent pieces
	public class FileManager
	{
		private readonly string outputPath;

		public FileManager (string outputPath)
		{
			this.outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
		}

		// Write a piece to disk and verify its SHA-1 hash
		public bool WriteAndVerifyPiece(Torrent torrent, int pieceIndex, byte[] pieceData)
		{
			// Verify piece hash
			if (!VerifyPiece(torrent, pieceIndex, pieceData))
			{
				return false; // Hash mismatch, piece is invalid
			}

			// Write piece to file
			using (var stream = new FileStream(outputPath, FileMode.OpenOrCreate, FileAccess.Write))
			{
				// We put the "writting cursor" at the position of the piece we are writing
				// So even if they are not in order, we can write them directly to the correct position using the piece index
				stream.Seek(pieceIndex * (long)torrent.PieceLength, SeekOrigin.Begin);
				stream.Write(pieceData, 0, pieceData.Length);
			}

			return true; // Piece written successfully
		}

		// Verify each piece we receive against the expected SHA-1 hash from the original torrent file
		private bool VerifyPiece(Torrent torrent, int pieceIndex, byte[] pieceData)
		{
			if (pieceIndex < 0 || pieceIndex >= torrent.PieceHashes.Length / 20)
			{
				throw new ArgumentException("Invalid piece index", nameof(pieceIndex));
			}

			using var sha1 = SHA1.Create();
			// Actual hash of the downloaded piece data
			byte[] computedHash = sha1.ComputeHash(pieceData);

			byte[] expectedHash = new byte[20];
			// We get the torrent expected hash for the piece at the given index since each piece hash is 20 bytes long
			// torrent.PieceHashes is a byte array containing all piece hashes concatenated together, so we need to extract the specific piece hash
			Array.Copy(torrent.PieceHashes, pieceIndex * 20, expectedHash, 0, 20);
			// Will return true if the computed hash matches the expected hash
			return computedHash.AsSpan().SequenceEqual(expectedHash);
		}
	}
}
