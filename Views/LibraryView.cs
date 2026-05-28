using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Thump.Pulse;
using Thump.Views.Tiles;

namespace Thump.Views
{
	public enum eLibraryButton
	{
		Artists,
		Albums,
		Playlists,
		Genres,
	}

	public class LibraryView : ThumpView
	{
		private static readonly Color s_buttonActiveBackground = Color.FromArgb("#3b82f6");
		private static readonly Color s_buttonActiveText = Color.FromArgb("#0a0a0c");
		private static readonly Color s_buttonInactiveBackground = Color.FromArgb("#101014");
		private static readonly Color s_buttonInactiveText = Color.FromArgb("#e8e8ec");

		private eLibraryButton m_activeButton = eLibraryButton.Artists;

		private Button m_buttonArtists;
		private Button m_buttonAlbums;
		private Button m_buttonPlaylists;
		private Button m_buttonGenres;
		private CollectionView m_artistsList;
		private CollectionView m_albumsList;
		private CollectionView m_playlistsList;
		private CollectionView m_genresList;

		public LibraryView(MainView mainView) : base(mainView)
		{

		}

		protected override void BuildLayout()
		{
			BackgroundColor = ThumpColors.Background;

			Grid grid = new Grid();

			RowDefinition titleRow = new RowDefinition();
			titleRow.Height = GridLength.Auto;
			RowDefinition buttonRow = new RowDefinition();
			buttonRow.Height = GridLength.Auto;
			RowDefinition listRow = new RowDefinition();
			listRow.Height = GridLength.Star;
			grid.RowDefinitions.Add(titleRow);
			grid.RowDefinitions.Add(buttonRow);
			grid.RowDefinitions.Add(listRow);

			grid.Children.Add(BuildTitle());
			grid.Children.Add(BuildButtons());
			grid.Children.Add(BuildLists());

			Content = grid;
		}

		private View BuildTitle()
		{
			Label header = new Label();
			header.Text = "Library";
			header.FontSize = 24;
			header.TextColor = ThumpColors.OnBackground;
			header.Padding = new Thickness(16, 12);

			Grid.SetRow(header, 0);
			return header;
		}

		private View BuildButtons()
		{
			HorizontalStackLayout buttonStack = new HorizontalStackLayout();
			buttonStack.Spacing = 8;
			buttonStack.Padding = new Thickness(16, 0, 16, 12);

			m_buttonArtists = new Button();
			m_buttonArtists.Text = "Artists";
			m_buttonArtists.TextColor = ThumpColors.Background;
			m_buttonArtists.BackgroundColor = ThumpColors.Accent;
			m_buttonArtists.CornerRadius = 16;
			m_buttonArtists.FontSize = 13;
			m_buttonArtists.Padding = new Thickness(14, 4);
			m_buttonArtists.HeightRequest = 32;
			m_buttonArtists.Clicked += OnButtonArtistsClicked;
			buttonStack.Children.Add(m_buttonArtists);

			m_buttonAlbums = new Button();
			m_buttonAlbums.Text = "Albums";
			m_buttonAlbums.TextColor = ThumpColors.OnBackground;
			m_buttonAlbums.BackgroundColor = ThumpColors.Surface;
			m_buttonAlbums.CornerRadius = 16;
			m_buttonAlbums.FontSize = 13;
			m_buttonAlbums.Padding = new Thickness(14, 4);
			m_buttonAlbums.HeightRequest = 32;
			m_buttonAlbums.Clicked += OnButtonAlbumsClicked;
			buttonStack.Children.Add(m_buttonAlbums);

			m_buttonPlaylists = new Button();
			m_buttonPlaylists.Text = "Playlists";
			m_buttonPlaylists.TextColor = ThumpColors.OnBackground;
			m_buttonPlaylists.BackgroundColor = ThumpColors.Surface;
			m_buttonPlaylists.CornerRadius = 16;
			m_buttonPlaylists.FontSize = 13;
			m_buttonPlaylists.Padding = new Thickness(14, 4);
			m_buttonPlaylists.HeightRequest = 32;
			m_buttonPlaylists.Clicked += OnButtonPlaylistsClicked;
			buttonStack.Children.Add(m_buttonPlaylists);

			m_buttonGenres = new Button();
			m_buttonGenres.Text = "Genres";
			m_buttonGenres.TextColor = ThumpColors.OnBackground;
			m_buttonGenres.BackgroundColor = ThumpColors.Surface;
			m_buttonGenres.CornerRadius = 16;
			m_buttonGenres.FontSize = 13;
			m_buttonGenres.Padding = new Thickness(14, 4);
			m_buttonGenres.HeightRequest = 32;
			m_buttonGenres.Clicked += OnButtonGenresClicked;
			buttonStack.Children.Add(m_buttonGenres);

			Grid.SetRow(buttonStack, 1);
			return buttonStack;
		}

