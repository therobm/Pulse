using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Thump.Pulse
{
	// The single API surface every media-server client (Subsonic today, Pulse-native
	// in the future) must implement. Consumers (ThumpData, MainView, the playback
	// service, settings) hold an IMediaClient, so the concrete implementation can
	// be swapped without touching them.
	public interface IMediaClient
	{
		void SetServerParams(string ip, string port, string username, string password, SubsonicAPI.eSubSonicAuthType authType, bool enableSSL);
		bool TestConnection(out JsonElement response);
		bool IsOnline();
		string BuildStreamUrl(string trackId);
		string BuildRestUrl(string endpoint, string extraParams = null);
		void GetTrack(string trackId, Action<PulseTrack> onComplete);
		void GetArtists(Action<List<PulseArtist>> onComplete);
		void GetArtist(string artistId, Action<PulseArtist> onComplete);
		void GetPodcasts(Action<List<PulsePodcastChannel>> onComplete);
		void Search(string query, Action<PulseSearchData> onComplete);
		void GetArtistAlbums(string artistId, Action<List<PulseAlbum>> onComplete);
		void GetAlbum(string albumId, Action<PulseAlbum> onComplete);
		void GetAlbums(Action<List<PulseAlbum>> onComplete);
		void CreatePlaylist(string name, Action<PulsePlaylist> onComplete);
		void RenamePlaylist(string playlistId, string newName, Action<bool> onComplete);
		void Star(string trackId, Action<bool> onComplete);
		void Unstar(string trackId, Action<bool> onComplete);
		void DeletePlaylist(string playlistId, Action<bool> onComplete);
		void AddTrackToPlaylist(string playlistId, string songId, Action<bool> onComplete);
		void RemoveTrackFromPlaylist(string playlistId, int songIndex, Action<bool> onComplete);
		void ReorderPlaylist(string playlistId, int fromIndex, int toIndex, List<PulseTrack> newOrder, Action<bool> onComplete);
		void MarkPlaylistPlayed(string playlistId, Action<bool> onComplete);
		void GetPlaylists(Action<List<PulsePlaylist>> onComplete);
		void GetPlaylist(string playlistId, Action<PulsePlaylist> onComplete);
		void GetCoverArt(string coverArtId, Action<byte[]> onComplete);
		void GetTrackAudio(string trackId, Action<byte[]> onComplete);
		void GetRecentlyPlayed(Action<List<PulseObject>> onComplete);
		void GetPopularArtists(Action<List<PulseArtist>> onComplete);
		void GetTopPlaylists(Action<List<PulsePlaylist>> onComplete);
		void GetRecentPlaylists(Action<List<PulsePlaylist>> onComplete);
		void GetRecentlyAdded(Action<List<PulseObject>> onComplete);
		void GetGenres(Action<List<PulseGenre>> onComplete);
		void GetTopItems(Action<List<PulseObject>> onComplete);
		void GetTracksForGenre(string genre, Action<List<PulseTrack>> onComplete);
		void GetFavorites(Action<List<PulseTrack>> onComplete);
	}
}
