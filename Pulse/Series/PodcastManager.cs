using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using Pulse.Database;
using Pulse.MusicLibrary;

namespace Pulse.Series
{
	/// <summary>
	/// Facade over the series database for the podcast subset, sibling to
	/// MusicManager. Owns a SeriesDBConnector pointed at PulseData/
	/// pulse_series_{env}.db plus the SeriesDB it talks through, and (when
	/// Run() is called) a single background poll thread that walks every
	/// feed on its configured interval, refreshes the RSS, downloads
	/// pending media, and applies the retention policy. The single static
	/// HttpClient is reused for every fetch so RSS polls and media GETs
	/// don't churn TCP/TLS state.
	/// </summary>
	public class PodcastManager
	{
		private static readonly HttpClient s_httpClient = BuildHttpClient();

		private SeriesDBConnector m_connector;
		private SeriesDB m_db;
		private string m_musicPath;
		private Thread m_pollThread;

		public PodcastManager(PulseConfig config)
		{
			string environmentName = config.DatabaseEnvironment;
			if (string.IsNullOrWhiteSpace(environmentName))
			{
				environmentName = "Production";
			}

			m_musicPath = config.MusicPath;

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
		/// Returns the on-disk directory where episode media (and the
		/// cached artwork file) for this series live. Creates the path on
		/// demand so callers can immediately write into it without their
		/// own mkdir step. Layout: {MusicPath}/PulseData/Podcasts/{seriesId}.
		/// </summary>
		public string GetSeriesMediaDir(string seriesId)
		{
			string podcastsRoot = Path.Combine(m_musicPath, "PulseData", "Podcasts");
			string seriesDir = Path.Combine(podcastsRoot, seriesId);
			if (!Directory.Exists(seriesDir))
			{
				Directory.CreateDirectory(seriesDir);
			}
			return seriesDir;
		}

		/// <summary>
		/// Offline-testable core of feed ingest: parse the supplied stream,
		/// upsert the series row, refresh series_items, and update only the
		/// LastPolled column on the existing podcast_feeds row. Feed
		/// settings (PollIntervalMinutes, Retention, RetentionValue,
		/// AutoDownload) are NOT touched here -- they are set once at
		/// AddPodcast time and only changed by deliberate user action; a
		/// poll cycle must never clobber them.
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
			// Preserve a previously-cached local artwork path across a
			// re-poll; otherwise a freshly parsed (remote) URL would
			// overwrite the localised one and re-trigger a fetch.
			if (existingSeries != null && !string.IsNullOrEmpty(existingSeries.ArtworkPath) && !existingSeries.ArtworkPath.StartsWith("http"))
			{
				series.ArtworkPath = existingSeries.ArtworkPath;
			}
			series.DateAdded = dateAdded;
			m_db.UpsertSeries(series);

			m_db.SetFeedLastPolled(seriesId, nowIso);

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
		/// First-add only: creates the podcast_feeds row with default
		/// PollIntervalMinutes / Retention / AutoDownload values if none
		/// exists yet -- subsequent calls leave the user's settings alone.
		/// After ingest the series artwork is cached locally and any
		/// already-eligible items begin downloading.
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

			PodcastFeedInfo existingFeed = m_db.LoadPodcastFeed(seriesId);
			if (existingFeed == null)
			{
				string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
				PodcastFeedInfo feed = new PodcastFeedInfo();
				feed.SeriesId = seriesId;
				feed.FeedUrl = feedUrl;
				feed.PollIntervalMinutes = 60;
				feed.Retention = eRetentionPolicy.KeepN;
				feed.RetentionValue = 10;
				feed.AutoDownload = true;
				feed.LastPolled = nowIso;
				m_db.UpsertPodcastFeed(feed);
			}

			bool shouldSubscribe = subscribe && !string.IsNullOrEmpty(userName);
			if (shouldSubscribe)
			{
				string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
				m_db.SetSubscribed(seriesId, userName, true, nowIso);
			}

			SeriesInfo storedSeries = GetSeries(seriesId);
			if (storedSeries != null)
			{
				CacheArtwork(storedSeries);
			}

			Thread downloadThread = new Thread(RunInitialDownload);
			downloadThread.IsBackground = true;
			downloadThread.Name = "Pulse.PodcastInitialDownload";
			downloadThread.Start(seriesId);

			return GetSeries(seriesId);
		}