		private View BuildLists()
		{
			Grid listContainer = new Grid();

			m_artistsList = new CollectionView();
			m_artistsList.IsVisible = true;
			m_artistsList.BackgroundColor = ThumpColors.Background;
			m_artistsList.ItemTemplate = new DataTemplate(typeof(ArtistRowTile));
			listContainer.Children.Add(m_artistsList);

			m_albumsList = new CollectionView();
			m_albumsList.IsVisible = false;
			m_albumsList.BackgroundColor = ThumpColors.Background;
			m_albumsList.ItemTemplate = new DataTemplate(typeof(AlbumRowTile));
			listContainer.Children.Add(m_albumsList);

			m_playlistsList = new CollectionView();
			m_playlistsList.IsVisible = false;
			m_playlistsList.BackgroundColor = ThumpColors.Background;
			m_playlistsList.ItemTemplate = new DataTemplate(typeof(PlaylistRowTile));
			listContainer.Children.Add(m_playlistsList);

			m_genresList = new CollectionView();
			m_genresList.IsVisible = false;
			m_genresList.BackgroundColor = ThumpColors.Background;
			m_genresList.ItemTemplate = new DataTemplate(typeof(GenreRowTile));
			listContainer.Children.Add(m_genresList);

			Grid.SetRow(listContainer, 2);
			return listContainer;
		}

		public override void Initialize()
		{
			base.Initialize();
			MainView.Data.GetArtists(OnArtistsLoaded);
			MainView.Data.GetAlbums(OnAlbumsLoaded);
			MainView.Data.GetPlaylists(OnPlaylistsLoaded);
			MainView.Data.GetGenres(OnGenresLoaded);
			SetActiveButton(eLibraryButton.Artists);
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

		private void SetActiveButton(eLibraryButton button)
		{
			m_activeButton = button;

			m_buttonArtists.BackgroundColor = s_buttonInactiveBackground;
			m_buttonArtists.TextColor = s_buttonInactiveText;
			m_buttonAlbums.BackgroundColor = s_buttonInactiveBackground;
			m_buttonAlbums.TextColor = s_buttonInactiveText;
			m_buttonPlaylists.BackgroundColor = s_buttonInactiveBackground;
			m_buttonPlaylists.TextColor = s_buttonInactiveText;
			m_buttonGenres.BackgroundColor = s_buttonInactiveBackground;
			m_buttonGenres.TextColor = s_buttonInactiveText;

			m_artistsList.IsVisible = false;
			m_albumsList.IsVisible = false;
			m_playlistsList.IsVisible = false;
			m_genresList.IsVisible = false;

			if (button == eLibraryButton.Artists)
			{
				m_buttonArtists.BackgroundColor = s_buttonActiveBackground;
				m_buttonArtists.TextColor = s_buttonActiveText;
				m_artistsList.IsVisible = true;
			}
			else if (button == eLibraryButton.Albums)
			{
				m_buttonAlbums.BackgroundColor = s_buttonActiveBackground;
				m_buttonAlbums.TextColor = s_buttonActiveText;
				m_albumsList.IsVisible = true;
			}
			else if (button == eLibraryButton.Playlists)
			{
				m_buttonPlaylists.BackgroundColor = s_buttonActiveBackground;
				m_buttonPlaylists.TextColor = s_buttonActiveText;
				m_playlistsList.IsVisible = true;
			}
			else if (button == eLibraryButton.Genres)
			{
				m_buttonGenres.BackgroundColor = s_buttonActiveBackground;
				m_buttonGenres.TextColor = s_buttonActiveText;
				m_genresList.IsVisible = true;
			}
		}

		private void OnButtonArtistsClicked(object sender, EventArgs e)
		{
			SetActiveButton(eLibraryButton.Artists);
		}

		private void OnButtonAlbumsClicked(object sender, EventArgs e)
		{
			SetActiveButton(eLibraryButton.Albums);
		}

		private void OnButtonPlaylistsClicked(object sender, EventArgs e)
		{
			SetActiveButton(eLibraryButton.Playlists);
		}

		private void OnButtonGenresClicked(object sender, EventArgs e)
		{
			SetActiveButton(eLibraryButton.Genres);
		}
	}
}
