using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Thump.Pulse;
using Thump.Views;

namespace Thump.Views.Tiles
{
	public class PlaylistRowTile : ThumpView
	{
		private ArtImage m_art;
		private Label m_nameLabel;
		private Label m_subtitleLabel;
		private PulsePlaylist m_playlist;

		public PlaylistRowTile() : base(MainView.Self)
		{
			Grid grid = new Grid();
			grid.Padding = new Thickness(16, 8);

			ColumnDefinition artColumn = new ColumnDefinition();
			artColumn.Width = new GridLength(56);
			ColumnDefinition textColumn = new ColumnDefinition();
			textColumn.Width = GridLength.Star;
			grid.ColumnDefinitions.Add(artColumn);
			grid.ColumnDefinitions.Add(textColumn);

			m_art = new ArtImage();
			m_art.WidthRequest = 56;
			m_art.HeightRequest = 56;
			m_art.VerticalOptions = LayoutOptions.Center;
			Grid.SetColumn(m_art, 0);
			grid.Children.Add(m_art);

			StackLayout textStack = new StackLayout();
			textStack.VerticalOptions = LayoutOptions.Center;
			textStack.Spacing = 2;
			textStack.Padding = new Thickness(12, 0, 0, 0);
			Grid.SetColumn(textStack, 1);

			m_nameLabel = new Label();
			m_nameLabel.Text = "Playlist name";
			m_nameLabel.TextColor = ThumpColors.OnBackground;
			m_nameLabel.FontSize = 16;
			m_nameLabel.LineBreakMode = LineBreakMode.TailTruncation;
			textStack.Children.Add(m_nameLabel);

			m_subtitleLabel = new Label();
			m_subtitleLabel.Text = "0 tracks";
			m_subtitleLabel.TextColor = ThumpColors.TextSecondary;
			m_subtitleLabel.FontSize = 12;
			m_subtitleLabel.LineBreakMode = LineBreakMode.TailTruncation;
			textStack.Children.Add(m_subtitleLabel);

			grid.Children.Add(textStack);

			TapGestureRecognizer tap = new TapGestureRecognizer();
			tap.Tapped += OnTapped;
			grid.GestureRecognizers.Add(tap);

			Content = grid;
		}

		public override void Initialize()
		{
			base.Initialize();
		}

		protected override void OnBindingContextChanged()
		{
			base.OnBindingContextChanged();
			PulsePlaylist playlist = BindingContext as PulsePlaylist;
			if (playlist == null)
			{
				return;
			}
			m_playlist = playlist;
			m_nameLabel.Text = playlist.Name;
			m_subtitleLabel.Text = playlist.SongCount + " tracks";
			m_art.SetCoverArt(playlist.CoverArt);
		}

		private void OnTapped(object sender, EventArgs e)
		{
			if (m_playlist == null)
			{
				return;
			}
			m_mainView.OnPlaylistSelected(m_playlist);
		}
	}
}
