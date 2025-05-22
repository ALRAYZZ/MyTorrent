using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace TorrentClient.src.Parsing
{
	// Parses Bencode data from .torrent files
	public class BencodeParser
    {
		private byte[] data;
		private int index; // This is the index that we iterate over the byte array since we using while loops to parse the data

		// Stores the byte range of the info dictionary for computing the info hash
		public (int start, int length)? InfoRange { get; private set; }

		// Initialize parser with torrent file bytes
		// We use object instead of a specific type to allow for flexibility in the data structure since torrent files can contain various types of data
		public Dictionary<string, object> Parse(byte[] torrentData)
		{
			//  Read all bytes from the .torrent file
			data = torrentData;
			index = 0;
			if (data.Length == 0)
			{
				throw new ArgumentException("File is empty");
			}
			// .torrent files are always dictionaries at the root
			return ParseDictionary();
		}
		// Parse a Bencode dictionary
		private Dictionary<string, object> ParseDictionary()
		{
			// Check for dictionary start ('d')
			if (data[index] != 'd')
			{
				throw new FormatException("Invalid Bencode format: Expected dictionary start.");
			}
			index++; // We detect the "d" meaning we on a dictionary and then we move to analyze the next character

			var dict = new Dictionary<string, object>();

			// We keep iterating while index doesnt fall out of the byte array lenght or we find an  'e'
			// meaning its the end of the dict based on Bencode encoding rules
			while (index < data.Length && data[index] != 'e') 
			{
				// Dictionary keys are always strings
				string key = ParseString();
				// Track byte range for info dictionary
				int startIndex = index;
				// Parse the value associated with the key
				object value = ParseNext();


				// Parsing string will iterate untill it hits the ":" character so it will build the whole string
				// then if the string is "info" we store the range of the info dictionary
				// We need the range to go back to the raw byte array and compute the info hash
				// We cannot compute hash of the dictionary directly since it will be different from the original torrent file
				if (key == "info")
				{
					InfoRange = (startIndex, index - startIndex); // Store the range of the info dictionary so we can compute the info hash later
				}
				dict[key] = value;
			}
			if (index >= data.Length)
			{
				throw new FormatException("Unexpected end of data in dictionary");
			}
			index++; // Skip 'e' (end of dictionary)
			return dict;
		}
		// Parse a Bencode string
		private string ParseString()
		{
			// Find length prefix (e.g., "4" in "4:spam")
			int lengthStart = 0;
			while (index < data.Length && data[index] != ':')
			{
				if (!char.IsDigit((char)data[index]))
				{
					throw new FormatException($"Invalid string length at position {index}");
				}
				// Convert ASCII digit to integer
				lengthStart = lengthStart * 10 + (data[index] - '0'); // Starts with 0 * 10 + (ASCII value of the digit) ex (50 - 48 = 2)
				index++;
			}
			if (index >= data.Length || data[index] != ':')
			{
				throw new FormatException($"Invalid string format at position {index}");
			}
			index++; // Skip ':'

			if (index + lengthStart > data.Length)
			{
				throw new FormatException($"String length {lengthStart} exceeds data size at position {index}");
			}
			// Extract string data
			string result = Encoding.ASCII.GetString(data, index, lengthStart);
			index += lengthStart; // Move index past the string data
			return result;
		}
		// Parse the next Bencode element
		// Generic method that checks the next data type on the index and routes to the appropriate parsing method

		// Parse a Bencode integer
		private long ParseInteger()
		{
			if (data[index] != 'i')
			{
				throw new FormatException($"Expected integer start, found {(char)data[index]} at position {index}");
			}
			index++; // Skip 'i'

			long value = 0;
			bool negative = false;
			// Checking for negative sign and then we use the bool variable to keep in memory that is a negative number
			if (index < data.Length && data[index] == '-')
			{
				negative = true;
				index++; // Skip '-'
			}

			while (index < data.Length && data[index] != 'e')
			{
				if (!char.IsDigit((char)data[index]))
				{
					throw new FormatException($"Invalid integer format at position {index}");
				}
				value = value * 10 + (data[index] - '0'); // Convert ASCII digit to integer
				index++;
			}
			if (index >= data.Length || data[index] != 'e')
			{
				throw new FormatException($"Expected 'e' to end integer at position {index}");
			}
			index++; // Skip 'e'

			return negative ? -value : value; // Return negative value if negative flag is set

		}
		// Parse a Bencode list
		private List<object> ParseList()
		{
			if (data[index] != 'l')
			{
				throw new FormatException($"Expected list start, found {(char)data[index]} at position {index}");
			}
			index++; // Skip 'l'

			var list = new List<object>();
			while (index < data.Length && data[index] != 'e')
			{
				// Parse each element in the list. We then check every type in the list and run again the interation needed
				// Since its a list, we need to add an extra layer of iteration on top.
				list.Add(ParseNext()); 
			}
			if (index >= data.Length)
			{
				throw new FormatException("Unexpected end of data in list");
			}
			index++; // Skip 'e'
			return list;
		}

		// Routing method to determine the next element type and call the appropriate parser
		private object ParseNext()
		{
			if (index >= data.Length)
			{
				throw new FormatException("Unexpected end of data");
			}

			char current = (char)data[index];

			if (char.IsDigit(current))
			{
				return ParseString(); // Parse string
			}
			if (current == 'd')
			{
				return ParseDictionary(); // Parse dictionary
			}
			if (current == 'i')
			{
				return ParseInteger(); // Parse integer
			}
			if (current == 'l')
			{
				return ParseList(); // Parse list
			}
			else
			{
				throw new FormatException("Invalid Bencode format: Unexpected character.");
			}
		}

		public byte[] ComputeInfoHash()
		{
			if (InfoRange == null)
			{
				throw new InvalidOperationException("Info dictionary not parsed");
			}
			using var sha1 = SHA1.Create();
			// Here we hash from the byte array using the range we stored when parsing the info dictionary
			// So its a 3 step proccess: Parse the byte array to understand what contains and search for "info" dictionary
			// Once found, we store the range of it inside the byte array.
			// Then we used the range and we apply it in the byte array and hash it
			return sha1.ComputeHash(data, InfoRange.Value.start, InfoRange.Value.length);
		}
	}
}
