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
		private readonly long totalLength; // Total length of the torrent data
		private int downloadedPieceCount; // Count of downloaded pieces
		private readonly Torrent torrent;
		private readonly object writeLock = new object(); // Lock for thread-safe writing


		public FileManager (string outputPath, int pieceCount, Torrent torrent)
		{
			this.torrent = torrent ?? throw new ArgumentNullException(nameof(torrent)); // Ensure torrent is not null
			this.outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
			downloadedPieces = new BitArray(pieceCount, false); // Initialize with all pieces not downloaded
																// BitArray allows us to store the downloaded state of each piece efficiently
																// PieceCount is the total number of pieces in the torrent
																// We creating a BitArray with the size of pieceCount and initializing all bits to false
																// So every piece has its bool value
			this.totalLength = torrent.TotalLength;
			downloadedPieceCount = 0;
			PreAllocateFiles(); // Pre-allocate files based on the total length and piece count
		}
		private void PreAllocateFiles()
		{
			foreach (var (path, length) in torrent.Files.Count > 0 ? torrent.Files : new[] { (torrent.Name, torrent.TotalLength) })
			{
				string fullPath = Path.Combine(outputPath, path);
				Directory.CreateDirectory(Path.GetDirectoryName(fullPath)); // Ensure directory exists
				using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
				{
					stream.SetLength(length); // Pre-allocate file with the specified length
				}
			}
		}

		// Check if a piece has already been downloaded
		public bool IsPieceDownloaded(int pieceIndex)
		{
			lock (writeLock)
			{
				if (pieceIndex < 0 || pieceIndex >= downloadedPieces.Length)
				{
					throw new ArgumentException("Invalid piece index", nameof(pieceIndex));
				}
				return downloadedPieces[pieceIndex]; // Returns true if the piece is already downloaded
													 // This is doing a check on the BitArray to see if the piece at the given index is true or false 
			}
		}

		// Write a piece to disk and verify its SHA-1 hash and mark it as downloaded
		public bool WriteAndVerifyPiece(Torrent torrent, int pieceIndex, byte[] pieceData)
		{
			// Verify piece hash
			if (!VerifyPiece(torrent, pieceIndex, pieceData))
			{
				return false; // Hash mismatch, piece is invalid
			}

			lock (writeLock)
			{
				if (downloadedPieces[pieceIndex])
				{
					return true; // Piece already downloaded, no need to write again
				}

				// Mark piece as downloaded
				downloadedPieces[pieceIndex] = true; // Set the bit for this piece to true in the BitArray
				downloadedPieceCount++; // Increment the count of downloaded pieces

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
						using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Write))
						{
							// Move the cursor to the correct position in the file
							stream.Seek(writeStart, SeekOrigin.Begin);
							// Write the piece data to the file
							stream.Write(pieceData, dataOffset + (int)writeStart, writeLength);
						}
					}
					currentOffset += length; // Move to the next file's start position
				}
				// Update progress
				double progress = (double)downloadedPieceCount / downloadedPieces.Length * 100.0; // Calculate progress percentage
				Console.WriteLine($"Progress: {progress:F2}% ({downloadedPieceCount}/{downloadedPieces.Length} pieces)");
				return true; // Piece written successfully 
			}
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
		public double GetProgress()
		{
			lock (writeLock)
			{
				return (double)downloadedPieceCount / downloadedPieces.Length * 100.0; // Calculate progress percentage 
			}
		}
		public bool IsComplete()
		{
			lock (writeLock)
			{
				// This will return true if the count of downloaded pieces is equal to the total number of pieces in the torrent 
				return downloadedPieceCount == downloadedPieces.Length; // Check if all pieces are downloaded
			}
		}
	}
}
