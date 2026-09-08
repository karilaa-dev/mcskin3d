using System;
using System.Buffers.Binary;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCSkin3D.Services
{
	public sealed class MinecraftSkinClient
	{
		private const int MaximumResponseBytes = 1024 * 1024;
		private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
		private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
		private static readonly HttpClient SharedClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
		{
			Timeout = RequestTimeout
		};
		private readonly HttpClient _client;

		public MinecraftSkinClient() : this(SharedClient)
		{
		}

		public MinecraftSkinClient(HttpClient client)
		{
			_client = client ?? throw new ArgumentNullException(nameof(client));
		}

		public async Task<byte[]> DownloadSkinAsync(string name)
		{
			if (string.IsNullOrEmpty(name) || name.Length > 16)
				throw new ArgumentException("A Minecraft username must contain 1 to 16 letters, numbers, or underscores.", nameof(name));
			foreach (char character in name)
			{
				if (!((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') ||
					(character >= '0' && character <= '9') || character == '_'))
					throw new ArgumentException("A Minecraft username must contain 1 to 16 letters, numbers, or underscores.", nameof(name));
			}

			using var timeout = new CancellationTokenSource(RequestTimeout);
			try
			{
				using var profile = JsonDocument.Parse(await GetBytesAsync(
					new Uri("https://api.mojang.com/users/profiles/minecraft/" + name), timeout.Token).ConfigureAwait(false));
				string id = RequiredString(profile.RootElement, "id");
				if (id.Length != 32 || !IsHex(id))
					throw new InvalidDataException("Minecraft returned an invalid profile ID.");

				using var session = JsonDocument.Parse(await GetBytesAsync(
					new Uri("https://sessionserver.mojang.com/session/minecraft/profile/" + id), timeout.Token).ConfigureAwait(false));
				if (session.RootElement.ValueKind != JsonValueKind.Object ||
					!session.RootElement.TryGetProperty("properties", out JsonElement properties) || properties.ValueKind != JsonValueKind.Array)
					throw new InvalidDataException("Minecraft did not return this profile's skin properties.");

				foreach (JsonElement property in properties.EnumerateArray())
				{
					if (RequiredString(property, "name") != "textures")
						continue;
					using var textures = JsonDocument.Parse(Convert.FromBase64String(RequiredString(property, "value")));
					if (textures.RootElement.ValueKind != JsonValueKind.Object ||
						!textures.RootElement.TryGetProperty("textures", out JsonElement textureSet) || textureSet.ValueKind != JsonValueKind.Object ||
						!textureSet.TryGetProperty("SKIN", out JsonElement skin))
						throw new InvalidDataException("This Minecraft profile does not have a custom skin.");

					Uri textureUri = GetTextureUri(RequiredString(skin, "url"));
					byte[] bytes = await GetBytesAsync(textureUri, timeout.Token).ConfigureAwait(false);
					ValidateSkin(bytes);
					return bytes;
				}
				throw new InvalidDataException("This Minecraft profile does not have a custom skin.");
			}
			catch (JsonException exception)
			{
				throw new InvalidDataException("Minecraft returned invalid skin information.", exception);
			}
			catch (FormatException exception)
			{
				throw new InvalidDataException("Minecraft returned invalid encoded skin information.", exception);
			}
		}

		private static string RequiredString(JsonElement element, string name)
		{
			if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value) ||
				value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
				throw new InvalidDataException("Minecraft returned incomplete skin information.");
			return value.GetString();
		}

		private static bool IsHex(string text)
		{
			foreach (char character in text)
			{
				if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') ||
					(character >= 'A' && character <= 'F')))
					return false;
			}
			return text.Length != 0;
		}

		private static Uri GetTextureUri(string value)
		{
			const string texturePath = "/texture/";
			if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
				(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
				!string.Equals(uri.Host, "textures.minecraft.net", StringComparison.OrdinalIgnoreCase) ||
				!uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
				!uri.AbsolutePath.StartsWith(texturePath, StringComparison.Ordinal))
				throw new InvalidDataException("Minecraft returned an unsupported skin texture address.");
			string hash = uri.AbsolutePath.Substring(texturePath.Length);
			if (hash.Length > 64 || !IsHex(hash))
				throw new InvalidDataException("Minecraft returned an invalid skin texture path.");
			return new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
		}

		private async Task<byte[]> GetBytesAsync(Uri uri, CancellationToken cancellationToken)
		{
			using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
			response.EnsureSuccessStatusCode();
			if (response.Content.Headers.ContentLength > MaximumResponseBytes)
				throw new InvalidDataException("Minecraft returned an unexpectedly large response.");
			using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
			using var output = new MemoryStream();
			var buffer = new byte[8192];
			int count;
			while ((count = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
			{
				if (output.Length + count > MaximumResponseBytes)
					throw new InvalidDataException("Minecraft returned an unexpectedly large response.");
				output.Write(buffer, 0, count);
			}
			return output.ToArray();
		}

		private static void ValidateSkin(byte[] bytes)
		{
			if (bytes.Length < 33 || !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
				throw new InvalidDataException("The downloaded skin is not a PNG image.");
			uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
			uint height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
			if (width != 64 || (height != 32 && height != 64))
				throw new InvalidDataException("A Minecraft skin must be 64 by 32 or 64 by 64 pixels.");
			try
			{
				using var stream = new MemoryStream(bytes, false);
				using var image = Image.FromStream(stream, false, true);
				if (image.Width != width || image.Height != height)
					throw new InvalidDataException("The downloaded skin has invalid image dimensions.");
			}
			catch (ArgumentException exception)
			{
				throw new InvalidDataException("The downloaded skin is not a valid PNG image.", exception);
			}
			catch (OutOfMemoryException exception)
			{
				throw new InvalidDataException("The downloaded skin could not be decoded.", exception);
			}
		}
	}
}
