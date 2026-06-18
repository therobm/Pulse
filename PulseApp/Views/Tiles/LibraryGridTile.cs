using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using PulseAPI.CSharp;
using PulseApp.Data;
using PulseApp.Pulse;
using PulseApp.Views;

namespace PulseApp.Views.Tiles
{
	public class LibraryGridTile : PulseAppView
	{
		private ArtImage m_art;
		private Label m_titleLabel;
		private Label m_subtitleLabel;
		private PulseObject m_item;

		public LibraryGridTile() : base(MainView.Self)
		{

		}

		protected override void BuildLayout()
		{
			StackLayout stack = new StackLayout();
			stack.Spacing = 6;
			stack.Padding = new Thickness(8, 10);

			stack.Children.Add(BuildArt());
			stack.Children.Add(BuildLabels());

			TapGestureRecognizer tap = new TapGestureRecognizer();
			tap.Tapped += OnTapped;
			stack.GestureRecognizers.Add(tap);

			Content = stack;
		}

		private View BuildArt()
		{
			m_art = new ArtImage(104);
			m_art.WidthRequest = 104;
			m_art.HeightRequest = 104;
			m_art.HorizontalOptions = LayoutOptions.Center;

			return m_art;
		}

		private View BuildLabels()
		{
			StackLayout labelStack = new StackLayout();
			labelStack.Spacing = 2;

			m_titleLabel = new Label();
			m_titleLabel.Text = "Title";
			m_titleLabel.TextColor = PulseAppColors.OnBackground;
			m_titleLabel.FontSize = 13;
			m_titleLabel.HorizontalTextAlignment = TextAlignment.Center;
			m_titleLabel.LineBreakMode = LineBreakMode.TailTruncation;
			m_titleLabel.MaxLines = 1;
			labelStack.Children.Add(m_titleLabel);

			m_subtitleLabel = new Label();
			m_subtitleLabel.Text = "Subtitle";
			m_subtitleLabel.TextColor = PulseAppColors.TextSecondary;
			m_subtitleLabel.FontSize = 11;
			m_subtitleLabel.HorizontalTextAlignment = TextAlignment.Center;
			m_subtitleLabel.LineBreakMode = LineBreakMode.TailTruncation;
			m_subtitleLabel.MaxLines = 1;
			labelStack.Children.Add(m_subtitleLabel);

			return labelStack;
		}

		protected override void OnBindingContextChanged()
		{
			base.OnBindingContextChanged();
			PulseObject item = BindingContext as PulseObject;
			if (item == null)
			{
				return;
			}
			m_item = item;

			if (item.Kind == ePulseWireType.Album)
			{
				PulseAlbum album = item as PulseAlbum;
				if (album != null)
				{
					m_art.SetShape(eArtShape.RoundedRect);
					m_titleLabel.Text = album.Name;
					m_subtitleLabel.Text = album.Artist;
					m_art.SetCoverArt(album.CoverArt);
				}
			}
			else if (item.Kind == ePulseWireType.Playlist)
			{
				PulsePlaylist playlist = item as PulsePlaylist;
				if (playlist != null)
				{
					m_art.SetShape(eArtShape.RoundedRect);
					m_titleLabel.Text = playlist.Name;
					m_subtitleLabel.Text = playlist.TrackCount + " tracks";
					m_art.SetCoverArt(playlist.CoverArt);
				}
			}
			else if (item.Kind == ePulseWireType.Artist)
			{
				PulseArtist artist = item as PulseArtist;
				if (artist != null)
				{
					m_art.SetShape(eArtShape.Circle);
					m_titleLabel.Text = artist.Name;
					m_subtitleLabel.Text = artist.AlbumCount + " albums";
					m_art.SetCoverArt(artist.CoverArt);
				}
			}
			else if (item.Kind == ePulseWireType.Genre)
			{
				PulseGenre genre = item as PulseGenre;
				if (genre != null)
				{
					m_art.SetShape(eArtShape.RoundedRect);
					m_titleLabel.Text = genre.Name;
					m_subtitleLabel.Text = genre.TrackCount + " songs";
				}
			}
			else if (item.Kind == ePulseWireType.Podcast)
			{
				PulsePodcast podcast = item as PulsePodcast;
				if (podcast != null)
				{
					m_art.SetShape(eArtShape.RoundedRect);
					m_titleLabel.Text = podcast.Title;
					m_subtitleLabel.Text = podcast.EpisodeCount + " episodes";
				}
			}
		}

		private void OnTapped(object sender, EventArgs e)
		{
			if (m_item == null)
			{
				return;
			}
			if (m_item.Kind == ePulseWireType.Genre)
			{
				PulseGenre genre = m_item as PulseGenre;
				if (genre != null)
				{
					m_mainView.OnGenreSelected(genre);
				}
				return;
			}
			m_mainView.OnHomeItemSelected(m_item);
		}
	}
}
