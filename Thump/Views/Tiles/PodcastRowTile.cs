using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using PulseAPI.CSharp;
using Thump.Pulse;
using Thump.Views;

namespace Thump.Views.Tiles
{
	public class PodcastRowTile : ThumpView
	{
		private ArtImage m_art;
		private Label m_nameLabel;
		private Label m_subtitleLabel;
		private PulsePodcast m_podcast;

		public PodcastRowTile() : base(MainView.Self)
		{

		}

		protected override void BuildLayout()
		{
			Grid grid = new Grid();
			grid.Padding = new Thickness(16, 8);

			ColumnDefinition artColumn = new ColumnDefinition();
			artColumn.Width = new GridLength(56);
			ColumnDefinition textColumn = new ColumnDefinition();
			textColumn.Width = GridLength.Star;
			grid.ColumnDefinitions.Add(artColumn);
			grid.ColumnDefinitions.Add(textColumn);

			grid.Children.Add(BuildArt());
			grid.Children.Add(BuildText());

			TapGestureRecognizer tap = new TapGestureRecognizer();
			tap.Tapped += OnTapped;
			grid.GestureRecognizers.Add(tap);

			Content = grid;
		}

		private View BuildArt()
		{
			m_art = new ArtImage();
			m_art.WidthRequest = 56;
			m_art.HeightRequest = 56;
			m_art.VerticalOptions = LayoutOptions.Center;

			Grid.SetColumn(m_art, 0);
			return m_art;
		}

		private View BuildText()
		{
			StackLayout textStack = new StackLayout();
			textStack.VerticalOptions = LayoutOptions.Center;
			textStack.Spacing = 2;
			textStack.Padding = new Thickness(12, 0, 0, 0);

			m_nameLabel = new Label();
			m_nameLabel.Text = "Podcast title";
			m_nameLabel.TextColor = ThumpColors.OnBackground;
			m_nameLabel.FontSize = 16;
			m_nameLabel.LineBreakMode = LineBreakMode.TailTruncation;
			textStack.Children.Add(m_nameLabel);

			m_subtitleLabel = new Label();
			m_subtitleLabel.Text = "0 episodes";
			m_subtitleLabel.TextColor = ThumpColors.TextSecondary;
			m_subtitleLabel.FontSize = 12;
			m_subtitleLabel.LineBreakMode = LineBreakMode.TailTruncation;
			textStack.Children.Add(m_subtitleLabel);

			Grid.SetColumn(textStack, 1);
			return textStack;
		}

		public override void Initialize()
		{
			base.Initialize();
		}

		protected override void OnBindingContextChanged()
		{
			base.OnBindingContextChanged();
			PulsePodcast podcast = BindingContext as PulsePodcast;
			if (podcast == null)
			{
				return;
			}
			m_podcast = podcast;
			m_nameLabel.Text = podcast.Title;
			m_subtitleLabel.Text = podcast.EpisodeCount + " episodes";
			m_art.SetCoverArt(podcast.CoverArt);
		}

		private void OnTapped(object sender, EventArgs e)
		{
			if (m_podcast == null)
			{
				return;
			}
			m_mainView.OnPodcastSelected(m_podcast);
		}
	}
}