		/// <summary>
		/// Background-thread entry point that runs the first DownloadPendingForFeed
		/// for a newly added podcast off the request thread. The HTTP handler
		/// returns as soon as the feed has been ingested and the series row is
		/// usable; the (potentially hundreds-of-MB) media downloads happen
		/// behind it. Any exception is caught and logged -- an uncaught throw
		/// out of a background thread would tear down the process.
		/// </summary>
		private void RunInitialDownload(object seriesIdObject)
		{
			string seriesId = (string)seriesIdObject;
			try
			{
				DownloadPendingForFeed(seriesId);
			}
			catch (Exception ex)
			{
				Log.Error(-1, "Initial podcast download failed for " + seriesId + ": " + ex.Message);
			}
		}

		/// <summary>
		/// Downloads one item's media to {MusicPath}/PulseData/Podcasts/
		/// {seriesId}/{itemId}{ext}, stamping LocalPath, FileSizeBytes,
		/// DurationSeconds (probed from TagLib when the RSS had none), and
		/// DownloadState back onto the row. Streams the HTTP body straight
		/// to disk so memory stays flat regardless of episode size. Never
		/// re-throws -- a single bad download must not stop the poll loop;
		/// the item is marked Failed and the next cycle can retry.
		/// </summary>
		public void DownloadItem(SeriesItemInfo item)
		{
			if (item == null)
			{
				return;
			}
			if (string.IsNullOrEmpty(item.MediaSourceUrl))
			{
				return;
			}
			bool alreadyOnDisk = !string.IsNullOrEmpty(item.LocalPath) && File.Exists(item.LocalPath);
			if (alreadyOnDisk)
			{
				return;
			}

			item.DownloadState = eDownloadState.Downloading;
			m_db.UpsertItem(item);

			string extension = ExtensionForMediaSourceUrl(item.MediaSourceUrl);
			string seriesDir = GetSeriesMediaDir(item.SeriesId);
			string targetPath = Path.Combine(seriesDir, item.Id + extension);

			try
			{
				HttpResponseMessage response = s_httpClient.GetAsync(item.MediaSourceUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
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
				item.LocalPath = targetPath;
				item.FileSizeBytes = info.Length;
				if (item.DurationSeconds == 0)
				{
					item.DurationSeconds = ProbeDurationSeconds(targetPath);
				}
				item.DownloadState = eDownloadState.Downloaded;
				m_db.UpsertItem(item);
				Log.Info(-1, "Podcast downloaded: " + item.Title);
			}
			catch (Exception ex)
			{
				item.DownloadState = eDownloadState.Failed;
				m_db.UpsertItem(item);
				Log.Warning(-1, "Podcast download failed: " + item.Title + " -- " + ex.Message);
				if (File.Exists(targetPath))
				{
					try
					{
						File.Delete(targetPath);
					}
					catch (Exception deleteEx)
					{
						Log.Warning(-1, "Podcast partial delete failed: " + targetPath + " -- " + deleteEx.Message);
					}
				}
			}
		}

		/// <summary>
		/// Walks the keep set produced by the feed's retention policy and
		/// invokes DownloadItem for any keep-set item that isn't already
		/// Downloaded (or whose LocalPath no longer points at a real file).
		/// Honors AutoDownload: if the user turned auto-download off, this
		/// is a no-op even when retention selects items.
		/// </summary>
		public void DownloadPendingForFeed(string seriesId)
		{
			PodcastFeedInfo feed = m_db.LoadPodcastFeed(seriesId);
			if (feed == null)
			{
				return;
			}
			if (!feed.AutoDownload)
			{
				return;
			}

			List<SeriesItemInfo> items = m_db.LoadItemsForSeries(seriesId);
			List<SeriesItemInfo> keepSet = ComputeKeepSet(items, feed);

			int keepCount = keepSet.Count;
			for (int keepIndex = 0; keepIndex < keepCount; keepIndex++)
			{
				SeriesItemInfo candidate = keepSet[keepIndex];
				bool fileMissing = string.IsNullOrEmpty(candidate.LocalPath) || !File.Exists(candidate.LocalPath);
				bool needsDownload = candidate.DownloadState != eDownloadState.Downloaded || fileMissing;
				if (needsDownload)
				{
					DownloadItem(candidate);
				}
			}
		}

		/// <summary>
		/// Best-effort duration probe via TagLib. Returns 0 when the file
		/// is unsupported or corrupt rather than propagating the exception
		/// -- a missing duration is a cosmetic loss, not a download
		/// failure. Used by DownloadItem only when the feed didn't supply
		/// an itunes:duration.
		/// </summary>
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
				Log.Warning(-1, "Podcast duration probe failed: " + filePath + " -- " + ex.Message);
				return 0;
			}
		}

