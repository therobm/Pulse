using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PulseAPI.CSharp;
using PulseApp.Pulse;
using PulseApp.Views.Tiles;

namespace PulseApp.Views
{
	public enum eSearchMode
	{
		Media,
		Podcasts,
	}

	public class SearchView : PulseAppView
	{
		private Entry m_searchEntry;
		private Button m_modeMediaButton;
		private Button m_modePodcastsButton;
		private eSearchMode m_searchMode = eSearchMode.Media;
		private Label m_artistsHeader;
		private Label m_albumsHeader;
		private Label m_songsHeader;
		private Label m_playlistsHeader;
		private Label m_podcastsHeader;
		private CollectionView m_artistResults;
		private CollectionView m_albumResults;
		private CollectionView m_songResults;
		private CollectionView m_playlistResults;
		private CollectionView m_podcastResults;

		public SearchView(MainView mainView) : base(mainView)
		{
			
		}

		protected override void BuildLayout()
		{
			BackgroundColor = PulseAppColors.Background;

			Grid grid = new Grid();

			RowDefinition titleRow = new RowDefinition();
			titleRow.Height = GridLength.Auto;
			RowDefinition entryRow = new RowDefinition();
			entryRow.Height = GridLength.Auto;
			RowDefinition toggleRow = new RowDefinition();
			toggleRow.Height = GridLength.Auto;
			RowDefinition resultsRow = new RowDefinition();
			resultsRow.Height = GridLength.Star;
			grid.RowDefinitions.Add(titleRow);
			grid.RowDefinitions.Add(entryRow);
			grid.RowDefinitions.Add(toggleRow);
			grid.RowDefinitions.Add(resultsRow);

			grid.Children.Add(BuildTitle());
			grid.Children.Add(BuildSearchEntry());
			grid.Children.Add(BuildModeToggle());
			grid.Children.Add(BuildResults());

			Content = grid;
		}

		private View BuildTitle()
		{
			Label header = new Label();
			header.Text = "Search";
			header.FontSize = 24;
			header.TextColor = PulseAppColors.OnBackground;
			header.Padding = new Thickness(16, 12);

			Grid.SetRow(header, 0);
			return header;
		}

		private View BuildSearchEntry()
		{
			m_searchEntry = new Entry();
			m_searchEntry.Placeholder = "Search artists, albums, songs";
			m_searchEntry.PlaceholderColor = PulseAppColors.TextDim;
			m_searchEntry.TextColor = PulseAppColors.OnBackground;
			m_searchEntry.BackgroundColor = PulseAppColors.Surface;
			m_searchEntry.FontSize = 15;
			m_searchEntry.Margin = new Thickness(16, 0, 16, 12);
			//Tapping a suggestion in Android's predictive bar counts as the
			//keyboard's submit action and fires Completed - so a single
			//character + suggestion-tap was kicking the search and tearing
			//the keyboard down. Turn prediction / spell check off so only
			//the explicit Search return key submits.
			m_searchEntry.IsTextPredictionEnabled = false;
			m_searchEntry.IsSpellCheckEnabled = false;
			m_searchEntry.ReturnType = ReturnType.Search;
			m_searchEntry.Completed += OnSearchCompleted;

			Grid.SetRow(m_searchEntry, 1);
			return m_searchEntry;
		}

