using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging;
using Pulse.MusicLibrary;
using Pulse.Protocols;
using System.Diagnostics;
using System.Text.Json;

namespace Pulse.Data
{
	public class PulseFileDatabase : PulseDatabaseBase
	{
		private string m_rootPath;
		private string m_tracksPath;
		private string m_albumsPath;
		private string m_artistsPath;
		private string m_playlistsPath;
		private string m_analyticsPath;

		private JsonSerializerOptions m_jsonOptions;
		MusicManager m_musicManager;

		public PulseFileDatabase(string rootPath, MusicManager musicManager)
		{
			m_rootPath = rootPath;
			m_tracksPath = Path.Combine(rootPath, "tracks");
			m_albumsPath = Path.Combine(rootPath, "albums");
			m_artistsPath = Path.Combine(rootPath, "artists");
			m_playlistsPath = Path.Combine(rootPath, "playlists");
			m_analyticsPath = Path.Combine(rootPath, "analytics.json");

			m_jsonOptions = new JsonSerializerOptions
			{
				WriteIndented = true
			};

			Directory.CreateDirectory(m_artistsPath);
			Directory.CreateDirectory(m_playlistsPath);
			m_musicManager = musicManager;
		}

		public void Load()
		{
			Stopwatch sw = Stopwatch.StartNew();

			sw.Start();
			LoadAnalytics();
			sw.Stop();
			Console.WriteLine("LoadAnalytics: " + sw.ElapsedMilliseconds + "ms");
			sw.Reset();

			sw.Start();
			LoadTracks();
			sw.Stop();
			Console.WriteLine("LoadTracks: " + sw.ElapsedMilliseconds + "ms");
			sw.Reset();

			sw.Start();
			LoadAlbums();
			sw.Stop();
			Console.WriteLine("LoadAlbums: " + sw.ElapsedMilliseconds + "ms");
			sw.Reset();


			sw.Start();
			LoadArtists();
			Console.WriteLine("LoadArtists: " + sw.ElapsedMilliseconds + "ms");
			sw.Reset();
			sw.Start();


			sw.Start();
			LoadPlaylists();
			Console.WriteLine("LoadPlaylists: " + sw.ElapsedMilliseconds + "ms");
			sw.Reset();
			sw.Start();


			sw.Start();
			WireUpReferences();
			Console.WriteLine("WireUpReferences: " + sw.ElapsedMilliseconds + "ms");
			sw.Reset();
			sw.Start();

			CalculateArtistScores();
		}

		private void CalculateArtistScores()
		{
			foreach (ArtistInfo artist in m_artists.Values)
			{
				float totalScore = 0f;
				int scoredCount = 0;
				Dictionary<string, float> userTotals = new Dictionary<string, float>();
				Dictionary<string, int> userCounts = new Dictionary<string, int>();

				for (int albumIndex = 0; albumIndex < artist.Albums.Count; albumIndex++)
				{
					AlbumInfo album = artist.Albums[albumIndex];
					for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
					{
						TrackInfo track = album.Tracks[trackIndex];
						m_musicManager.RecalculateScore(track);

						if (track.Score.PlayCount > 0)
						{
							if (track.Score.WeightedScore > 1)
							{
								track.Score.WeightedScore = 1;
							}

							totalScore += track.Score.WeightedScore;
							scoredCount++;
						}

						foreach (string userName in track.UserScore.Keys)
						{
							ScoreData userData = track.UserScore[userName];
							if (userData.PlayCount > 0)
							{
								if (!userTotals.ContainsKey(userName))
								{
									userTotals[userName] = 0f;
									userCounts[userName] = 0;
								}
								if (userData.WeightedScore > 1)
								{
									userData.WeightedScore = 1;
								}

								userTotals[userName] += userData.WeightedScore;
								userCounts[userName]++;
							}
						}
					}
				}

				if (scoredCount > 0)
				{
					artist.WeightedScore = totalScore / scoredCount;
				}

				foreach (string userName in userTotals.Keys)
				{
					artist.UserWeightedScore[userName] = userTotals[userName] / userCounts[userName];
				}
			}
		}

