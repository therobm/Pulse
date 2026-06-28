
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
		public float GetScore(PulseData pulseData, AnalyticsData analyticsData)
		{
			float score = 0;

			switch (AnalyticType)
			{
				case eAnalyticType.Track:
					{
						//Audiobook chapters don't get rated ever
						if (ItemID.StartsWith("ch"))
							return 0;

						TrackData track = pulseData.GetTrack(ItemID);
						if (track == null)
						{
							Log.Error("Unknown track: " + ItemID);
							return 0;
						}
						float trackDuration = track.DurationSeconds;

						double maxPlayTime = PlayCount * trackDuration;
						if (maxPlayTime > 0)
							score = (float)(TotalPlayedSeconds / maxPlayTime);
						break;
					}
				case eAnalyticType.Album:
					AlbumData album = pulseData.GetAlbum(ItemID);
					if (album == null)
					{
						Log.Error("Unknown album: " + ItemID);
						return 0;
					}
					List<string> trackIds = new List<string>();
					for (int i = 0; i < album.TrackCount; i++)
					{
						trackIds.Add(album.GetTrackId(i));
					}
					List<AnaliticUserItem> tracks = analyticsData.GetRankedItems(trackIds, eAnalyticType.Track);
					score = 0;
					if (tracks.Count > 0)
					{
						foreach (AnaliticUserItem track in tracks)
						{
							if (track == null)
							{
								Log.Error("Bad track in album ranking");
								continue;
							}
							score += track.GetScore(pulseData, analyticsData);
						}
						score /= tracks.Count;
					}
					break;
				case eAnalyticType.Artist:
					ArtistData artist = pulseData.GetArtist(ItemID);
					if (artist == null)
					{
						Log.Error("Unknown artist: " + ItemID);
						return 0;
					}
					List<string> artistTrackIds = new List<string>();
					List<AlbumData> artistAlbums = artist.GetAlbums();
					for (int i = 0; i < artistAlbums.Count; i++)
					{
						AlbumData artistAlbum = artistAlbums[i];
						List<TrackData> albumTracks = artistAlbum.GetTracks();
						for (int j = 0; j < albumTracks.Count; j++)
						{
							artistTrackIds.Add(albumTracks[j].Id);
						}
					}
					List<AnaliticUserItem> artistTracks = analyticsData.GetRankedItems(artistTrackIds, eAnalyticType.Track);
					score = 0;
					if (artistTracks.Count > 0)
					{
						foreach (AnaliticUserItem track in artistTracks)
						{
							if (track == null)
							{
								Log.Error("Bad track in artist ranking");
								continue;
							}
							score += track.GetScore(pulseData, analyticsData);
						}
						score /= artistTracks.Count;
					}
					break;
				case eAnalyticType.Playlist:
					PlaylistData playlist = pulseData.GetPlaylist(ItemID);
					if (playlist == null)
					{
						Log.Error("Unknown playlist: " + ItemID);
						return 0;
					}
					List<AnaliticUserItem> playlistTracks = analyticsData.GetRankedItems(playlist.TrackIds, eAnalyticType.Track);
					score = 0;
					if (playlistTracks.Count > 0)
					{
						foreach (AnaliticUserItem track in playlistTracks)
						{
							if (track == null)
							{
								Log.Error("Bad track in playlist ranking");
								continue;
							}
							score += track.GetScore(pulseData, analyticsData);
						}
						score /= playlistTracks.Count;
					}
					break;
				default:
					//noop
					break;
			}
			

			return score;
		}

	}
}