		private View BuildResults()
		{
			ScrollView scroll = new ScrollView();

			StackLayout stack = new StackLayout();
			stack.Spacing = 16;

			m_playlistsHeader = new Label();
			m_playlistsHeader.Text = "Playlists";
			m_playlistsHeader.FontSize = 16;
			m_playlistsHeader.TextColor = PulseAppColors.OnBackground;
			m_playlistsHeader.Padding = new Thickness(16, 0);
			stack.Children.Add(m_playlistsHeader);

			m_playlistResults = new CollectionView();
			m_playlistResults.ItemTemplate = new DataTemplate(typeof(PlaylistRowTile));
			stack.Children.Add(m_playlistResults);

			m_artistsHeader = new Label();
			m_artistsHeader.Text = "Artists";
			m_artistsHeader.FontSize = 16;
			m_artistsHeader.TextColor = PulseAppColors.OnBackground;
			m_artistsHeader.Padding = new Thickness(16, 0);
			stack.Children.Add(m_artistsHeader);

			m_artistResults = new CollectionView();
			m_artistResults.ItemTemplate = new DataTemplate(typeof(ArtistRowTile));
			stack.Children.Add(m_artistResults);

			m_albumsHeader = new Label();
			m_albumsHeader.Text = "Albums";
			m_albumsHeader.FontSize = 16;
			m_albumsHeader.TextColor = PulseAppColors.OnBackground;
			m_albumsHeader.Padding = new Thickness(16, 0);
			stack.Children.Add(m_albumsHeader);

			m_albumResults = new CollectionView();
			m_albumResults.ItemTemplate = new DataTemplate(typeof(AlbumRowTile));
			stack.Children.Add(m_albumResults);

			m_songsHeader = new Label();
			m_songsHeader.Text = "Songs";
			m_songsHeader.FontSize = 16;
			m_songsHeader.TextColor = PulseAppColors.OnBackground;
			m_songsHeader.Padding = new Thickness(16, 0);
			stack.Children.Add(m_songsHeader);

			m_songResults = new CollectionView();
			m_songResults.ItemTemplate = new DataTemplate(typeof(TrackRowTile));
			stack.Children.Add(m_songResults);

			m_podcastsHeader = new Label();
			m_podcastsHeader.Text = "Podcasts";
			m_podcastsHeader.FontSize = 16;
			m_podcastsHeader.TextColor = PulseAppColors.OnBackground;
			m_podcastsHeader.Padding = new Thickness(16, 0);
			stack.Children.Add(m_podcastsHeader);

			m_podcastResults = new CollectionView();
			m_podcastResults.ItemTemplate = new DataTemplate(typeof(PodcastResultTile));
			stack.Children.Add(m_podcastResults);

			m_artistsHeader.IsVisible = false;
			m_artistResults.IsVisible = false;
			m_albumsHeader.IsVisible = false;
			m_albumResults.IsVisible = false;
			m_songsHeader.IsVisible = false;
			m_songResults.IsVisible = false;
			m_playlistsHeader.IsVisible = false;
			m_playlistResults.IsVisible = false;
			m_podcastsHeader.IsVisible = false;
			m_podcastResults.IsVisible = false;

			scroll.Content = stack;

			Grid.SetRow(scroll, 3);
			return scroll;
		}

		private void OnSearchCompleted(object sender, EventArgs e)
		{
			RunSearch();
		}

		// Search runs in one mode at a time - Pulse library OR podcast discovery,
		// chosen by the toggle - never both at once.
		private void RunSearch()
		{
			HideAllResults();
			string query = m_searchEntry.Text;
			if (string.IsNullOrWhiteSpace(query))
			{
				return;
			}
			if (m_searchMode == eSearchMode.Media)
			{
				MainView.MediaClient.Search(query, OnSearchResults);
			}
			else
			{
				MainView.MediaClient.SearchPodcasts(query, OnPodcastSearchResults);
			}
		}

		private View BuildModeToggle()
		{
			Grid toggleGrid = new Grid();
			toggleGrid.ColumnSpacing = 8;
			toggleGrid.Padding = new Thickness(16, 0, 16, 12);
			ColumnDefinition mediaColumn = new ColumnDefinition();
			mediaColumn.Width = GridLength.Star;
			ColumnDefinition podcastColumn = new ColumnDefinition();
			podcastColumn.Width = GridLength.Star;
			toggleGrid.ColumnDefinitions.Add(mediaColumn);
			toggleGrid.ColumnDefinitions.Add(podcastColumn);

			m_modeMediaButton = new Button();
			m_modeMediaButton.Text = "Pulse";
			m_modeMediaButton.CornerRadius = 8;
			m_modeMediaButton.FontSize = 14;
			m_modeMediaButton.Clicked += OnModeMediaClicked;
			Grid.SetColumn(m_modeMediaButton, 0);
			toggleGrid.Children.Add(m_modeMediaButton);

			m_modePodcastsButton = new Button();
			m_modePodcastsButton.Text = "Podcasts";
			m_modePodcastsButton.CornerRadius = 8;
			m_modePodcastsButton.FontSize = 14;
			m_modePodcastsButton.Clicked += OnModePodcastsClicked;
			Grid.SetColumn(m_modePodcastsButton, 1);
			toggleGrid.Children.Add(m_modePodcastsButton);

			UpdateModeButtons();

			Grid.SetRow(toggleGrid, 2);
			return toggleGrid;
		}

		private void OnModeMediaClicked(object sender, EventArgs e)
		{
			SetSearchMode(eSearchMode.Media);
		}

		private void OnModePodcastsClicked(object sender, EventArgs e)
		{
			SetSearchMode(eSearchMode.Podcasts);
		}

