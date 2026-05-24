using System.Text.Json;
using Pulse.MusicLibrary;

namespace Pulse.Data
{
	public class PulseJsonData : PulseDatabaseBase
	{
		public List<TrackInfo> Tracks { get; set; } = new List<TrackInfo>();
		public List<AlbumInfo> Albums { get; set; } = new List<AlbumInfo>();
		public List<ArtistInfo> Artists { get; set; } = new List<ArtistInfo>();
		public List<PlaylistInfo> Playlists { get; set; } = new List<PlaylistInfo>();

		private string m_filePath;

		public override void Save()
		{
			lock (m_saveLock)
			{
				Tracks = new List<TrackInfo>(m_tracks.Values);
				Albums = new List<AlbumInfo>(m_albums.Values);
				Artists = new List<ArtistInfo>(m_artists.Values);
				Playlists = new List<PlaylistInfo>(m_playlists.Values);

				JsonSerializerOptions options = new JsonSerializerOptions
				{
					WriteIndented = true
				};

				string tempPath = m_filePath + ".tmp";
				string json = JsonSerializer.Serialize(this, options);
				File.WriteAllText(tempPath, json);
				File.Move(tempPath, m_filePath, overwrite: true);
			}
		}

		public static PulseJsonData Load(string jsonPath)
		{
			PulseJsonData db = null;

			if (File.Exists(jsonPath))
			{
				string json = File.ReadAllText(jsonPath);
				db = JsonSerializer.Deserialize<PulseJsonData>(json);
			}

			if (db == null)
			{
				db = new PulseJsonData();
			}

			db.m_filePath = jsonPath;
			db.BuildIndex();
			return db;
		}

		private void BuildIndex()
		{
			for (int index = 0; index < Artists.Count; index++)
			{
				m_artists[Artists[index].Id] = Artists[index];
			}

			for (int index = 0; index < Albums.Count; index++)
			{
				m_albums[Albums[index].Id] = Albums[index];
			}

			for (int index = 0; index < Tracks.Count; index++)
			{
				m_tracks[Tracks[index].Id] = Tracks[index];
			}

			for (int index = 0; index < Playlists.Count; index++)
			{
				m_playlists[Playlists[index].Id] = Playlists[index];
			}
		}
	}
}