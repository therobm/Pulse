using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using Pulse.Database;
using Pulse.MusicLibrary;

namespace Pulse.Series
{
	/// <summary>
	/// Facade over the series database for the podcast subset, sibling to
	/// MusicManager. Owns a SeriesDBConnector pointed at PulseData/
	/// pulse_series_{env}.db plus the SeriesDB it talks through. Holds no
	/// poll thread, no download pipeline, no HTTP routing -- those land in
	/// a later task. The single static HttpClient is reused for every fetch
	/// so RSS polls don't churn TCP/TLS state.
	/// </summary>
	public class PodcastManager
	{
		private static readonly HttpClient s_httpClient = BuildHttpClient();

		private SeriesDBConnector m_connector;
		private SeriesDB m_db;

		public PodcastManager(PulseConfig config)
		{
			string environmentName = config.DatabaseEnvironment;
			if (string.IsNullOrWhiteSpace(environmentName))
			{
				environmentName = "Production";
			}

			string pulseDataRoot = Path.Combine(config.MusicPath, "PulseData");
			if (!Directory.Exists(pulseDataRoot))
			{
				Directory.CreateDirectory(pulseDataRoot);
			}

			string sqliteFileName = "pulse_series_" + environmentName.ToLowerInvariant() + ".db";
			string sqlitePath = Path.Combine(pulseDataRoot, sqliteFileName);

			SeriesDBConnector connector = new SeriesDBConnector();
			connector.SetDatabaseFilePath(sqlitePath);
			m_connector = connector;

			SeriesDBMigrations migrations = new SeriesDBMigrations(connector);
			migrations.RunMigrations();

			m_db = new SeriesDB(connector);

			Log.Info(-1, "Pulse Series DB: env=" + environmentName + " path=" + sqlitePath);
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

		public SeriesInfo GetSeries(string seriesId)
		{
			return m_db.LoadSeries(seriesId);
		}

		public List<SeriesItemInfo> GetItems(string seriesId)
		{
			return m_db.LoadItemsForSeries(seriesId);
		}

		/// <summary>
		/// Offline-testable core of feed ingest: parse the supplied stream,
		/// upsert the series + podcast_feeds rows, then diff parsed item
		/// guids against what's already stored and batch-insert only the
		/// new ones. The caller owns the Stream's lifetime. ArtworkPath is
		/// set to the remote channel image URL for now -- localising it to
		/// PulseData is a later task.
		/// </summary>
		public void IngestFeedStream(string seriesId, string feedUrl, Stream feedXml)
		{
			RssFeedParser parser = new RssFeedParser();
			ParsedFeed parsed = parser.Parse(feedXml);

			string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

			SeriesInfo existingSeries = m_db.LoadSeries(seriesId);
			string dateAdded = nowIso;
			if (existingSeries != null && !string.IsNullOrEmpty(existingSeries.DateAdded))
			{
				dateAdded = existingSeries.DateAdded;
			}

			SeriesInfo series = new SeriesInfo();
			series.Id = seriesId;
			series.Type = eSeriesType.Podcast;
			series.Title = parsed.Channel.Title;
			series.Author = parsed.Channel.Author;
			series.Description = parsed.Channel.Description;
			series.ArtworkPath = parsed.Channel.ArtworkUrl;
			series.DateAdded = dateAdded;
			m_db.UpsertSeries(series);

			PodcastFeedInfo feed = new PodcastFeedInfo();
			feed.SeriesId = seriesId;
			feed.FeedUrl = feedUrl;
			feed.PollIntervalMinutes = 60;
			feed.Retention = eRetentionPolicy.KeepN;
			feed.RetentionValue = 10;
			feed.AutoDownload = true;
			feed.LastPolled = nowIso;
			m_db.UpsertPodcastFeed(feed);

			List<string> existingGuids = m_db.LoadItemGuidsForSeries(seriesId);
			HashSet<string> existingSet = new HashSet<string>(existingGuids);

			List<SeriesItemInfo> newItems = new List<SeriesItemInfo>();
			int parsedItemCount = parsed.Items.Count;
			for (int itemIndex = 0; itemIndex < parsedItemCount; itemIndex++)
			{
				ParsedItem parsedItem = parsed.Items[itemIndex];
				if (string.IsNullOrEmpty(parsedItem.Guid))
				{
					continue;
				}
				bool alreadyStored = existingSet.Contains(parsedItem.Guid);
				if (alreadyStored)
				{
					continue;
				}

				SeriesItemInfo seriesItem = new SeriesItemInfo();
				seriesItem.Id = MusicManager.GenerateID(seriesId + parsedItem.Guid);
				seriesItem.SeriesId = seriesId;
				seriesItem.Guid = parsedItem.Guid;
				seriesItem.Title = parsedItem.Title;
				seriesItem.Description = parsedItem.Description;
				seriesItem.DurationSeconds = parsedItem.DurationSeconds;
				seriesItem.MediaSourceUrl = parsedItem.EnclosureUrl;
				seriesItem.FileSizeBytes = parsedItem.EnclosureLengthBytes;
				seriesItem.PublishedDate = parsedItem.PublishedDateIso;
				// OrderIndex is the audiobook-chapter ordering signal; podcast
				// episodes are sorted by PublishedDate at read time, so this
				// stays at 0 for the podcast path.
				seriesItem.OrderIndex = 0;
				seriesItem.DownloadState = eDownloadState.Discovered;
				seriesItem.LocalPath = "";
				newItems.Add(seriesItem);
			}

			if (newItems.Count > 0)
			{
				m_db.UpsertItems(newItems);
			}
		}

		/// <summary>
		/// Fetches the feed at feedUrl over HTTP (follow-redirects, 60s
		/// timeout, "Pulse/1.0" UA), routes the response stream into
		/// IngestFeedStream, then optionally records a subscription for
		/// userName. The seriesId is derived deterministically from the
		/// feed URL so re-adding the same feed lands on the same row.
		/// </summary>
		public SeriesInfo AddPodcast(string feedUrl, string userName, bool subscribe)
		{
			string seriesId = MusicManager.GenerateID(feedUrl);

			HttpResponseMessage response = s_httpClient.GetAsync(feedUrl).GetAwaiter().GetResult();
			try
			{
				response.EnsureSuccessStatusCode();
				Stream contentStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
				try
				{
					IngestFeedStream(seriesId, feedUrl, contentStream);
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

			bool shouldSubscribe = subscribe && !string.IsNullOrEmpty(userName);
			if (shouldSubscribe)
			{
				string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
				m_db.SetSubscribed(seriesId, userName, true, nowIso);
			}

			return GetSeries(seriesId);
		}
	}
}