		/// <summary>
		/// Applies the feed's retention policy: items not in the keep set
		/// have their local file deleted and their row reset to
		/// Discovered / LocalPath="" (the metadata row is preserved so the
		/// item can be re-downloaded later without re-ingesting the feed).
		/// KeepAll culls nothing; KeepN keeps the newest N by
		/// PublishedDate; KeepDays keeps anything within the last N days.
		/// </summary>
		public void ApplyRetention(string seriesId)
		{
			PodcastFeedInfo feed = m_db.LoadPodcastFeed(seriesId);
			if (feed == null)
			{
				return;
			}
			if (feed.Retention == eRetentionPolicy.KeepAll)
			{
				return;
			}

			List<SeriesItemInfo> items = m_db.LoadItemsForSeries(seriesId);
			List<SeriesItemInfo> keepSet = ComputeKeepSet(items, feed);
			HashSet<string> keepIds = new HashSet<string>();
			int keepCount = keepSet.Count;
			for (int keepIndex = 0; keepIndex < keepCount; keepIndex++)
			{
				keepIds.Add(keepSet[keepIndex].Id);
			}

			int itemCount = items.Count;
			for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
			{
				SeriesItemInfo item = items[itemIndex];
				if (item.DownloadState != eDownloadState.Downloaded)
				{
					continue;
				}
				bool kept = keepIds.Contains(item.Id);
				if (kept)
				{
					continue;
				}

				if (!string.IsNullOrEmpty(item.LocalPath) && File.Exists(item.LocalPath))
				{
					try
					{
						File.Delete(item.LocalPath);
					}
					catch (Exception ex)
					{
						Log.Warning(-1, "Podcast retention delete failed: " + item.LocalPath + " -- " + ex.Message);
					}
				}
				item.LocalPath = "";
				item.DownloadState = eDownloadState.Discovered;
				m_db.UpsertItem(item);
			}
		}

