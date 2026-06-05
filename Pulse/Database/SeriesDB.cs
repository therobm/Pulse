using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Pulse.Series;

namespace Pulse.Database
{
	/// <summary>
	/// SQLite-backed persistence for the podcast / audiobook "series" data
	/// layer. Stateless: holds no dictionaries and never caches a row. Load
	/// methods return collections (or null for single-row misses); Upsert
	/// methods write a single row. Connection lifetime is per-call --
	/// OpenConnection / try / finally / Close on every method, matching the
	/// PulseDB shape.
	/// </summary>
	public class SeriesDB
	{
		private SeriesDBConnector m_connector;

		public SeriesDB(SeriesDBConnector connector)
		{
			m_connector = connector;
		}

		public List<SeriesInfo> LoadAllSeries()
		{
			List<SeriesInfo> result = new List<SeriesInfo>();
			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT id, series_type, title, author, description, artwork_path, date_added FROM series;";
				SqliteDataReader reader = command.ExecuteReader();
				try
				{
					while (reader.Read())
					{
						SeriesInfo series = ReadSeriesRow(reader);
						result.Add(series);
					}
				}
				finally
				{
					reader.Close();
				}
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		public SeriesInfo LoadSeries(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return null;
			}

			SeriesInfo found = null;
			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT id, series_type, title, author, description, artwork_path, date_added FROM series WHERE id = $id;";
				command.Parameters.AddWithValue("$id", id);

				SqliteDataReader reader = command.ExecuteReader();
				try
				{
					if (reader.Read())
					{
						found = ReadSeriesRow(reader);
					}
				}
				finally
				{
					reader.Close();
				}
			}
			finally
			{
				connection.Close();
			}
			return found;
		}

