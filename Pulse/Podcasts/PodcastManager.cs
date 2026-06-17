using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Pulse.Data;
using Pulse.DataStorage;
using Pulse.MusicLibrary;

namespace Pulse.Podcasts
{

	public class PodcastManager
	{
		private static readonly HttpClient s_httpClient = BuildHttpClient();

		private PulseConfig m_config;
		private string m_podcastSearchUrl;
		private Thread m_pollThread;

		private PodcastData m_data;


		public PodcastManager(PulseConfig config)
		{
			m_config = config;
			m_podcastSearchUrl = config.PodcastSearchUrl;
			m_data = new PodcastData(m_config);
		}

		private static HttpClient BuildHttpClient()
		{
			HttpClientHandler handler = new HttpClientHandler();
			handler.AllowAutoRedirect = true;
			HttpClient client = new HttpClient(handler);
			client.Timeout = TimeSpan.FromSeconds(60);
			client.DefaultRequestHeaders.UserAgent.ParseAdd("Pulse/1.0");
			return client;
		}

		public Podcast GetPodcast(string seriesId)
		{
			return m_data.LoadPodcast(seriesId);
		}

		public List<Episode> GetItems(string seriesId)
		{
			return m_data.LoadEpisodes(seriesId);
		}

		public string GetPodcastMediaDir(Podcast podcast)
		{
			string podcastsRoot = m_config.PodcastPath;
			string folderName = SanitizeForFileName(podcast.Title);
			string podcastDir = Path.Combine(podcastsRoot, folderName);
			if (!Directory.Exists(podcastDir))
			{
				Directory.CreateDirectory(podcastDir);
			}
			return podcastDir;
		}


		private string SanitizeForFileName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "untitled";
			}

			StringBuilder builder = new StringBuilder(name.Length);
			int nameLength = name.Length;
			bool lastWasSpace = false;
			for (int i = 0; i < nameLength; i++)
			{
				char currentChar = name[i];
				bool isIllegal = false;
				if (currentChar == '<' || currentChar == '>' || currentChar == ':' || currentChar == '"' || currentChar == '/' || currentChar == '\\' || currentChar == '|' || currentChar == '?' || currentChar == '*')
				{
					isIllegal = true;
				}
				else if (currentChar < 32)
				{
					isIllegal = true;
				}

				char outputChar;
				if (isIllegal)
				{
					outputChar = ' ';
				}
				else
				{
					outputChar = currentChar;
				}

				if (outputChar == ' ')
				{
					if (lastWasSpace)
					{
						continue;
					}
					lastWasSpace = true;
				}
				else
				{
					lastWasSpace = false;
				}

				builder.Append(outputChar);
			}

			string trimmed = builder.ToString().Trim();

			int trimEnd = trimmed.Length;
			for (int i = trimmed.Length - 1; i >= 0; i--)
			{
				char tailChar = trimmed[i];
				if (tailChar == '.' || tailChar == ' ')
				{
					trimEnd = i;
				}
				else
				{
					break;
				}
			}
			if (trimEnd < trimmed.Length)
			{
				trimmed = trimmed.Substring(0, trimEnd);
			}

			if (trimmed.Length > 150)
			{
				trimmed = trimmed.Substring(0, 150).Trim();
			}