		private void LoadAnalytics()
		{
			if (File.Exists(m_analyticsPath))
			{
				string json = File.ReadAllText(m_analyticsPath);
				PulseAnalyticsRecord loaded = JsonSerializer.Deserialize<PulseAnalyticsRecord>(json, m_jsonOptions);
				if (loaded != null)
				{
					m_analytics = loaded.ToInfo();
					return;
				}
			}
			m_analytics = new PulseAnalyticsInfo();
		}
		private void LoadTracks()
		{
			string[] files = Directory.GetFiles(m_tracksPath, "*.json"); 
			Console.WriteLine("Loading TrackInfo: " + files.Length);
			ParallelOptions options = new ParallelOptions();
			options.MaxDegreeOfParallelism = 8;
			Parallel.For(0, files.Length, options, (int index) =>
			{
				string json = File.ReadAllText(files[index]);
				TrackRecord record = JsonSerializer.Deserialize<TrackRecord>(json, m_jsonOptions);
				if (record == null)
				{
					return;
				}
				TrackInfo track = record.ToTrackInfo();
				m_tracks[track.Id] = track;
			});
		}

		private void LoadAlbums()
		{
			string[] files = Directory.GetFiles(m_albumsPath, "*.json");
			Console.WriteLine("Loading AlbumInfo: " + files.Length);
			ParallelOptions options = new ParallelOptions();
			options.MaxDegreeOfParallelism = 8;
			Parallel.For(0, files.Length, options, (int index) =>
			{
				string json = File.ReadAllText(files[index]);
				AlbumRecord record = JsonSerializer.Deserialize<AlbumRecord>(json, m_jsonOptions);
				if (record == null)
				{
					return;
				}
				AlbumInfo album = record.ToAlbumInfo();
				m_albums[album.Id] = album;
			});
		}

		private void LoadArtists()
		{
			string[] files = Directory.GetFiles(m_artistsPath, "*.json");
			Console.WriteLine("Loading ArtistInfo: " + files.Length);
			ParallelOptions options = new ParallelOptions();
			options.MaxDegreeOfParallelism = 8;
			Parallel.For(0, files.Length, options, (int index) =>
			{
				string json = File.ReadAllText(files[index]);
				ArtistRecord record = JsonSerializer.Deserialize<ArtistRecord>(json, m_jsonOptions);
				if (record == null)
				{
					return;
				}

				Migrate(record);
			

				ArtistInfo artist = record.ToArtistInfo();
				m_artists[artist.Id] = artist;

				for (int albumIndex = 0; albumIndex < artist.Albums.Count; albumIndex++)
				{
					AlbumInfo album = artist.Albums[albumIndex];
					m_albums[album.Id] = album;

					for (int trackIndex = 0; trackIndex < album.Tracks.Count; trackIndex++)
					{
						album.Tracks[trackIndex].ParentArtist = artist;
						m_tracks[album.Tracks[trackIndex].Id] = album.Tracks[trackIndex];
					}
				}
			});
		}

		private void Migrate(ArtistRecord record)
		{
			if (record.Albums.Count > 0)
			{
				return;
			}
			Log.Info(-1, "Migration: " + record.Name);
			bool needSave = false;
			foreach (AlbumInfo album in m_albums.Values)
			{
				if (album.ArtistId != record.Id)
				{
					continue;
				}

				AlbumRecord albumRecord = AlbumRecord.FromAlbumInfo(album);
				foreach (TrackInfo track in m_tracks.Values)
				{
					if (track.AlbumId != album.Id)
					{
						continue;
					}
					albumRecord.Tracks.Add(TrackRecord.FromTrackInfo(track));

					string trackFile = Path.Combine(m_tracksPath, track.Id + ".json");
					if (File.Exists(trackFile))
					{
						File.Delete(trackFile);
					}
				}
				record.Albums.Add(albumRecord);

				needSave = true;
				string albumFile = Path.Combine(m_albumsPath, album.Id + ".json");
				if (File.Exists(albumFile))
				{
					File.Delete(albumFile);
				}
			}
			if (needSave)
			{
				SaveRecord(m_artistsPath, record.Id, record);
			}
		}

		private void LoadPlaylists()
		{
			string[] files = Directory.GetFiles(m_playlistsPath, "*.json");
			Console.WriteLine("Loading PlaylistInfo: " + files.Length);
			ParallelOptions options = new ParallelOptions();
			options.MaxDegreeOfParallelism = 8;
			Parallel.For(0, files.Length, options, (int index) =>
			{
				string json = File.ReadAllText(files[index]);
				PlaylistRecord record = JsonSerializer.Deserialize<PlaylistRecord>(json, m_jsonOptions);
				if (record == null)
				{
					return;
				}
				PlaylistInfo playlist = record.ToPlaylistInfo();
				m_playlists[playlist.Id] = playlist;
			});
		}

