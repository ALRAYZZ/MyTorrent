using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TorrentClient.src.Parsing
{
	// Parses Bencode data from .torrent files
	public class BencodeParser
    {
		private byte[] data;
		private int index;


		// Initialize parser with torrent file bytes
		// We use object instead of a specific type to allow for flexibility in the data structure since torrent files can contain various types of data
		public Dictionary<string, object> Parse(string filePath)
		{
			//  Read all bytes from the .torrent file
			data = File.ReadAllBytes(filePath);
			index = 0;
			// .torrent files are always dictionaries at the root
			return ParseDictionary();
		}
		// Parse a Bencode dictionary
		private Dictionary<string, object> ParseDictionary()
		{
			// Check for didctionary start ('d')
			if (data[index] != 'd')
			{
				throw new FormatException("Invalid Bencode format: Expected dictionary start.");
			}
			index++; // Skip 'd'

			var dict = new Dictionary<string, object>();
			while (index < data.Length && data[index] != 'e')
			{
				// Dictionary kets are always strings
				string key = ParseString();
				// Parse the value associated with the key
				object value = ParseNext();
				dict[key] = value;
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
				lengthStart = lengthStart * 10 + (data[index] - '0');
				index++;
			}
			index++; // Skip ':'

			// Extract string data
			string result = Encoding.ASCII.GetString(data, index, lengthStart);
			index += lengthStart; // Move index past the string data
			return result;
		}
		// Parse the next Bencode element
		private object ParseNext()
		{
			if (char.IsDigit((char)data[index]))
			{
				return ParseString(); // Parse string
			}
			else if (data[index] == 'd')
			{
				return ParseDictionary(); // Parse dictionary
			}
			else
			{
				throw new FormatException("Invalid Bencode format: Unexpected character.");
			}
		}

		
	}


	class Program
	{
		static void main(string[] args)
		{
			// Implement main method to test the BencodeParser
		}
	}
}