			if (trimmed.Length == 0)
			{
				return "untitled";
			}
			return trimmed;
		}


		public void IngestFeedStream(string podcastId, string feedUrl, Stream feedXml, out string artworkUrl)
		{
			RssFeedParser parser = new RssFeedParser();
			ParsedFeed parsed = parser.Parse(feedXml);

			Podcast series = m_data.LoadPodcast(podcastId);
			string dateAdded = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
			if (series != null && !string.IsNullOrEmpty(series.DateAdded))
			{
				dateAdded = series.DateAdded;
			}
			else
			{
				series = new Podcast();
			}

			series.Id = podcastId;
			series.Title = parsed.Channel.Title;
			series.Author = parsed.Channel.Author;
			series.Description = parsed.Channel.Description;
			artworkUrl = parsed.Channel.ArtworkUrl;
			series.DateAdded = dateAdded;

			m_data.UpdatePodcast(series);

			List<Episode> existingEpisodes = m_data.LoadEpisodes(podcastId);
			HashSet<string> existingSet = new HashSet<string>();
			foreach (Episode ep in existingEpisodes)
			{
				existingSet.Add(ep.Guid);
			}

			List<Episode> newItems = new List<Episode>();
			int parsedItemCount = parsed.Items.Count;
			for (int i = 0; i < parsedItemCount; i++)
			{
				ParsedItem parsedItem = parsed.Items[i];
				if (string.IsNullOrEmpty(parsedItem.Guid))
				{
					continue;
				}
				bool alreadyStored = existingSet.Contains(parsedItem.Guid);
				if (alreadyStored)
				{
					continue;
				}

				Episode episode = new Episode();
				episode.Id = MusicManager.GenerateID(podcastId + parsedItem.Guid);
				episode.PodcastId = podcastId;
				episode.Guid = parsedItem.Guid;
				episode.Title = parsedItem.Title;
				episode.Description = parsedItem.Description;
				episode.DurationSeconds = parsedItem.DurationSeconds;
				episode.MediaSourceUrl = parsedItem.EnclosureUrl;
				episode.FileSizeBytes = parsedItem.EnclosureLengthBytes;
				episode.PublishedDate = parsedItem.PublishedDateIso;
				episode.OrderIndex = 0;
				episode.DownloadState = eDownloadState.Discovered;
				episode.LocalPath = "";
				newItems.Add(episode);
			}

			if (newItems.Count > 0)
			{
				m_data.UpdateEpisodes(newItems);
			}
		}


		public Podcast AddPodcast(string feedUrl, string userName, bool subscribe)
		{
			string podcastId = MusicManager.GenerateID(feedUrl);

			string artworkUrl = "";
			HttpResponseMessage response = s_httpClient.GetAsync(feedUrl).GetAwaiter().GetResult();
			try
			{
				response.EnsureSuccessStatusCode();
				Stream contentStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
				try
				{
					IngestFeedStream(podcastId, feedUrl, contentStream, out artworkUrl);
				}
				finally
				{
					contentStream.Close();
				}
			}
			finally
			{
				response.Dispose();
			}

			Podcast podcast = GetPodcast(podcastId);
			if (podcast == null)
			{
				return null;
			}

			if (string.IsNullOrEmpty(podcast.FeedUrl))
			{
				podcast.FeedUrl = feedUrl;
				podcast.Retention = eRetentionPolicy.KeepN;
				podcast.RetentionValue = 10;
				podcast.AutoDownload = true;
				podcast.MarkDirty();
			}

			bool shouldSubscribe = subscribe && !string.IsNullOrEmpty(userName);
			if (shouldSubscribe)
			{
				if (!podcast.Users.ContainsKey(userName))
				{
					podcast.Users[userName] = new Podcast.UserData();
				}
				podcast.Users[userName].Subscribed = true;
				podcast.MarkDirty();
			}

			podcast.MarkDirty();
			CacheArtwork(artworkUrl, podcast);

			Thread downloadThread = new Thread(RunInitialDownload);
			downloadThread.IsBackground = true;
			downloadThread.Name = "Pulse.PodcastInitialDownload";
			downloadThread.Start(podcastId);

			return podcast;
		}

		private void RunInitialDownload(object seriesIdObject)
		{
			string seriesId = (string)seriesIdObject;
			try
			{
				EnforceRetention(seriesId);
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}

		public void DownloadEpisode(Episode episode)
		{
			if (episode == null)
			{
				return;
			}
			if (string.IsNullOrEmpty(episode.MediaSourceUrl))
			{
				return;
			}
			bool alreadyOnDisk = !string.IsNullOrEmpty(episode.LocalPath) && File.Exists(episode.LocalPath);
			if (alreadyOnDisk)
			{
				episode.DownloadState = eDownloadState.Downloaded;
				return;
			}

			Podcast series = m_data.LoadPodcast(episode.PodcastId);
			if (series == null)
			{
				return;
			}

			episode.DownloadState = eDownloadState.Downloading;

			string extension = ExtensionForMediaSourceUrl(episode.MediaSourceUrl);
			string seriesDir = GetPodcastMediaDir(series);
			string baseName = SanitizeForFileName(episode.Title);
			string targetPath = Path.Combine(seriesDir, baseName + extension);


			if (File.Exists(targetPath))
			{
				episode.LocalPath = targetPath;
				return;
			}

			try
			{
				HttpResponseMessage response = s_httpClient.GetAsync(episode.MediaSourceUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
				try
				{
					response.EnsureSuccessStatusCode();
					Stream responseStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
					try
					{
						FileStream fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
						try
						{
							responseStream.CopyTo(fileStream);
						}
						finally
						{
							fileStream.Close();
						}
					}
					finally
					{
						responseStream.Close();
					}
				}
				finally
				{
					response.Dispose();
				}

				FileInfo info = new FileInfo(targetPath);
				episode.LocalPath = targetPath;
				episode.FileSizeBytes = info.Length;
				if (episode.DurationSeconds == 0)
				{
					episode.DurationSeconds = ProbeDurationSeconds(targetPath);
				}
				episode.DownloadState = eDownloadState.Downloaded;
				Log.Info("Podcast downloaded: " + episode.Title);
			}
			catch (Exception ex)
			{
				episode.DownloadState = eDownloadState.Failed;
				Log.Warning("Podcast download failed: " + episode.Title + " -- " + ex.Message);
				if (File.Exists(targetPath))
				{
					try
					{
						File.Delete(targetPath);
					}
					catch (Exception deleteEx)
					{
						Log.Warning("Podcast partial delete failed: " + targetPath + " -- " + deleteEx.Message);
					}
				}
			}
		}



		public int ProbeDurationSeconds(string filePath)
		{
			try
			{
				TagLib.File tagFile = TagLib.File.Create(filePath);
				try
				{
					int seconds = (int)tagFile.Properties.Duration.TotalSeconds;
					return seconds;
				}
				finally
				{
					tagFile.Dispose();
				}
			}
			catch (Exception ex)
			{
				Log.Warning("Podcast duration probe failed: " + filePath + " -- " + ex.Message);
				return 0;
			}
		}



		public void EnforceRetention(string podcastId)
		{
			Podcast series = m_data.LoadPodcast(podcastId);
			if (series == null || series.Retention == eRetentionPolicy.KeepExisting)
			{
				return;
			}

			List<Episode> episodes = m_data.LoadEpisodes(podcastId);
			List<Episode> keepSet = ComputeKeepSet(episodes, series);
			HashSet<string> keepIds = new HashSet<string>();
			int keepCount = keepSet.Count;
			for (int i = 0; i < keepCount; i++)
			{
				keepIds.Add(keepSet[i].Id);
			}

			int itemCount = episodes.Count;
			for (int i = 0; i < itemCount; i++)
			{
				Episode episode = episodes[i];
				bool kept = keepIds.Contains(episode.Id);
				if (kept)
				{
					if (episode.NeedsDownload())
					{
						DownloadEpisode(episode);
					}
				}
				else
				{
					if (!kept && episode.DownloadState == eDownloadState.Downloaded)
					{
						UncacheItem(episode);
					}
				}
			}
		}

		private void UncacheItem(Episode episode)
		{
			if (!string.IsNullOrEmpty(episode.LocalPath) && File.Exists(episode.LocalPath))
			{
				try
				{
					File.Delete(episode.LocalPath);
				}
				catch (Exception ex)
				{
					Log.Warning("Podcast retention delete failed: " + episode.LocalPath + " -- " + ex.Message);
				}
			}
			episode.LocalPath = "";
			episode.DownloadState = eDownloadState.Discovered;
		}


		public void CacheArtwork(string httpArtURL, Podcast series)
		{
			if (series == null)
			{
				return;
			}
			if (string.IsNullOrEmpty(httpArtURL))
			{
				return;
			}

			string seriesDir = GetPodcastMediaDir(series);
			string artworkPath = Path.Combine(seriesDir, "folder.jpg");
			if (File.Exists(artworkPath))
			{
				return;
			}

			try
			{
				HttpResponseMessage response = s_httpClient.GetAsync(httpArtURL, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
				try
				{
					response.EnsureSuccessStatusCode();
					Stream responseStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
					try
					{
						FileStream fileStream = new FileStream(artworkPath, FileMode.Create, FileAccess.Write, FileShare.None);
						try
						{
							responseStream.CopyTo(fileStream);
						}
						finally
						{
							fileStream.Close();
						}
					}
					finally
					{
						responseStream.Close();
					}
				}
				finally
				{
					response.Dispose();
				}
			}
			catch (Exception ex)
			{
				Log.Warning("Podcast artwork cache failed: " + series.Title + " -- " + ex.Message);
				if (File.Exists(artworkPath))
				{
					try
					{
						File.Delete(artworkPath);
					}
					catch (Exception deleteEx)
					{
						Log.Warning("Podcast artwork partial delete failed: " + artworkPath + " -- " + deleteEx.Message);
					}
				}
			}
		}


		public void RefreshFeed(string podcastId)
		{
			Podcast series = m_data.LoadPodcast(podcastId);
			if (series == null)
			{
				return;
			}
			if (string.IsNullOrEmpty(series.FeedUrl))
			{
				return;
			}

			string artworkUrl = "";
			try
			{
				HttpResponseMessage response = s_httpClient.GetAsync(series.FeedUrl).GetAwaiter().GetResult();
				try
				{
					response.EnsureSuccessStatusCode();
					Stream contentStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
					try
					{
						IngestFeedStream(podcastId, series.FeedUrl, contentStream, out artworkUrl);
					}
					finally
					{
						contentStream.Close();
					}
				}
				finally
				{
					response.Dispose();
				}

				Podcast storedPodcast = GetPodcast(podcastId);
				if (storedPodcast != null)
				{
					CacheArtwork(artworkUrl, storedPodcast);
				}
				EnforceRetention(podcastId);
			}
			catch (Exception ex)
			{
				Log.Warning("Podcast feed refresh failed: " + podcastId + " -- " + ex.Message);
			}
		}

		public void Run()
		{
			if (m_pollThread != null)
			{
				return;
			}

			m_data.Load();

			m_pollThread = new Thread(PollLoop);
			m_pollThread.IsBackground = true;
			m_pollThread.Name = "Pulse.PodcastPoll";
			m_pollThread.Start();
		}

		/// <summary>
		/// Stop the poll thread and flush dirty data. Call once on process exit.
		/// </summary>
		public void Shutdown()
		{
			m_data.Shutdown();
		}

		private void PollLoop()
		{
			while (true)
			{
				PollPodcasts();

				int pollInterval = 1000 * 3600;
				Thread.Sleep(pollInterval);
			}
		}

		private void PollPodcasts()
		{
			try
			{
				List<Podcast> podcasts = m_data.GetPodcasts();
				int podcastCount = podcasts.Count;
				for (int i = 0; i < podcastCount; i++)
				{
					Podcast podcast = podcasts[i];
					RefreshFeed(podcast.Id);
				}
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}


		private List<Episode> ComputeKeepSet(List<Episode> episodes, Podcast podcast)
		{
			List<Episode> remainingEpisodes = new List<Episode>();
			if (episodes == null || podcast == null)
			{
				return remainingEpisodes;
			}

			int itemCount = episodes.Count;
			if (podcast.Retention == eRetentionPolicy.KeepAll)
			{
				for (int i = 0; i < itemCount; i++)
				{
					remainingEpisodes.Add(episodes[i]);
				}
				return remainingEpisodes;
			}

			List<Episode> sortedByDateDesc = new List<Episode>(episodes);
			sortedByDateDesc.Sort(CompareByPublishedDescending);

			if (podcast.Retention == eRetentionPolicy.KeepN)
			{
				int keep = podcast.RetentionValue;
				if (keep < 0)
				{
					keep = 0;
				}
				int sortedCount = sortedByDateDesc.Count;
				int upper = keep;
				if (upper > sortedCount)
				{
					upper = sortedCount;
				}
				for (int i = 0; i < upper; i++)
				{
					remainingEpisodes.Add(sortedByDateDesc[i]);
				}
				return remainingEpisodes;
			}

			if (podcast.Retention == eRetentionPolicy.KeepDays)
			{
				DateTime cutoff = DateTime.UtcNow - TimeSpan.FromDays(podcast.RetentionValue);
				int sortedCount = sortedByDateDesc.Count;
				for (int i = 0; i < sortedCount; i++)
				{
					Episode candidate = sortedByDateDesc[i];
					DateTimeOffset parsed;
					bool parseOk = DateTimeOffset.TryParse(candidate.PublishedDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed);
					if (!parseOk)
					{
						remainingEpisodes.Add(candidate);
						continue;
					}
					if (parsed.UtcDateTime >= cutoff)
					{
						remainingEpisodes.Add(candidate);
					}
				}
				return remainingEpisodes;
			}

			return remainingEpisodes;
		}

		private int CompareByPublishedDescending(Episode left, Episode right)
		{
			return string.CompareOrdinal(right.PublishedDate, left.PublishedDate);
		}

		public List<Podcast> GetSubscribedPodcasts(string userName)
		{
			List<Podcast> podcasts = m_data.GetPodcasts();
			List<Podcast> userSubscribed = new List<Podcast>();
			foreach (Podcast podcast in podcasts)
			{
				Podcast.UserData userData;
				if (podcast.Users.TryGetValue(userName, out userData))
				{
					if (userData.Subscribed)
					{
						userSubscribed.Add(podcast);
					}
				}
			}
			return userSubscribed;
		}

		public List<Podcast> GetAllPodcasts()
		{
			return m_data.GetPodcasts();
		}


		public List<PodcastSearchResult> SearchPodcasts(string query)
		{
			List<PodcastSearchResult> results = new List<PodcastSearchResult>();
			if (string.IsNullOrWhiteSpace(query))
			{
				return results;
			}
			if (string.IsNullOrWhiteSpace(m_podcastSearchUrl))
			{
				return results;
			}

			string url = m_podcastSearchUrl.Replace("{query}", Uri.EscapeDataString(query));
			try
			{
				HttpResponseMessage response = s_httpClient.GetAsync(url).GetAwaiter().GetResult();
				if (!response.IsSuccessStatusCode)
				{
					Log.Warning("Podcast search failed (" + ((int)response.StatusCode).ToString() + ") for query '" + query + "'");
					return results;
				}

				string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				JsonDocument document = JsonDocument.Parse(body);
				try
				{
					JsonElement root = document.RootElement;
					JsonElement resultsElement;
					bool hasResults = root.TryGetProperty("results", out resultsElement);
					if (!hasResults || resultsElement.ValueKind != JsonValueKind.Array)
					{
						return results;
					}

					foreach (JsonElement entry in resultsElement.EnumerateArray())
					{
						string feedUrl = ReadJsonString(entry, "feedUrl");
						if (string.IsNullOrWhiteSpace(feedUrl))
						{
							continue;
						}
						PodcastSearchResult result = new PodcastSearchResult();
						result.Title = ReadJsonString(entry, "collectionName");
						result.Author = ReadJsonString(entry, "artistName");
						result.FeedUrl = feedUrl;
						string artwork = ReadJsonString(entry, "artworkUrl600");
						if (string.IsNullOrEmpty(artwork))
						{
							artwork = ReadJsonString(entry, "artworkUrl100");
						}
						result.ArtworkUrl = artwork;
						results.Add(result);
					}
				}
				finally
				{
					document.Dispose();
				}
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
			return results;
		}


		private static string ReadJsonString(JsonElement element, string propertyName)
		{
			JsonElement value;
			bool found = element.TryGetProperty(propertyName, out value);
			if (!found)
			{
				return "";
			}
			if (value.ValueKind != JsonValueKind.String)
			{
				return "";
			}
			return value.GetString();
		}


		public List<Episode> GetDownloadedItems(string seriesId)
		{
			List<Episode> items = m_data.LoadEpisodes(seriesId);

			List<Episode> loaded = new List<Episode>();
			foreach (Episode episode in items)
			{
				if (episode.DownloadState == eDownloadState.Downloaded)
				{
					loaded.Add(episode);
				}
			}
			loaded.Sort(CompareByPublishedDescending);
			return loaded;
		}

		public Episode GetEpisode(string episodeId)
		{
			return m_data.LoadEpisode(episodeId);
		}

		public int GetUnplayedCount(string seriesId, string userName)
		{
			List<Episode> downloaded = GetDownloadedItems(seriesId);
			int unplayed = 0;
			int downloadedCount = downloaded.Count;
			for (int i = 0; i < downloadedCount; i++)
			{
				Episode item = downloaded[i];
				if (!item.Users.ContainsKey(userName))
				{
					unplayed++;
					continue;
				}
				if (!item.Users[userName].Completed)
				{
					unplayed++;
				}
			}
			return unplayed;
		}


		public void UpdatePodcastSettings(string podcastId, eRetentionPolicy retention, int retentionValue, bool autoDownload)
		{
			Podcast podcast = m_data.LoadPodcast(podcastId);
			podcast.Retention = retention;
			podcast.RetentionValue = retentionValue;
			podcast.AutoDownload = autoDownload;
			podcast.MarkDirty();

			Thread settingsThread = new Thread(RunSettingsApply);
			settingsThread.IsBackground = true;
			settingsThread.Name = "Pulse.PodcastSettingsApply";
			settingsThread.Start(podcastId);
		}

		private void RunSettingsApply(object podcastIdString)
		{
			string podcastId = (string)podcastIdString;
			try
			{
				EnforceRetention(podcastId);
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}

		public void SetSubscribed(string seriesId, string userName, bool subscribed)
		{
			Podcast podcast = m_data.LoadPodcast(seriesId);
			if (!podcast.Users.ContainsKey(userName))
			{
				podcast.Users[userName] = new Podcast.UserData();
			}
			podcast.Users[userName].Subscribed = subscribed;
			podcast.MarkDirty();
		}

		public void SaveProgress(string episodeId, string userName, int positionSeconds)
		{
			Episode episode = m_data.LoadEpisode(episodeId);
			if (episode == null)
			{
				return;
			}

			bool wasCompleted = false;
			if (episode.Users.ContainsKey(userName))
			{
				wasCompleted = episode.Users[userName].Completed;
			}

			bool passedThreshold = false;
			if (episode.DurationSeconds > 0)
			{
				int threshold = episode.DurationSeconds * 95 / 100;
				if (positionSeconds > threshold)
				{
					passedThreshold = true;
				}
			}

			Episode.UserData userData = new Episode.UserData();
			userData.PositionSeconds = positionSeconds;
			userData.LastPlayed = DateTime.UtcNow;
			if (wasCompleted || passedThreshold)
			{
				userData.Completed = true;
			}
			episode.Users[userName] = userData;
			episode.MarkDirty();

			Podcast parent = m_data.LoadPodcast(episode.PodcastId);
			if (parent != null)
			{
				if (!parent.Users.ContainsKey(userName))
				{
					parent.Users[userName] = new Podcast.UserData();
				}
				parent.Users[userName].LastEpisodeId = episodeId;
				parent.Users[userName].LastPlayed = DateTime.UtcNow;
				parent.MarkDirty();
			}
		}

		private string ExtensionForMediaSourceUrl(string mediaSourceUrl)
		{
			if (string.IsNullOrEmpty(mediaSourceUrl))
			{
				return ".mp3";
			}
			string lowered = mediaSourceUrl.ToLowerInvariant();
			int queryStart = lowered.IndexOf('?');
			if (queryStart >= 0)
			{
				lowered = lowered.Substring(0, queryStart);
			}
			if (lowered.EndsWith(".m4a"))
			{
				return ".m4a";
			}
			if (lowered.EndsWith(".mp4"))
			{
				return ".m4a";
			}
			if (lowered.EndsWith(".aac"))
			{
				return ".m4a";
			}
			return ".mp3";
		}
	}
}
