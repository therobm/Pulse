using PulseAPI.CSharp;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;


namespace Pulse.DataStorage
{
	public enum eDataType
	{
		Track,
		Album,
		Artist,
		Playlist,
		PulseAnalytics,
		Genre,
		User,
		Podcast,
		PodcastEpisode,
		Audiobook,
		AudiobookChapter,
		Diagnostic,
		AnalyticRecordItem,
	}

	public abstract class PulseDataObject
	{
		public string Id;

		[JsonIgnore]
		private bool m_bIsDirty;
		public void ClearDirty()
		{
			m_bIsDirty = false;
		}
		public bool IsDirty()
		{
			return m_bIsDirty;
		}
		public void MarkDirty()
		{
			m_bIsDirty = true;
		}

	}


	public class TrackData : PulseDataObject
	{
		public string LegacyId;
		public string Title;
		public string Artist;
		public string ArtistId;
		public string Album;
		public string AlbumId;
		public string Genre;
		[Obsolete("Replaced by RelativeFilePath")]
		public string FilePath;
		public string RelativeFilePath;
		public string CoverArtId;
		public int TrackNumber;
		public int DiscNumber;
		public int Year;
		public int DurationSeconds;
		public long FileSizeBytes;
		public string ContentType;
		public string Suffix;

	
		public DateTime LastPlayed;

		[JsonIgnore]
		public ArtistData ParentArtist;

		
		public PulseTrack BuildPulse()
		{
			PulseTrack pulseTrack = new PulseTrack();
			pulseTrack.Id = Id;
			pulseTrack.Title = Title;
			pulseTrack.Artist = Artist;
			pulseTrack.ArtistId = ArtistId;
			pulseTrack.Album = Album;
			pulseTrack.AlbumId = AlbumId;
			pulseTrack.CoverArt = CoverArtId;
			pulseTrack.Duration = DurationSeconds;
			return pulseTrack;
		}
	}

	
	public class AlbumData : PulseDataObject
	{
		public string Name;
		public string ArtistName;
		public string ArtistId;
		public string Genre;
		public string CoverArtId;
		public int Year;

		[JsonIgnore]
		private List<TrackData> Tracks = new List<TrackData>();
		private object m_lock = new object();
		public int TrackCount { get { lock (m_lock) { return Tracks.Count; } } }
		public string GetTrackId(int index)
		{
			lock (m_lock)
			{
				return Tracks[index].Id;
			}
		}
		public TrackData GetTrack(int index)
		{
			lock (m_lock)
			{
				if (index >= 0 && index < Tracks.Count)
					return Tracks[index];
			}
			return null;
		}
		public List<TrackData> GetTracks()
		{
			lock (m_lock)
			{
				return new List<TrackData>(Tracks);
			}
		}
		public bool ContainsTrack(string id)
		{
			lock (m_lock)
			{
				for (int index = 0; index < Tracks.Count; index++)
				{
					if (Tracks[index].Id == id)
					{
						return true;
					}
				}
				return false;
			}
		}
		public void AddTrack(TrackData track)
		{
			lock (m_lock)
			{
				Tracks.Add(track);
			}
		}
		public void RemoveTrack(int index)
		{
			lock (m_lock)
			{
				if (index >= 0 && index < Tracks.Count)
				{
					Tracks.RemoveAt(index);
				}
			}
		}
		public void RemoveTrackById(string id)
		{
			lock (m_lock)
			{
				for (int index = Tracks.Count - 1; index >= 0; index--)
				{
					if (Tracks[index].Id == id)
					{
						Tracks.RemoveAt(index);
					}
				}
			}
		}


