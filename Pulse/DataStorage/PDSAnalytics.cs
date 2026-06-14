
using Pulse.Data;
using System;
using System.Collections.Generic;
using TagLib.Ape;
using TagLib.Riff;

namespace Pulse.DataStorage
{
	public enum eAnalyticType
	{
		Track,
		Album,
		Artist,
		Playlist
	}
	/// <summary>
	/// Reconstructed at load time, not saved to disk
	/// </summary>
	public class AnalyticRecord : PulseDataObject
	{
		public Dictionary<string, AnaliticUserItem> m_tracks = new Dictionary<string, AnaliticUserItem>();
		public Dictionary<string, AnaliticUserItem> m_artists = new Dictionary<string, AnaliticUserItem>();
		public Dictionary<string, AnaliticUserItem> m_albums = new Dictionary<string, AnaliticUserItem>();
		public Dictionary<string, AnaliticUserItem> m_playlists = new Dictionary<string, AnaliticUserItem>();
	}


	/// <summary>
	/// Per-row item data
	/// </summary>
	public class AnaliticUserItem : PulseDataObject
	{
		/// <summary>
		/// The user this record is referring to
		/// </summary>
		public string UserID;
		public string ItemID;
		public eAnalyticType AnalyticType;
		public bool IsFavorite;
		public int PlayCount;
		public double TotalPlayedSeconds;
		public DateTime LastPlayed = DateTime.MinValue;
		public AnaliticUserItem()
		{
			Id = Guid.NewGuid().ToString();	
		}
		public float GetScore(PulseData pulseData)
		{
			float score = 0;

			switch (AnalyticType)
			{
				case eAnalyticType.Track:
					{
						//todoo this should support all music object types
						TrackData track = pulseData.GetTrack(ItemID);
						float trackDuration = track.DurationSeconds;

						double maxPlayTime = PlayCount * trackDuration;
						score = (float)(TotalPlayedSeconds / maxPlayTime);
						break;
					}
				case eAnalyticType.Album:
					break;
				case eAnalyticType.Artist:
					break;
				case eAnalyticType.Playlist:
					break;
				default:
					//noop
					break;
			}
			

			return score;
		}

	}
}
