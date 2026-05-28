using System;
using System.Collections.Generic;
using Microsoft.Maui.Graphics;
using Thump.Pulse;

namespace Thump.Views
{
	public enum eLibraryChip
	{
		Artists,
		Albums,
		Playlists,
		Genres,
	}

	public partial class LibraryView : ThumpView
	{
		private static readonly Color s_chipActiveBackground = Color.FromArgb("#3b82f6");
		private static readonly Color s_chipActiveText = Color.FromArgb("#0a0a0c");
		private static readonly Color s_chipInactiveBackground = Color.FromArgb("#101014");
		private static readonly Color s_chipInactiveText = Color.FromArgb("#e8e8ec");

		private eLibraryChip m_activeChip = eLibraryChip.Artists;

		public LibraryView(MainView mainView) : base(mainView)
		{
			InitializeComponent();
		}

		public override void Initialize()
		{
			base.Initialize();
			MainView.Data.GetArtists(OnArtistsLoaded);
			MainView.Data.GetAlbums(OnAlbumsLoaded);
			MainView.Data.GetPlaylists(OnPlaylistsLoaded);
			MainView.Data.GetGenres(OnGenresLoaded);
			SetActiveChip(eLibraryChip.Artists);
		}

		private void OnArtistsLoaded(List<PulseArtist> artists)
		{
			m_artistsList.ItemsSource = artists;
		}

		private void OnAlbumsLoaded(List<PulseAlbum> albums)
		{
			m_albumsList.ItemsSource = albums;
		}

		private void OnPlaylistsLoaded(List<PulsePlaylist> playlists)
		{
			m_playlistsList.ItemsSource = playlists;
		}

		private void OnGenresLoaded(List<PulseGenre> genres)
		{
			m_genresList.ItemsSource = genres;
		}

		private void SetActiveChip(eLibraryChip chip)
		{
			m_activeChip = chip;

			m_chipArtists.BackgroundColor = s_chipInactiveBackground;
			m_chipArtists.TextColor = s_chipInactiveText;
			m_chipAlbums.BackgroundColor = s_chipInactiveBackground;
			m_chipAlbums.TextColor = s_chipInactiveText;
			m_chipPlaylists.BackgroundColor = s_chipInactiveBackground;
			m_chipPlaylists.TextColor = s_chipInactiveText;
			m_chipGenres.BackgroundColor = s_chipInactiveBackground;
			m_chipGenres.TextColor = s_chipInactiveText;

			m_artistsList.IsVisible = false;
			m_albumsList.IsVisible = false;
			m_playlistsList.IsVisible = false;
			m_genresList.IsVisible = false;

			if (chip == eLibraryChip.Artists)
			{
				m_chipArtists.BackgroundColor = s_chipActiveBackground;
				m_chipArtists.TextColor = s_chipActiveText;
				m_artistsList.IsVisible = true;
			}
			else if (chip == eLibraryChip.Albums)
			{
				m_chipAlbums.BackgroundColor = s_chipActiveBackground;
				m_chipAlbums.TextColor = s_chipActiveText;
				m_albumsList.IsVisible = true;
			}
			else if (chip == eLibraryChip.Playlists)
			{
				m_chipPlaylists.BackgroundColor = s_chipActiveBackground;
				m_chipPlaylists.TextColor = s_chipActiveText;
				m_playlistsList.IsVisible = true;
			}
			else if (chip == eLibraryChip.Genres)
			{
				m_chipGenres.BackgroundColor = s_chipActiveBackground;
				m_chipGenres.TextColor = s_chipActiveText;
				m_genresList.IsVisible = true;
			}
		}

		private void OnChipArtistsClicked(object sender, EventArgs e)
		{
			SetActiveChip(eLibraryChip.Artists);
		}

		private void OnChipAlbumsClicked(object sender, EventArgs e)
		{
			SetActiveChip(eLibraryChip.Albums);
		}

		private void OnChipPlaylistsClicked(object sender, EventArgs e)
		{
			SetActiveChip(eLibraryChip.Playlists);
		}

		private void OnChipGenresClicked(object sender, EventArgs e)
		{
			SetActiveChip(eLibraryChip.Genres);
		}
	}
}
