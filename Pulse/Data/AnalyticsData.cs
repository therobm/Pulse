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
					if (string.IsNullOrEmpty(item.ItemID) || string.IsNullOrEmpty(item.UserID))
					{
						//todo: delete these they're orphaned
						continue;
					}

					if (!m_analytics.ContainsKey(item.UserID))
						m_analytics.Add(item.UserID, new AnalyticRecord());

					
					switch (item.AnalyticType)
					{
						case eAnalyticType.Track:
							m_analytics[item.UserID].m_tracks.Add(item.ItemID, item);
							break;
						case eAnalyticType.Album:
							m_analytics[item.UserID].m_albums.Add(item.ItemID, item);
							break;
						case eAnalyticType.Artist:
							m_analytics[item.UserID].m_artists.Add(item.ItemID, item);
							break;
						case eAnalyticType.Playlist:
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

		public List<AnaliticUserItem> GetRankedItems(List<eAnalyticType> types)
		{
			List<AnaliticUserItem> rankedItems = new List<AnaliticUserItem>();

			foreach (KeyValuePair<string, AnalyticRecord> kvp in m_analytics)
			{
				if (types.Count == 0)
				{
					//return everything
					types.Add(eAnalyticType.Track);
					types.Add(eAnalyticType.Artist);
					types.Add(eAnalyticType.Album);
					types.Add(eAnalyticType.Playlist);
				}

				foreach (eAnalyticType type in types)
				{
					rankedItems.AddRange(GetContainer(kvp.Value, type));
				}
			}
			
			return rankedItems;
		}

		public List<AnaliticUserItem> GetUserItems(string userId, List<eAnalyticType> types)
		{
			List<AnaliticUserItem> rankedItems = new List<AnaliticUserItem>();
			AnalyticRecord userRecord = null;
			if (!m_analytics.TryGetValue(userId, out userRecord))
				return new List<AnaliticUserItem>();

			if (types.Count == 0)
			{
				//return everything
				types.Add(eAnalyticType.Track);
				types.Add(eAnalyticType.Artist);
				types.Add(eAnalyticType.Album);
				types.Add(eAnalyticType.Playlist);
			}
			
			foreach (eAnalyticType type in types)
			{
				rankedItems.AddRange(GetContainer(userRecord, type));
			}
			
			return rankedItems;
		}

		private IEnumerable<AnaliticUserItem> GetContainer(AnalyticRecord record, eAnalyticType type)
		{
			switch (type)
			{
				case eAnalyticType.Track: return record.m_tracks.Values;
				case eAnalyticType.Album: return record.m_albums.Values;
				case eAnalyticType.Artist: return record.m_artists.Values;
				case eAnalyticType.Playlist: return record.m_playlists.Values;
				default: return Array.Empty<AnaliticUserItem>();
			}
		}

		public void OnItemPlayed(string userId, ePulseWireType type, string itemId)
		{
			AnaliticUserItem item = GetItem(userId, type, itemId);
			if (item != null)
			{
				item.PlayCount++;
				item.LastPlayed = DateTime.UtcNow;
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
						item.AnalyticType = eAnalyticType.Track;
						item.ItemID = itemId;
						item.UserID = userId;
						record.m_tracks[itemId] = item;
					}
					break;
				case ePulseWireType.Album:
					if (!record.m_albums.TryGetValue(itemId, out item))
					{
						item = new AnaliticUserItem();
						item.AnalyticType = eAnalyticType.Album;
						item.ItemID = itemId;
						item.UserID = userId;
						record.m_albums[itemId] = item;
					}
					break;
				case ePulseWireType.Artist:
					if (!record.m_artists.TryGetValue(itemId, out item))
					{
						item = new AnaliticUserItem();
						item.AnalyticType = eAnalyticType.Artist;
						item.ItemID = itemId;
						item.UserID = userId;
						record.m_artists[itemId] = item;
					}
					break;
				case ePulseWireType.Playlist:
					if (!record.m_playlists.TryGetValue(itemId, out item))
					{
						item = new AnaliticUserItem();
						item.AnalyticType = eAnalyticType.Playlist;
						item.ItemID = itemId;
						item.UserID = userId;
						record.m_playlists[itemId] = item;
					}
					break;
			}
			return item;
		}
	}
}