		/// <summary>
		/// Localises a series' artwork: if ArtworkPath is still a remote
		/// URL, downloads it to {seriesDir}/artwork.jpg and rewrites the
		/// series row to point at the local file. Skips work if a local
		/// artwork file is already present on disk. Failures leave the
		/// remote URL intact -- a missing thumbnail is not worth aborting
		/// a poll cycle for.
		/// </summary>
		public void CacheArtwork(SeriesInfo series)
		{
			if (series == null)
			{
				return;
			}
			if (string.IsNullOrEmpty(series.ArtworkPath))
			{
				return;
			}
			if (!series.ArtworkPath.StartsWith("http"))
			{
				return;
			}

			string seriesDir = GetSeriesMediaDir(series.Id);
			string artworkPath = Path.Combine(seriesDir, "artwork.jpg");
			if (File.Exists(artworkPath))
			{
				series.ArtworkPath = artworkPath;
				m_db.UpsertSeries(series);
				return;
			}

			try
			{
				HttpResponseMessage response = s_httpClient.GetAsync(series.ArtworkPath, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
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

				series.ArtworkPath = artworkPath;
				m_db.UpsertSeries(series);
			}
			catch (Exception ex)
			{
				Log.Warning(-1, "Podcast artwork cache failed: " + series.Title + " -- " + ex.Message);
				if (File.Exists(artworkPath))
				{
					try
					{
						File.Delete(artworkPath);
					}
					catch (Exception deleteEx)
					{
						Log.Warning(-1, "Podcast artwork partial delete failed: " + artworkPath + " -- " + deleteEx.Message);
					}
				}
			}
		}

		/// <summary>
		/// One complete refresh of a single feed: HTTP GET the feed URL,
		/// ingest into the DB (series + items + LastPolled), localise the
		/// artwork if still remote, pull any items that retention says
		/// should be on disk, and cull anything that retention no longer
		/// wants. Any feed-level failure (network, XML, etc.) is logged
		/// and swallowed so a single bad feed never tears down the poll
		/// thread.
		/// </summary>
		public void RefreshFeed(string seriesId)
		{
			PodcastFeedInfo feed = m_db.LoadPodcastFeed(seriesId);
			if (feed == null)
			{
				return;
			}
			if (string.IsNullOrEmpty(feed.FeedUrl))
			{
				return;
			}

			try
			{
				HttpResponseMessage response = s_httpClient.GetAsync(feed.FeedUrl).GetAwaiter().GetResult();
				try
				{
					response.EnsureSuccessStatusCode();
					Stream contentStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
					try
					{
						IngestFeedStream(seriesId, feed.FeedUrl, contentStream);
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

				SeriesInfo storedSeries = GetSeries(seriesId);
				if (storedSeries != null)
				{
					CacheArtwork(storedSeries);
				}
				DownloadPendingForFeed(seriesId);
				ApplyRetention(seriesId);
			}
			catch (Exception ex)
			{
				Log.Warning(-1, "Podcast feed refresh failed: " + seriesId + " -- " + ex.Message);
			}
		}

		/// <summary>
		/// Starts the single background poll thread. The thread walks all
		/// podcast_feeds rows once per cycle, calls RefreshFeed on any
		/// whose LastPolled is older than PollIntervalMinutes (treating an
		/// empty or unparseable LastPolled as "due"), then sleeps 60s
		/// before the next cycle. The infinite while-loop is the sanctioned
		/// pattern for a worker thread (mirrors the analytics drain).
		/// </summary>
		public void Run()
		{
			if (m_pollThread != null)
			{
				return;
			}
			m_pollThread = new Thread(PollLoop);
			m_pollThread.IsBackground = true;
			m_pollThread.Name = "Pulse.PodcastPoll";
			m_pollThread.Start();
		}

		private void PollLoop()
		{
			while (true)
			{
				try
				{
					List<PodcastFeedInfo> feeds = m_db.LoadAllPodcastFeeds();
					int feedCount = feeds.Count;
					DateTime nowUtc = DateTime.UtcNow;
					for (int feedIndex = 0; feedIndex < feedCount; feedIndex++)
					{
						PodcastFeedInfo feed = feeds[feedIndex];
						bool due = IsFeedDue(feed, nowUtc);
						if (due)
						{
							RefreshFeed(feed.SeriesId);
						}
					}
				}
				catch (Exception ex)
				{
					Log.Error(-1, "Podcast poll cycle failed: " + ex.Message);
				}
				Thread.Sleep(60000);
			}
		}

		private bool IsFeedDue(PodcastFeedInfo feed, DateTime nowUtc)
		{
			if (string.IsNullOrEmpty(feed.LastPolled))
			{
				return true;
			}
			DateTimeOffset lastPolled;
			bool parseOk = DateTimeOffset.TryParse(feed.LastPolled, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out lastPolled);
			if (!parseOk)
			{
				return true;
			}
			TimeSpan since = nowUtc - lastPolled.UtcDateTime;
			TimeSpan interval = TimeSpan.FromMinutes(feed.PollIntervalMinutes);
			return since >= interval;
		}

		/// <summary>
		/// Picks the items that should be on disk per the feed's
		/// retention policy. KeepAll returns everything; KeepN returns the
		/// newest N by PublishedDate (descending); KeepDays returns items
		/// whose PublishedDate is within the last RetentionValue days, and
		/// keeps any item with an unparseable date on the safe side.
		/// PublishedDate is ISO-8601 ("yyyy-MM-ddTHH:mm:ssZ") so a string
		/// sort is equivalent to a chronological sort.
		/// </summary>
		private List<SeriesItemInfo> ComputeKeepSet(List<SeriesItemInfo> items, PodcastFeedInfo feed)
		{
			List<SeriesItemInfo> result = new List<SeriesItemInfo>();
			if (items == null || feed == null)
			{
				return result;
			}

			int itemCount = items.Count;
			if (feed.Retention == eRetentionPolicy.KeepAll)
			{
				for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
				{
					result.Add(items[itemIndex]);
				}
				return result;
			}

			List<SeriesItemInfo> sortedByDateDesc = new List<SeriesItemInfo>(items);
			sortedByDateDesc.Sort(CompareByPublishedDescending);

			if (feed.Retention == eRetentionPolicy.KeepN)
			{
				int keep = feed.RetentionValue;
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
				for (int sortedIndex = 0; sortedIndex < upper; sortedIndex++)
				{
					result.Add(sortedByDateDesc[sortedIndex]);
				}
				return result;
			}

			if (feed.Retention == eRetentionPolicy.KeepDays)
			{
				DateTime cutoff = DateTime.UtcNow - TimeSpan.FromDays(feed.RetentionValue);
				int sortedCount = sortedByDateDesc.Count;
				for (int sortedIndex = 0; sortedIndex < sortedCount; sortedIndex++)
				{
					SeriesItemInfo candidate = sortedByDateDesc[sortedIndex];
					DateTimeOffset parsed;
					bool parseOk = DateTimeOffset.TryParse(candidate.PublishedDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed);
					if (!parseOk)
					{
						// Unparseable published-date: keep on the safe side
						// rather than evict (we'd rather hold disk than
						// silently drop a real episode).
						result.Add(candidate);
						continue;
					}
					if (parsed.UtcDateTime >= cutoff)
					{
						result.Add(candidate);
					}
				}
				return result;
			}

			return result;
		}

		private int CompareByPublishedDescending(SeriesItemInfo left, SeriesItemInfo right)
		{
			return string.CompareOrdinal(right.PublishedDate, left.PublishedDate);
		}

		/// <summary>
		/// Subscribed podcast series for one user. Thin facade over
		/// SeriesDB.LoadSubscribedSeries scoped to eSeriesType.Podcast --
		/// keeps PulseEndpoints from reaching past the manager.
		/// </summary>
		public List<SeriesInfo> GetSubscribedPodcasts(string userName)
		{
			return m_db.LoadSubscribedSeries(userName, eSeriesType.Podcast);
		}

		/// <summary>
		/// Every podcast series known to the database, subscribed or not.
		/// Used by the discovery endpoint that lists podcasts across users.
		/// </summary>
		public List<SeriesInfo> GetAllPodcasts()
		{
			return m_db.LoadAllSeriesByType(eSeriesType.Podcast);
		}

		/// <summary>
		/// Items presented to clients: only episodes whose media is on disk
		/// are returned, sorted newest-first by PublishedDate so the UI
		/// reads chronologically without re-sorting.
		/// </summary>
		public List<SeriesItemInfo> GetDownloadedItems(string seriesId)
		{
			List<SeriesItemInfo> items = m_db.LoadDownloadedItemsForSeries(seriesId);
			items.Sort(CompareByPublishedDescending);
			return items;
		}

		/// <summary>
		/// One series item by primary key. Facade over SeriesDB.LoadItem.
		/// </summary>
		public SeriesItemInfo GetItem(string id)
		{
			return m_db.LoadItem(id);
		}

		/// <summary>
		/// The podcast_feeds row for this series, or null if the series is
		/// not a podcast or has no feed configured yet.
		/// </summary>
		public PodcastFeedInfo GetFeed(string seriesId)
		{
			return m_db.LoadPodcastFeed(seriesId);
		}

		/// <summary>
		/// Per-user subscription / resume-anchor row, or null if the user
		/// has never touched this series.
		/// </summary>
		public UserSeriesInfo GetUserSeries(string seriesId, string userName)
		{
			return m_db.LoadUserSeries(seriesId, userName);
		}

		/// <summary>
		/// Per-user playback progress for one item, or null when the user
		/// has never played it.
		/// </summary>
		public ItemProgressInfo GetProgress(string itemId, string userName)
		{
			return m_db.LoadProgress(itemId, userName);
		}

		/// <summary>
		/// Count of downloaded episodes the user has not yet completed: an
		/// episode counts as unplayed when its per-user progress row is
		/// missing OR its Completed flag is still false.
		/// </summary>
		public int GetUnplayedCount(string seriesId, string userName)
		{
			List<SeriesItemInfo> downloaded = GetDownloadedItems(seriesId);
			int unplayed = 0;
			int downloadedCount = downloaded.Count;
			for (int itemIndex = 0; itemIndex < downloadedCount; itemIndex++)
			{
				SeriesItemInfo item = downloaded[itemIndex];
				ItemProgressInfo progress = m_db.LoadProgress(item.Id, userName);
				if (progress == null)
				{
					unplayed++;
					continue;
				}
				if (!progress.Completed)
				{
					unplayed++;
				}
			}
			return unplayed;
		}

		/// <summary>
		/// Subscribe or unsubscribe a user from a series. Wraps
		/// SeriesDB.SetSubscribed so callers don't have to format the
		/// date_added sentinel themselves.
		/// </summary>
		public void SetSubscribed(string seriesId, string userName, bool subscribed)
		{
			string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
			m_db.SetSubscribed(seriesId, userName, subscribed, nowIso);
		}

		/// <summary>
		/// Record playback progress for one user on one item. Upserts the
		/// item_progress row, flips Completed once playback passes 95% of
		/// the known duration (a previously-true Completed stays true so a
		/// stray seek backwards doesn't unset it), and refreshes the
		/// series resume anchor (user_series.last_item_id / last_played)
		/// so the UI can jump straight back to where they left off.
		/// </summary>
		public void SaveProgress(string itemId, string userName, int positionSeconds)
		{
			SeriesItemInfo item = m_db.LoadItem(itemId);
			if (item == null)
			{
				return;
			}

			string nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

			ItemProgressInfo existing = m_db.LoadProgress(itemId, userName);
			bool wasCompleted = false;
			if (existing != null)
			{
				wasCompleted = existing.Completed;
			}

			bool passedThreshold = false;
			if (item.DurationSeconds > 0)
			{
				int threshold = item.DurationSeconds * 95 / 100;
				if (positionSeconds > threshold)
				{
					passedThreshold = true;
				}
			}

			ItemProgressInfo progress = new ItemProgressInfo();
			progress.ItemId = itemId;
			progress.UserName = userName;
			progress.PositionSeconds = positionSeconds;
			progress.LastPlayed = nowIso;
			if (wasCompleted || passedThreshold)
			{
				progress.Completed = true;
			}
			else
			{
				progress.Completed = false;
			}
			m_db.UpsertProgress(progress);

			m_db.SetSeriesLastItem(item.SeriesId, userName, itemId, nowIso, nowIso);
		}

		/// <summary>
		/// Picks the on-disk file extension for a downloaded episode. The
		/// RSS-parsed MIME type isn't carried on SeriesItemInfo, so the
		/// URL's own path-suffix is the only signal once an item lives in
		/// the DB. Maps the common audio suffixes to .mp3 / .m4a and
		/// falls back to .mp3 (the dominant podcast format) when the URL
		/// gives no usable hint.
		/// </summary>
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
