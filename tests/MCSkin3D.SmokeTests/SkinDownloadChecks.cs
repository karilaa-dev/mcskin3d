using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCSkin3D.Services;
using Paril.Imaging;

internal static class SkinDownloadChecks
{
    private const string PlayerId = "0123456789abcdef0123456789abcdef";
    private const string TextureUrl = "http://textures.minecraft.net/texture/0123456789abcdef";

    public static void Run(Action<bool, string> check)
    {
        foreach (int height in new[] { 32, 64 })
        {
            byte[] png = MakePng(64, height);
            using var handler = new FixtureHandler(Profile(PlayerId), Textures(TextureUrl), png);
            using var http = new HttpClient(handler);
            var client = new MinecraftSkinClient(http);
            byte[] result = client.DownloadSkinAsync("Test_Player").GetAwaiter().GetResult();
            check(png.SequenceEqual(result), "Downloaded skin bytes must match valid PNG response");
            check(handler.Requests.Count == 3, "Skin download must resolve player, session textures, then image");
            check(handler.Requests[0].Host == "api.mojang.com" && handler.Requests[0].AbsolutePath.EndsWith("/Test_Player"), "Username request must target Mojang profile endpoint");
            check(handler.Requests[1].Host == "sessionserver.mojang.com" && handler.Requests[1].AbsolutePath.Contains(PlayerId), "Texture request must use resolved profile ID");
            check(handler.Requests[2].Host == "textures.minecraft.net", "Image request must target Minecraft texture host");
            check(handler.Requests.All(uri => uri.Scheme == "https"), "All requests must use HTTPS, including upgraded legacy texture URL");
        }

        foreach (int height in new[] { 32, 64 })
        foreach (string variant in new[] { null, "default", "slim" })
        {
            byte[] png = MakePng(64, height);
            using var handler = new FixtureHandler(Profile(PlayerId), Textures(TextureUrl, variant), png);
            using var http = new HttpClient(handler);
            var skin = new MinecraftSkinClient(http).DownloadProfileSkinAsync("Player").GetAwaiter().GetResult();
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            try
            {
                skin.SaveNew(path);
                var metadata = PNGMetadata.ReadMetadata(path);
                string expected = (variant == "slim" ? "Players/Alex" : "Players/Steve") + (height == 32 ? " (Minimal)" : "");
                check(metadata["Model"] == expected && metadata["ModelChecked"] == "1", "Nickname import stores the profile model on first write");
                using var image = new Bitmap(path);
                check(image.GetPixel(0, 0).A == 51, "Adding the model preserves the downloaded texture");
            }
            finally { File.Delete(path); }
        }

        foreach (string name in new[] { "", "../player", "name with spaces", "abcdefghijklmnopqrstuvwxyz" })
        {
            using var handler = new FixtureHandler(Profile(PlayerId), Textures(TextureUrl), MakePng(64, 64));
            using var http = new HttpClient(handler);
            check(Rejects(() => new MinecraftSkinClient(http).DownloadSkinAsync(name)), "Invalid Minecraft names must be rejected");
            check(handler.Requests.Count == 0, "Invalid username must be rejected before HTTP requests");
        }

        using (var handler = new FixtureHandler(Profile("../../bad"), Textures(TextureUrl), MakePng(64, 64)))
        using (var http = new HttpClient(handler))
        {
            check(Rejects(() => new MinecraftSkinClient(http).DownloadSkinAsync("Player")), "Invalid profile ID must be rejected");
            check(handler.Requests.Count == 1, "Invalid profile ID must not be inserted into a session request");
        }

        foreach (string url in new[] { "https://example.com/texture/abcdef", "https://textures.minecraft.net.evil.example/texture/abcdef", "https://textures.minecraft.net/not-a-texture/abcdef" })
        {
            using var handler = new FixtureHandler(Profile(PlayerId), Textures(url), MakePng(64, 64));
            using var http = new HttpClient(handler);
            check(Rejects(() => new MinecraftSkinClient(http).DownloadSkinAsync("Player")), "Unexpected texture URL must be rejected");
            check(handler.Requests.Count == 2, "Unexpected texture URL must not be fetched");
        }

        using (var handler = new FixtureHandler(Profile(PlayerId), Textures(TextureUrl), MakePng(32, 32)))
        using (var http = new HttpClient(handler))
            check(Rejects(() => new MinecraftSkinClient(http).DownloadSkinAsync("Player")), "Unsupported PNG skin dimensions must be rejected");

        using (var handler = new FixtureHandler(Profile(PlayerId), "{\"properties\":[]}", MakePng(64, 64)))
        using (var http = new HttpClient(handler))
        {
            check(Rejects(() => new MinecraftSkinClient(http).DownloadSkinAsync("Player")), "Missing textures property must produce an error");
            check(handler.Requests.Count == 2, "Missing texture property must not issue image request");
        }

        using (var handler = new FixtureHandler(Profile(PlayerId), Textures(TextureUrl), Encoding.UTF8.GetBytes("not a PNG")))
        using (var http = new HttpClient(handler))
            check(Rejects(() => new MinecraftSkinClient(http).DownloadSkinAsync("Player")), "Non-PNG image response must be rejected");
    }

    private static bool Rejects(Func<Task<byte[]>> action)
    {
        try { action().GetAwaiter().GetResult(); return false; }
        catch (ArgumentException) { return true; }
        catch (InvalidDataException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (HttpRequestException) { return true; }
    }

    private static string Profile(string id) => JsonSerializer.Serialize(new { id });
    private static string Textures(string url, string model = null)
    {
        object skin = model == null ? new { url } : new { url, metadata = new { model } };
        string payload = JsonSerializer.Serialize(new { textures = new { SKIN = skin } });
        return JsonSerializer.Serialize(new { properties = new[] { new { name = "textures", value = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)) } } });
    }

    private static byte[] MakePng(int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        bitmap.SetPixel(0, 0, Color.FromArgb(51, 20, 90, 130));
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly string profile;
        private readonly string textures;
        private readonly byte[] image;
        public readonly List<Uri> Requests = new List<Uri>();

        public FixtureHandler(string profile, string textures, byte[] image)
        {
            this.profile = profile; this.textures = textures; this.image = image;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri);
            HttpContent content = Requests.Count switch
            {
                1 => new StringContent(profile, Encoding.UTF8, "application/json"),
                2 => new StringContent(textures, Encoding.UTF8, "application/json"),
                3 => new ByteArrayContent(image),
                _ => throw new Exception("Unexpected fourth HTTP request")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
