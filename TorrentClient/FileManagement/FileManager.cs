using System;
using System.Collections;
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
		private readonly BitArray downloadedPieces; // Tracks downloaded pieces


		public FileManager (string outputPath, int pieceCount)
		{
			this.outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
			downloadedPieces = new BitArray(pieceCount, false); // Initialize with all pieces not downloaded
																// BitArray allows us to store the downloaded state of each piece efficiently
																// PieceCount is the total number of pieces in the torrent
																// We creating a BitArray with the size of pieceCount and initializing all bits to false
																// So every piece has its bool value
		}

		// Check if a piece has already been downloaded
		public bool IsPieceDownloaded(int pieceIndex)
		{
			if (pieceIndex < 0 || pieceIndex >= downloadedPieces.Length)
			{
				throw new ArgumentException("Invalid piece index", nameof(pieceIndex));
			}
			return downloadedPieces[pieceIndex]; // Returns true if the piece is already downloaded
												 // This is doing a check on the BitArray to see if the piece at the given index is true or false
		}

		// Write a piece to disk and verify its SHA-1 hash and mark it as downloaded
		public bool WriteAndVerifyPiece(Torrent torrent, int pieceIndex, byte[] pieceData)
		{
			// Verify piece hash
			if (!VerifyPiece(torrent, pieceIndex, pieceData))
			{
				return false; // Hash mismatch, piece is invalid
			}

			// Mark piece as downloaded
			downloadedPieces[pieceIndex] = true; // Set the bit for this piece to true in the BitArray

			// Calculate piece offset
			long pieceOffset = pieceIndex * (long)torrent.PieceLength;
			long pieceEnd = pieceOffset + pieceData.Length;

			// Find files overlapping with the piece
			long currentOffset = 0;
			int dataOffset = 0;
			foreach (var (path, length) in torrent.Files.Count > 0 ? torrent.Files : new[] { (torrent.Name, torrent.TotalLength) })
			{
				long fileStart = currentOffset;
				long fileEnd = currentOffset + length;

				// Check if the piece overlaps with the file
				if (pieceOffset < fileEnd && pieceEnd > fileStart)
				{
					// Calculate the portion of the peiece for this file
					long writeStart = Math.Max(pieceOffset, fileStart) - fileStart; // Offset within the file
					long writeEnd = Math.Min(pieceEnd, fileEnd) - fileStart; // End offset within the file
					int writeLength = (int)(writeEnd - writeStart);

					// Create directory and write to file
					string fullPath = Path.Combine(outputPath, path);
					Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
					using (var stream = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.Write))
					{
						// Move the cursor to the correct position in the file
						stream.Seek(pieceOffset + fileStart, SeekOrigin.Begin);
						// Write the piece data to the file
						stream.Write(pieceData, dataOffset + (int)writeStart, writeLength);
					}
				}
				currentOffset += length; // Move to the next file's start position
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