		private void WireUpReferences()
		{
			foreach (AlbumInfo album in m_albums.Values)
			{
				album.Tracks.Clear();
			}
			foreach (ArtistInfo artist in m_artists.Values)
			{
				artist.Albums.Clear();
			}

			foreach (TrackInfo track in m_tracks.Values)
			{
				AlbumInfo album;
				if (m_albums.TryGetValue(track.AlbumId, out album))
				{
					album.Tracks.Add(track);
				}
			}

			foreach (AlbumInfo album in m_albums.Values)
			{
				ArtistInfo artist;
				if (m_artists.TryGetValue(album.ArtistId, out artist))
				{
					artist.Albums.Add(album);
				}
			}
		}

		public override bool RemoveTrack(string trackId)
		{
			TrackInfo track;
			if (!m_tracks.TryGetValue(trackId, out track))
			{
				return false;
			}

			if (!base.RemoveTrack(trackId))
			{
				return false;
			}

			string trackFile = Path.Combine(m_tracksPath, trackId + ".json");
			if (File.Exists(trackFile))
			{
				File.Delete(trackFile);
			}

			// if album was removed, delete its file too
			if (!m_albums.ContainsKey(track.AlbumId))
			{
				string albumFile = Path.Combine(m_albumsPath, track.AlbumId + ".json");
				if (File.Exists(albumFile))
				{
					File.Delete(albumFile);
				}
			}

			// if artist was removed, delete its file too
			if (!m_artists.ContainsKey(track.ArtistId))
			{
				string artistFile = Path.Combine(m_artistsPath, track.ArtistId + ".json");
				if (File.Exists(artistFile))
				{
					File.Delete(artistFile);
				}
			}
			return true;
		}
		public override void DeletePlaylist(string playlistId)
		{
			base.DeletePlaylist(playlistId);
			string playlistFile = Path.Combine(m_playlistsPath, playlistId + ".json");
			if (File.Exists(playlistFile))
			{
				File.Delete(playlistFile);
			}
		}

		public void SaveAnalytics()
		{
			PulseAnalyticsRecord analyticsRecord = PulseAnalyticsRecord.FromInfo(m_analytics);
			SaveRecord(m_rootPath, "analytics", analyticsRecord);
		}

		public void SaveTrack(TrackInfo track)
		{
			TrackRecord record = TrackRecord.FromTrackInfo(track);
			SaveRecord(m_tracksPath, record.Id, record);
		}

		public void SaveAlbum(AlbumInfo album)
		{
			AlbumRecord record = AlbumRecord.FromAlbumInfo(album);
			SaveRecord(m_albumsPath, record.Id, record);
		}

		public void SaveArtist(ArtistInfo artist)
		{
			ArtistRecord record = ArtistRecord.FromArtistInfo(artist);
			SaveRecord(m_artistsPath, record.Id, record);
		}

		public void SavePlaylist(PlaylistInfo playlist)
		{
			PlaylistRecord record = PlaylistRecord.FromPlaylistInfo(playlist);
			SaveRecord(m_playlistsPath, record.Id, record);
		}

		private void SaveRecord<T>(string directoryPath, string id, T record)
		{
			string filePath = Path.Combine(directoryPath, id + ".json");
			string tempPath = filePath + ".tmp";
			string json = JsonSerializer.Serialize(record, m_jsonOptions);
			File.WriteAllText(tempPath, json);
			File.Move(tempPath, filePath, overwrite: true);
		}

		public override void Save()
		{
			lock (m_saveLock)
			{
				if (m_analytics.m_bIsDirty)
				{
					SaveAnalytics();
					m_analytics.m_bIsDirty = false;
				}
				foreach (TrackInfo track in m_tracks.Values)
				{
					if (track.m_bIsDirty)
					{
						track.m_bIsDirty = false;
						ArtistInfo artist;
						if (m_artists.TryGetValue(track.ArtistId, out artist))
						{
							artist.m_bIsDirty = true;
						}
					}
				}
				foreach (AlbumInfo album in m_albums.Values)
				{
					if (album.m_bIsDirty)
					{
						album.m_bIsDirty = false;
						ArtistInfo artist;
						if (m_artists.TryGetValue(album.ArtistId, out artist))
						{
							artist.m_bIsDirty = true;
						}
					}
				}
				foreach (ArtistInfo artist in m_artists.Values)
				{
					if (artist.m_bIsDirty)
					{
						artist.m_bIsDirty = false;
						SaveArtist(artist);
					}
				}
				foreach (PlaylistInfo playlist in m_playlists.Values)
				{
					if (playlist.m_bIsDirty)
					{
						playlist.m_bIsDirty = false;
						SavePlaylist(playlist);
					}
				}

			}
		}
	}
}
