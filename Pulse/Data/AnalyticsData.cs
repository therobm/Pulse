using Pulse.DataStorage;
using Pulse.Services;
using PulseAPI.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Pulse.Data
{

	public class AnalyticsData
	{
		/// <summary>
		/// userID, 
		/// </summary>
		private Dictionary<string, AnalyticRecord> m_analytics = new Dictionary<string, AnalyticRecord>();
		private object m_lock = new object();
		private PulseDataStore m_data;

		private Timer m_saveTimer;

		public AnalyticsData(PulseConfig config)
		{
			string analyticsDB = "analytics.db";
#if DEBUG
			analyticsDB = "analytics_staging.db";
#endif
			string dbPath = Path.Combine(config.PulseDataPath, analyticsDB);
			m_data = new PulseDataStore(dbPath);


			m_saveTimer = new Timer(SaveDB, null, 10000, 10000);
		}

		private void SaveDB(object state)
		{
			Save();
		}
		/// <summary>
		/// Hydrate the in-memory dictionary from the store. Call once at startup.
		/// </summary>
		public void Load()
		{
			List<AnaliticUserItem> records = m_data.LoadList<AnaliticUserItem>(eDataType.AnalyticRecordItem);
			lock (m_lock)
			{
				for (int i = 0; i < records.Count; i++)
				{
					AnaliticUserItem item = records[i];
					if (item == null)
					{
						continue;
					}
					if (string.IsNullOrEmpty(item.ItemID))
					{
						continue;
					}

					if (!m_analytics.ContainsKey(item.UserID))
						m_analytics.Add(item.UserID, new AnalyticRecord());

					
					switch (item.AnalyticType)
					{
						case AnalyticType.Track:
							m_analytics[item.UserID].m_tracks.Add(item.ItemID, item);
							break;
						case AnalyticType.Album:
							m_analytics[item.UserID].m_albums.Add(item.ItemID, item);
							break;
						case AnalyticType.Artist:
							m_analytics[item.UserID].m_artists.Add(item.ItemID, item);
							break;
						case AnalyticType.Playlist:
							m_analytics[item.UserID].m_playlists.Add(item.ItemID, item);
							break;
						default:
							//noop
							break;
					}
				}
			}
		}

		public void Save()
		{
			List<AnaliticUserItem> dirtyRecords = new List<AnaliticUserItem>();
			foreach (AnalyticRecord record in m_analytics.Values)
			{
				foreach (AnaliticUserItem item in record.m_tracks.Values)
					if (item.m_bIsDirty) { dirtyRecords.Add(item); }
				foreach (AnaliticUserItem item in record.m_albums.Values)
					if (item.m_bIsDirty) { dirtyRecords.Add(item); }
				foreach (AnaliticUserItem item in record.m_artists.Values)
					if (item.m_bIsDirty) { dirtyRecords.Add(item); }
				foreach (AnaliticUserItem item in record.m_playlists.Values)
					if (item.m_bIsDirty) { dirtyRecords.Add(item); }
			}

			for (int i = 0; i < dirtyRecords.Count; i++)
			{
				m_data.Save<AnaliticUserItem>(eDataType.AnalyticRecordItem, dirtyRecords[i]);
			}
		}

		public void OnItemPlayed(string userId, ePulseWireType type, string itemId)
		{
			AnaliticUserItem item = GetItem(userId, type, itemId);
			if (item != null)
			{
				item.PlayCount++;
				item.m_bIsDirty = true;
			}
		}
		public void OnItemStopped(string userId, ePulseWireType type, string itemId, float totalSecondsPlayed)
		{
			AnaliticUserItem item = GetItem(userId, type, itemId);
			if (item != null)
			{
				item.TotalPlayedSeconds += totalSecondsPlayed;
				item.m_bIsDirty = true;
			}
		}

		private AnalyticRecord GetUserRecord(string userId)
		{
			if (!m_analytics.ContainsKey(userId))
			{
				m_analytics.Add(userId, new AnalyticRecord());
			}
			return m_analytics[userId];
		}
		private AnaliticUserItem GetItem(string userId, ePulseWireType type, string itemId)
		{
			AnalyticRecord record = GetUserRecord(userId);
			AnaliticUserItem item = null;
			switch (type)
			{
				case ePulseWireType.Track:
					if (!record.m_tracks.TryGetValue(itemId, out item))
					{
						item = new AnaliticUserItem();
						item.AnalyticType = AnalyticType.Track;
						item.ItemID = itemId;
						item.UserID = userId;
						record.m_tracks[itemId] = item;
					}
					break;
				case ePulseWireType.Album:
					if (!record.m_albums.TryGetValue(itemId, out item))
					{
						item = new AnaliticUserItem();
						item.AnalyticType = AnalyticType.Album;
						item.ItemID = itemId;
						record.m_albums[itemId] = item;
					}
					break;
				case ePulseWireType.Artist:
					if (!record.m_artists.TryGetValue(itemId, out item))
					{
						item = new AnaliticUserItem();
						item.AnalyticType = AnalyticType.Artist;
						item.ItemID = itemId;
						record.m_artists[itemId] = item;
					}
					break;
				case ePulseWireType.Playlist:
					if (!record.m_playlists.TryGetValue(itemId, out item))
					{
						item = new AnaliticUserItem();
						item.AnalyticType = AnalyticType.Playlist;
						item.ItemID = itemId;
						record.m_playlists[itemId] = item;
					}
					break;
			}
			return item;
		}
	}
}
