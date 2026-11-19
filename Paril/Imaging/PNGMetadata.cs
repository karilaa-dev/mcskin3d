using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Paril.Cryptography;

namespace Paril.Imaging
{
	public class PNGMetadata
	{
		private const uint TextChunk = 0x74455874; // tEXt
		private const uint InternationalTextChunk = 0x69545874; // iTXt
		private const uint EndChunk = 0x49454E44;  // IEND

		// Older versions put their text chunks after IEND. Read those too.
		private static IEnumerable<(long Offset, int Size, uint Type, string Key, string Value)> ReadChunks(Stream input)
		{
			byte[] header = new byte[8];
			input.ReadExactly(header);
			if (!header.AsSpan().SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
				throw new InvalidDataException("Invalid PNG signature.");

			bool foundEnd = false;
			while (input.Position + 12 <= input.Length)
			{
				long start = input.Position;
				input.ReadExactly(header);
				int length = BinaryPrimitives.ReadInt32BigEndian(header);
				uint type = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
				if (length < 0 || length > input.Length - start - 12) break;
				string key = null, value = null;
				if (type is TextChunk or InternationalTextChunk)
				{
					byte[] bytes = new byte[length];
					input.ReadExactly(bytes);
					int separator = Array.IndexOf(bytes, (byte)0);
					if (separator > 0)
					{
						key = Encoding.Latin1.GetString(bytes, 0, separator);
						if (type == TextChunk)
							value = Encoding.Latin1.GetString(bytes.AsSpan(separator + 1));
						else if (separator + 3 < length && bytes[separator + 1] == 0)
						{
							// An uncompressed iTXt value follows the language tag and translated keyword.
							int languageEnd = Array.IndexOf(bytes, (byte)0, separator + 3);
							int translatedEnd = languageEnd < 0 ? -1 : Array.IndexOf(bytes, (byte)0, languageEnd + 1);
							if (translatedEnd >= 0) value = Encoding.UTF8.GetString(bytes.AsSpan(translatedEnd + 1));
						}
					}
				}
				input.Position = start + length + 12;
				foundEnd |= type == EndChunk;
				yield return (start, checked(length + 12), type, key, value);
			}
			if (!foundEnd) throw new InvalidDataException("Incomplete PNG.");
		}

		public static Dictionary<string, string> ReadMetadata(string fileName)
		{
			var metadata = new Dictionary<string, string>();
			try
			{
				using var input = File.OpenRead(fileName);
				foreach (var chunk in ReadChunks(input))
					if (chunk.Value != null) metadata[chunk.Key] = chunk.Value;
			}
			catch (IOException) { }
			catch (InvalidDataException) { }
			return metadata;
		}

		internal static void WriteMetadata(byte[] png, Stream output, Dictionary<string, string> data)
		{
			using var input = new MemoryStream(png, false);
			int copied = 0;
			foreach (var chunk in ReadChunks(input))
			{
				bool replace = chunk.Key != null && data.ContainsKey(chunk.Key);
				if (!replace && chunk.Type != EndChunk) continue;
				int offset = checked((int)chunk.Offset);
				output.Write(png, copied, offset - copied);
				if (chunk.Type == EndChunk)
					foreach (var entry in data) WriteText(output, entry.Key, entry.Value);
				copied = replace ? offset + chunk.Size : offset;
			}
			output.Write(png, copied, png.Length - copied);
		}

		private static void WriteText(Stream output, string key, string value)
		{
			// Keep legacy-readable text for Latin-1 model paths. Custom models can use Unicode.
			byte[] chunk = value.AsSpan().ContainsAnyExceptInRange('\0', '\u00ff')
				? Encoding.UTF8.GetBytes("iTXt" + key + "\0\0\0\0\0" + value)
				: Encoding.Latin1.GetBytes("tEXt" + key + "\0" + value);
			Span<byte> number = stackalloc byte[4];
			BinaryPrimitives.WriteInt32BigEndian(number, chunk.Length - 4);
			output.Write(number);
			output.Write(chunk);
			using var crc = new Crc32();
			output.Write(crc.ComputeHash(chunk));
		}

		public static void WriteMetadata(string fileName, Dictionary<string, string> data)
		{
			string temporary = fileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				// Deny concurrent writers while copying the original image and its unrelated chunks.
				using var source = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
				byte[] png = new byte[checked((int)source.Length)];
				source.ReadExactly(png);
				using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
					WriteMetadata(png, output, data);
				File.Replace(temporary, fileName, null);
			}
			finally
			{
				if (File.Exists(temporary)) File.Delete(temporary);
			}
		}
	}
}