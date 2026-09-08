using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCSkin3D.UpdateSystem
{
	public class UpdateChecker
	{
		public const string ReleasesUrl = "https://github.com/karilaa-dev/mcskin3d/releases";
		private static readonly HttpClient Client = CreateClient();
		public bool LastCheckSucceeded { get; private set; }

		private static HttpClient CreateClient()
		{
			var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			client.DefaultRequestHeaders.UserAgent.ParseAdd("MCSkin3D/" + typeof(UpdateChecker).Assembly.GetName().Version);
			client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
			return client;
		}

		public bool CheckForUpdates()
		{
			return CheckForUpdatesAsync().GetAwaiter().GetResult();
		}

		public async Task<bool> CheckForUpdatesAsync()
		{
			LastCheckSucceeded = false;
			try
			{
				using var response = await Client.GetAsync("https://api.github.com/repos/karilaa-dev/mcskin3d/releases/latest").ConfigureAwait(false);
				if (response.StatusCode == HttpStatusCode.NotFound)
				{
					LastCheckSucceeded = true;
					return false; // No release published yet.
				}
				response.EnsureSuccessStatusCode();
				using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
				var tag = json.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
				if (!System.Version.TryParse(tag, out var latest))
					return false;
				LastCheckSucceeded = true;
				return latest > System.Version.Parse(Program.Version.ToString());
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException ||
				ex is JsonException || ex is InvalidOperationException || ex is System.Collections.Generic.KeyNotFoundException)
			{
				return false;
			}
		}
	}
}