		public PulseAlbum BuildPulse()
		{
			PulseAlbum pulse = new PulseAlbum();
			pulse.Id = Id;

			pulse.Name = Name;
			pulse.Artist = ArtistName;
			pulse.ArtistId = ArtistId;
			pulse.CoverArt = CoverArtId;
			pulse.Year = Year;
			List<TrackData> snapshot = GetTracks();
			pulse.TrackCount = snapshot.Count;
			pulse.Duration = 0;
			for (int index = 0; index < snapshot.Count; index++)
			{
				pulse.Duration += snapshot[index].DurationSeconds;
			}

			return pulse;
		}
	}

	public class ArtistData : PulseDataObject
	{
		public string Name;

		[JsonIgnore]
		private List<AlbumData> Albums = new List<AlbumData>();
		private object m_lock = new object();
		public int AlbumCount { get { lock (m_lock) { return Albums.Count; } } }
		public AlbumData GetAlbum(int index)
		{
			lock (m_lock)
			{
				if (index >= 0 && index < Albums.Count)
					return Albums[index];
			}
			return null;
		}
		public List<AlbumData> GetAlbums()
		{
			lock (m_lock)
			{
				return new List<AlbumData>(Albums);
			}
		}
		public void AddAlbum(AlbumData album)
		{
			lock (m_lock)
			{
				Albums.Add(album);
			}
		}
		public void RemoveAlbumById(string id)
		{
			lock (m_lock)
			{
				for (int index = Albums.Count - 1; index >= 0; index--)
				{
					if (Albums[index].Id == id)
					{
						Albums.RemoveAt(index);
					}
				}
			}
		}
		public List<TrackData> GetTracks()
		{
			List<TrackData> tracks = new List<TrackData>();
			List<AlbumData> albums = GetAlbums();
			for (int albumIndex = 0; albumIndex < albums.Count; albumIndex++)
			{
				List<TrackData> albumTracks = albums[albumIndex].GetTracks();
				for (int trackIndex = 0; trackIndex < albumTracks.Count; trackIndex++)
				{
					tracks.Add(albumTracks[trackIndex]);
				}
			}
			return tracks;
		}

		public PulseArtist BuildPulse()
		{
			PulseArtist pulse = new PulseArtist();
			pulse.Id = Id;
			pulse.Name = Name;
			List<AlbumData> albums = GetAlbums();
			pulse.AlbumCount = albums.Count;
			if (albums.Count != 0)
			{
				pulse.CoverArt = albums[0].CoverArtId;
			}
			pulse.TrackCount = 0;
			for (int index = 0; index < albums.Count; index++)
			{
				pulse.TrackCount += albums[index].TrackCount;
			}
			return pulse;
		}

	}

	public class PlaylistData : PulseDataObject
	{
		public string Name;
		public string Comment;
		public List<string> TrackIds;
		public int GetTrackCount()
		{
			return TrackIds.Count;
		}
		public long DurationSeconds;

		public PulsePlaylist BuildPulsePlaylist()
		{
			PulsePlaylist pulse = new PulsePlaylist();
			pulse.Id = Id;
			pulse.Name = Name;
			pulse.Comment = Comment;
			pulse.CoverArt = "pl-" + Id;
			pulse.TrackCount = TrackIds.Count;
			pulse.Duration = 0;

			return pulse;
		}

		/// <summary>
		/// If the bad ID is in our track list we'll replace it with the good id
		/// </summary>
		/// <param name="badId"></param>
		/// <param name="goodId"></param>
		public void RepairTrackLinkID(string badId, string goodId)
		{
			for (int i = 0; i < TrackIds.Count; i++)
			{
				if (TrackIds[i] == badId)
				{
					TrackIds[i] = goodId;
					MarkDirty();
				}
			}
		}

		public PlaylistData()
		{
			TrackIds = new List<string>();
			Comment = "";
		}
	}

	public class PulseAnalyticsData : PulseDataObject
	{
		public List<string> RecentlyPlayed = new List<string>();
		public PulseAnalyticsData()
		{
			Id = "analytics";
		}
	}


	public class GenreData : PulseDataObject
	{
		public int TrackCount;
		public int AlbumCount;
		public string Name;
	}

}