		private void SetSearchMode(eSearchMode mode)
		{
			if (m_searchMode == mode)
			{
				return;
			}
			m_searchMode = mode;
			UpdateModeButtons();
			RunSearch();
		}

		// Reflect the active mode on the toggle buttons and the entry placeholder.
		private void UpdateModeButtons()
		{
			if (m_searchMode == eSearchMode.Media)
			{
				m_modeMediaButton.BackgroundColor = PulseAppColors.Accent;
				m_modeMediaButton.TextColor = PulseAppColors.Background;
				m_modePodcastsButton.BackgroundColor = PulseAppColors.Surface;
				m_modePodcastsButton.TextColor = PulseAppColors.OnBackground;
				m_searchEntry.Placeholder = "Search artists, albums, songs";
			}
			else
			{
				m_modePodcastsButton.BackgroundColor = PulseAppColors.Accent;
				m_modePodcastsButton.TextColor = PulseAppColors.Background;
				m_modeMediaButton.BackgroundColor = PulseAppColors.Surface;
				m_modeMediaButton.TextColor = PulseAppColors.OnBackground;
				m_searchEntry.Placeholder = "Search for podcasts";
			}
		}

		private void HideAllResults()
		{
			m_artistResults.IsVisible = false;
			m_artistsHeader.IsVisible = false;
			m_albumResults.IsVisible = false;
			m_albumsHeader.IsVisible = false;
			m_songResults.IsVisible = false;
			m_songsHeader.IsVisible = false;
			m_playlistResults.IsVisible = false;
			m_playlistsHeader.IsVisible = false;
			m_podcastResults.IsVisible = false;
			m_podcastsHeader.IsVisible = false;
		}

		private void OnPodcastSearchResults(System.Collections.Generic.List<PulsePodcast> podcasts)
		{
			if (podcasts != null && podcasts.Count > 0)
			{
				m_podcastResults.ItemsSource = podcasts;
				m_podcastResults.IsVisible = true;
				m_podcastsHeader.IsVisible = true;
			}
			else
			{
				m_podcastResults.ItemsSource = null;
				m_podcastResults.IsVisible = false;
				m_podcastsHeader.IsVisible = false;
			}
		}

		private void OnSearchResults(PulseSearchData results)
		{
			if (results == null)
			{
				m_artistResults.ItemsSource = null;
				m_artistResults.IsVisible = false;
				m_artistsHeader.IsVisible = false;
				m_albumResults.ItemsSource = null;
				m_albumResults.IsVisible = false;
				m_albumsHeader.IsVisible = false;
				m_songResults.ItemsSource = null;
				m_songResults.IsVisible = false;
				m_songsHeader.IsVisible = false;
				m_playlistResults.ItemsSource = null;
				m_playlistResults.IsVisible = false;
				m_playlistsHeader.IsVisible = false;
				return;
			}
			if (results.Artists != null && results.Artists.Count > 0)
			{
				m_artistResults.ItemsSource = results.Artists;
				m_artistResults.IsVisible = true;
				m_artistsHeader.IsVisible = true;
			}
			else
			{
				m_artistResults.ItemsSource = null;
				m_artistResults.IsVisible = false;
				m_artistsHeader.IsVisible = false;
			}
			if (results.Albums != null && results.Albums.Count > 0)
			{
				m_albumResults.ItemsSource = results.Albums;
				m_albumResults.IsVisible = true;
				m_albumsHeader.IsVisible = true;
			}
			else
			{
				m_albumResults.ItemsSource = null;
				m_albumResults.IsVisible = false;
				m_albumsHeader.IsVisible = false;
			}
			if (results.Tracks != null && results.Tracks.Count > 0)
			{
				m_songResults.ItemsSource = results.Tracks;
				m_songResults.IsVisible = true;
				m_songsHeader.IsVisible = true;
			}
			else
			{
				m_songResults.ItemsSource = null;
				m_songResults.IsVisible = false;
				m_songsHeader.IsVisible = false;
			}
			if (results.Playlists != null && results.Playlists.Count > 0)
			{
				m_playlistResults.ItemsSource = results.Playlists;
				m_playlistResults.IsVisible = true;
				m_playlistsHeader.IsVisible = true;
			}
			else
			{
				m_playlistResults.ItemsSource = null;
				m_playlistResults.IsVisible = false;
				m_playlistsHeader.IsVisible = false;
			}
		}
	}
}