		public void UpsertSeries(SeriesInfo series)
		{
			if (series == null)
			{
				return;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = @"INSERT INTO series (id, series_type, title, author, description, artwork_path, date_added)
					VALUES ($id, $series_type, $title, $author, $description, $artwork_path, $date_added)
					ON CONFLICT(id) DO UPDATE SET
						series_type = excluded.series_type,
						title = excluded.title,
						author = excluded.author,
						description = excluded.description,
						artwork_path = excluded.artwork_path,
						date_added = excluded.date_added;";
				command.Parameters.AddWithValue("$id", series.Id);
				command.Parameters.AddWithValue("$series_type", series.Type.ToString());
				command.Parameters.AddWithValue("$title", series.Title);
				command.Parameters.AddWithValue("$author", series.Author);
				command.Parameters.AddWithValue("$description", series.Description);
				command.Parameters.AddWithValue("$artwork_path", series.ArtworkPath);
				command.Parameters.AddWithValue("$date_added", series.DateAdded);
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		public void DeleteSeries(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "DELETE FROM series WHERE id = $id;";
				command.Parameters.AddWithValue("$id", id);
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		public PodcastFeedInfo LoadPodcastFeed(string seriesId)
		{
			if (string.IsNullOrEmpty(seriesId))
			{
				return null;
			}

			PodcastFeedInfo found = null;
			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT series_id, feed_url, last_polled, poll_interval_minutes, retention_policy, retention_value, auto_download FROM podcast_feeds WHERE series_id = $series_id;";
				command.Parameters.AddWithValue("$series_id", seriesId);

				SqliteDataReader reader = command.ExecuteReader();
				try
				{
					if (reader.Read())
					{
						found = ReadPodcastFeedRow(reader);
					}
				}
				finally
				{
					reader.Close();
				}
			}
			finally
			{
				connection.Close();
			}
			return found;
		}

		public List<PodcastFeedInfo> LoadAllPodcastFeeds()
		{
			List<PodcastFeedInfo> result = new List<PodcastFeedInfo>();
			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT series_id, feed_url, last_polled, poll_interval_minutes, retention_policy, retention_value, auto_download FROM podcast_feeds;";
				SqliteDataReader reader = command.ExecuteReader();
				try
				{
					while (reader.Read())
					{
						PodcastFeedInfo feed = ReadPodcastFeedRow(reader);
						result.Add(feed);
					}
				}
				finally
				{
					reader.Close();
				}
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		public void UpsertPodcastFeed(PodcastFeedInfo feed)
		{
			if (feed == null)
			{
				return;
			}

			int autoDownloadInt = 0;
			if (feed.AutoDownload)
			{
				autoDownloadInt = 1;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = @"INSERT INTO podcast_feeds (series_id, feed_url, last_polled, poll_interval_minutes, retention_policy, retention_value, auto_download)
					VALUES ($series_id, $feed_url, $last_polled, $poll_interval_minutes, $retention_policy, $retention_value, $auto_download)
					ON CONFLICT(series_id) DO UPDATE SET
						feed_url = excluded.feed_url,
						last_polled = excluded.last_polled,
						poll_interval_minutes = excluded.poll_interval_minutes,
						retention_policy = excluded.retention_policy,
						retention_value = excluded.retention_value,
						auto_download = excluded.auto_download;";
				command.Parameters.AddWithValue("$series_id", feed.SeriesId);
				command.Parameters.AddWithValue("$feed_url", feed.FeedUrl);
				command.Parameters.AddWithValue("$last_polled", feed.LastPolled);
				command.Parameters.AddWithValue("$poll_interval_minutes", feed.PollIntervalMinutes);
				command.Parameters.AddWithValue("$retention_policy", feed.Retention.ToString());
				command.Parameters.AddWithValue("$retention_value", feed.RetentionValue);
				command.Parameters.AddWithValue("$auto_download", autoDownloadInt);
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		public List<SeriesItemInfo> LoadItemsForSeries(string seriesId)
		{
			List<SeriesItemInfo> result = new List<SeriesItemInfo>();
			if (string.IsNullOrEmpty(seriesId))
			{
				return result;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT id, series_id, guid, title, description, duration_seconds, order_index, published_date, media_source_url, local_path, file_size_bytes, download_state FROM series_items WHERE series_id = $series_id ORDER BY order_index ASC;";
				command.Parameters.AddWithValue("$series_id", seriesId);

				SqliteDataReader reader = command.ExecuteReader();
				try
				{
					while (reader.Read())
					{
						SeriesItemInfo item = ReadItemRow(reader);
						result.Add(item);
					}
				}
				finally
				{
					reader.Close();
				}
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		public List<SeriesItemInfo> LoadDownloadedItemsForSeries(string seriesId)
		{
			List<SeriesItemInfo> result = new List<SeriesItemInfo>();
			if (string.IsNullOrEmpty(seriesId))
			{
				return result;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT id, series_id, guid, title, description, duration_seconds, order_index, published_date, media_source_url, local_path, file_size_bytes, download_state FROM series_items WHERE series_id = $series_id AND local_path IS NOT NULL AND local_path <> '' ORDER BY order_index ASC;";
				command.Parameters.AddWithValue("$series_id", seriesId);

				SqliteDataReader reader = command.ExecuteReader();
				try
				{
					while (reader.Read())
					{
						SeriesItemInfo item = ReadItemRow(reader);
						result.Add(item);
					}
				}
				finally
				{
					reader.Close();
				}
			}
			finally
			{
				connection.Close();
			}
			return result;
		}

		public SeriesItemInfo LoadItem(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return null;
			}

			SeriesItemInfo found = null;
			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT id, series_id, guid, title, description, duration_seconds, order_index, published_date, media_source_url, local_path, file_size_bytes, download_state FROM series_items WHERE id = $id;";
				command.Parameters.AddWithValue("$id", id);

				SqliteDataReader reader = command.ExecuteReader();
				try
				{
					if (reader.Read())
					{
						found = ReadItemRow(reader);
					}
				}
				finally
				{
					reader.Close();
				}
			}
			finally
			{
				connection.Close();
			}
			return found;
		}

		public void UpsertItem(SeriesItemInfo item)
		{
			if (item == null)
			{
				return;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = @"INSERT INTO series_items (id, series_id, guid, title, description, duration_seconds, order_index, published_date, media_source_url, local_path, file_size_bytes, download_state)
					VALUES ($id, $series_id, $guid, $title, $description, $duration_seconds, $order_index, $published_date, $media_source_url, $local_path, $file_size_bytes, $download_state)
					ON CONFLICT(id) DO UPDATE SET
						series_id = excluded.series_id,
						guid = excluded.guid,
						title = excluded.title,
						description = excluded.description,
						duration_seconds = excluded.duration_seconds,
						order_index = excluded.order_index,
						published_date = excluded.published_date,
						media_source_url = excluded.media_source_url,
						local_path = excluded.local_path,
						file_size_bytes = excluded.file_size_bytes,
						download_state = excluded.download_state;";
				command.Parameters.AddWithValue("$id", item.Id);
				command.Parameters.AddWithValue("$series_id", item.SeriesId);
				command.Parameters.AddWithValue("$guid", item.Guid);
				command.Parameters.AddWithValue("$title", item.Title);
				command.Parameters.AddWithValue("$description", item.Description);
				command.Parameters.AddWithValue("$duration_seconds", item.DurationSeconds);
				command.Parameters.AddWithValue("$order_index", item.OrderIndex);
				command.Parameters.AddWithValue("$published_date", item.PublishedDate);
				command.Parameters.AddWithValue("$media_source_url", item.MediaSourceUrl);
				command.Parameters.AddWithValue("$local_path", item.LocalPath);
				command.Parameters.AddWithValue("$file_size_bytes", item.FileSizeBytes);
				command.Parameters.AddWithValue("$download_state", item.DownloadState.ToString());
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		public void DeleteItem(string id)
		{
			if (string.IsNullOrEmpty(id))
			{
				return;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "DELETE FROM series_items WHERE id = $id;";
				command.Parameters.AddWithValue("$id", id);
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		public ItemProgressInfo LoadProgress(string itemId, string userName)
		{
			if (string.IsNullOrEmpty(itemId))
			{
				return null;
			}
			if (string.IsNullOrEmpty(userName))
			{
				return null;
			}

			ItemProgressInfo found = null;
			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = "SELECT item_id, user_name, position_seconds, completed, last_played FROM item_progress WHERE item_id = $item_id AND user_name = $user_name;";
				command.Parameters.AddWithValue("$item_id", itemId);
				command.Parameters.AddWithValue("$user_name", userName);

				SqliteDataReader reader = command.ExecuteReader();
				try
				{
					if (reader.Read())
					{
						found = ReadProgressRow(reader);
					}
				}
				finally
				{
					reader.Close();
				}
			}
			finally
			{
				connection.Close();
			}
			return found;
		}

		public void UpsertProgress(ItemProgressInfo progress)
		{
			if (progress == null)
			{
				return;
			}

			int completedInt = 0;
			if (progress.Completed)
			{
				completedInt = 1;
			}

			SqliteConnection connection = m_connector.OpenConnection();
			try
			{
				SqliteCommand command = connection.CreateCommand();
				command.CommandText = @"INSERT INTO item_progress (item_id, user_name, position_seconds, completed, last_played)
					VALUES ($item_id, $user_name, $position_seconds, $completed, $last_played)
					ON CONFLICT(item_id, user_name) DO UPDATE SET
						position_seconds = excluded.position_seconds,
						completed = excluded.completed,
						last_played = excluded.last_played;";
				command.Parameters.AddWithValue("$item_id", progress.ItemId);
				command.Parameters.AddWithValue("$user_name", progress.UserName);
				command.Parameters.AddWithValue("$position_seconds", progress.PositionSeconds);
				command.Parameters.AddWithValue("$completed", completedInt);
				command.Parameters.AddWithValue("$last_played", progress.LastPlayed);
				command.ExecuteNonQuery();
			}
			finally
			{
				connection.Close();
			}
		}

		private SeriesInfo ReadSeriesRow(SqliteDataReader reader)
		{
			SeriesInfo series = new SeriesInfo();
			series.Id = ReadString(reader, 0);

			string typeString = ReadString(reader, 1);
			eSeriesType parsedType;
			bool typeParsed = Enum.TryParse<eSeriesType>(typeString, out parsedType);
			if (typeParsed)
			{
				series.Type = parsedType;
			}

			series.Title = ReadString(reader, 2);
			series.Author = ReadString(reader, 3);
			series.Description = ReadString(reader, 4);
			series.ArtworkPath = ReadString(reader, 5);
			series.DateAdded = ReadString(reader, 6);
			return series;
		}

		private PodcastFeedInfo ReadPodcastFeedRow(SqliteDataReader reader)
		{
			PodcastFeedInfo feed = new PodcastFeedInfo();
			feed.SeriesId = ReadString(reader, 0);
			feed.FeedUrl = ReadString(reader, 1);
			feed.LastPolled = ReadString(reader, 2);
			feed.PollIntervalMinutes = ReadInt(reader, 3, 60);

			string retentionString = ReadString(reader, 4);
			eRetentionPolicy parsedRetention;
			bool retentionParsed = Enum.TryParse<eRetentionPolicy>(retentionString, out parsedRetention);
			if (retentionParsed)
			{
				feed.Retention = parsedRetention;
			}

			feed.RetentionValue = ReadInt(reader, 5, 0);

			int autoDownloadInt = ReadInt(reader, 6, 0);
			if (autoDownloadInt != 0)
			{
				feed.AutoDownload = true;
			}
			else
			{
				feed.AutoDownload = false;
			}
			return feed;
		}

		private SeriesItemInfo ReadItemRow(SqliteDataReader reader)
		{
			SeriesItemInfo item = new SeriesItemInfo();
			item.Id = ReadString(reader, 0);
			item.SeriesId = ReadString(reader, 1);
			item.Guid = ReadString(reader, 2);
			item.Title = ReadString(reader, 3);
			item.Description = ReadString(reader, 4);
			item.DurationSeconds = ReadInt(reader, 5, 0);
			item.OrderIndex = ReadInt(reader, 6, 0);
			item.PublishedDate = ReadString(reader, 7);
			item.MediaSourceUrl = ReadString(reader, 8);
			item.LocalPath = ReadString(reader, 9);
			item.FileSizeBytes = ReadLong(reader, 10, 0);

			string stateString = ReadString(reader, 11);
			eDownloadState parsedState;
			bool stateParsed = Enum.TryParse<eDownloadState>(stateString, out parsedState);
			if (stateParsed)
			{
				item.DownloadState = parsedState;
			}
			return item;
		}

		private ItemProgressInfo ReadProgressRow(SqliteDataReader reader)
		{
			ItemProgressInfo progress = new ItemProgressInfo();
			progress.ItemId = ReadString(reader, 0);
			progress.UserName = ReadString(reader, 1);
			progress.PositionSeconds = ReadInt(reader, 2, 0);

			int completedInt = ReadInt(reader, 3, 0);
			if (completedInt != 0)
			{
				progress.Completed = true;
			}
			else
			{
				progress.Completed = false;
			}

			progress.LastPlayed = ReadString(reader, 4);
			return progress;
		}

		private string ReadString(SqliteDataReader reader, int columnIndex)
		{
			if (reader.IsDBNull(columnIndex))
			{
				return "";
			}
			return reader.GetString(columnIndex);
		}

		private int ReadInt(SqliteDataReader reader, int columnIndex, int whenNull)
		{
			if (reader.IsDBNull(columnIndex))
			{
				return whenNull;
			}
			return reader.GetInt32(columnIndex);
		}

		private long ReadLong(SqliteDataReader reader, int columnIndex, long whenNull)
		{
			if (reader.IsDBNull(columnIndex))
			{
				return whenNull;
			}
			return reader.GetInt64(columnIndex);
		}
	}
}
