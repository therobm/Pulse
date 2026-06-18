using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PulseAPI.CSharp;
using PulseApp.Pulse;
using PulseApp.Utility;
using PulseApp.Views.Tiles;

namespace PulseApp.Views
{
	public class ArtistDetailView : PulseAppView
	{
		private ArtImage m_art;
		private Label m_titleLabel;
		private Label m_metaLabel;
		private CollectionView m_albumList;
		private PulseArtist m_artist;
		// Bound to m_albumList once; OnAlbumsLoaded reconciles in place so a
		// back-nav refresh only touches changed rows.
		private QuietObservableCollection<PulseAlbum> m_albums = new QuietObservableCollection<PulseAlbum>();

		public ArtistDetailView(MainView mainView, PulseArtist artist) : base(mainView)
		{
			m_artist = artist;

			m_art.SetShape(eArtShape.Circle);
		}

		protected override void BuildLayout()
		{
			BackgroundColor = PulseAppColors.Background;

			Grid grid = new Grid();

			RowDefinition headerRow = new RowDefinition();
			headerRow.Height = GridLength.Auto;
			RowDefinition artRow = new RowDefinition();
			artRow.Height = GridLength.Auto;
			RowDefinition titleRow = new RowDefinition();
			titleRow.Height = GridLength.Auto;
			RowDefinition buttonRow = new RowDefinition();
			buttonRow.Height = GridLength.Auto;
			RowDefinition listRow = new RowDefinition();
			listRow.Height = GridLength.Star;
			grid.RowDefinitions.Add(headerRow);
			grid.RowDefinitions.Add(artRow);
			grid.RowDefinitions.Add(titleRow);
			grid.RowDefinitions.Add(buttonRow);
			grid.RowDefinitions.Add(listRow);

			grid.Children.Add(BuildHeader());
			grid.Children.Add(BuildArt());
			grid.Children.Add(BuildTitle());
			grid.Children.Add(BuildButtons());
			grid.Children.Add(BuildAlbumList());

			Content = grid;
		}

		private View BuildHeader()
		{
			HorizontalStackLayout headerStack = new HorizontalStackLayout();
			headerStack.Padding = new Thickness(8, 8, 8, 0);

			Button backButton = new Button();
			backButton.Text = "‹";
			backButton.FontSize = 22;
			backButton.TextColor = PulseAppColors.OnBackground;
			backButton.BackgroundColor = Colors.Transparent;
			backButton.WidthRequest = 44;
			backButton.HeightRequest = 44;
			backButton.Clicked += OnBackClicked;
			headerStack.Children.Add(backButton);

			Grid.SetRow(headerStack, 0);
			return headerStack;
		}

		private View BuildArt()
		{
			m_art = new ArtImage(180);
			m_art.HeightRequest = 180;
			m_art.WidthRequest = 180;
			m_art.HorizontalOptions = LayoutOptions.Center;
			m_art.Margin = new Thickness(0, 12, 0, 16);

			Grid.SetRow(m_art, 1);
			return m_art;
		}

		private View BuildTitle()
		{
			StackLayout titleStack = new StackLayout();
			titleStack.Spacing = 4;
			titleStack.Padding = new Thickness(16, 0, 16, 12);

			m_titleLabel = new Label();
			m_titleLabel.Text = "Artist";
			m_titleLabel.FontSize = 22;
			m_titleLabel.TextColor = PulseAppColors.OnBackground;
			m_titleLabel.HorizontalOptions = LayoutOptions.Center;
			titleStack.Children.Add(m_titleLabel);

			m_metaLabel = new Label();
			m_metaLabel.Text = "";
			m_metaLabel.FontSize = 12;
			m_metaLabel.TextColor = PulseAppColors.TextDim;
			m_metaLabel.HorizontalOptions = LayoutOptions.Center;
			titleStack.Children.Add(m_metaLabel);

			Grid.SetRow(titleStack, 2);
			return titleStack;
		}

		private View BuildButtons()
		{
			HorizontalStackLayout buttonStack = new HorizontalStackLayout();
			buttonStack.Spacing = 12;
			buttonStack.Padding = new Thickness(16, 0, 16, 12);
			buttonStack.HorizontalOptions = LayoutOptions.Center;

			Button playButton = new Button();
			playButton.Text = "▶  Play";
			playButton.TextColor = PulseAppColors.Background;
			playButton.BackgroundColor = PulseAppColors.Accent;
			playButton.CornerRadius = 8;
			playButton.FontSize = 14;
			playButton.Padding = new Thickness(20, 8);
			playButton.Clicked += OnPlayClicked;
			buttonStack.Children.Add(playButton);

			Button shuffleButton = new Button();
			shuffleButton.Text = "⇋  Shuffle";
			shuffleButton.TextColor = PulseAppColors.OnBackground;
			shuffleButton.BackgroundColor = PulseAppColors.Surface;
			shuffleButton.CornerRadius = 8;
			shuffleButton.FontSize = 14;
			shuffleButton.Padding = new Thickness(20, 8);
			shuffleButton.Clicked += OnShuffleClicked;
			buttonStack.Children.Add(shuffleButton);

			Grid.SetRow(buttonStack, 3);
			return buttonStack;
		}

		private View BuildAlbumList()
		{
			m_albumList = new CollectionView();
			m_albumList.ItemTemplate = new DataTemplate(typeof(AlbumRowTile));
			m_albumList.ItemsSource = m_albums;

			Grid.SetRow(m_albumList, 4);
			return m_albumList;
		}

		public override void Initialize()
		{
			m_titleLabel.Text = m_artist.Name;
			m_metaLabel.Text = m_artist.AlbumCount + " albums";
			m_art.SetCoverArt(m_artist.CoverArt);
			base.Initialize();

		}
		protected override void RefreshData()
		{
			MainView.MediaClient.GetArtistAlbums(m_artist.Id, (albums) =>
			{
				OnAlbumsLoaded(albums);
			});
			base.RefreshData();
		}
		private void OnAlbumsLoaded(List<PulseAlbum> albums)
		{
			// Server order is stable for an artist's albums, so reconcile by Id
			// and leave ordering as-is (no client sort needed).
			SyncFrom<PulseAlbum>(m_albums, albums);
		}

		private void OnBackClicked(object sender, EventArgs e)
		{
			m_mainView.OnBackPressed();
		}

		private void OnPlayClicked(object sender, EventArgs e)
		{
			m_mainView.OnPlayArtist(m_artist, false);
		}

		private void OnShuffleClicked(object sender, EventArgs e)
		{
			m_mainView.OnPlayArtist(m_artist, true);
		}
	}
}
