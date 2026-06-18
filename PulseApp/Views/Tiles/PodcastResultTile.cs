using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using PulseAPI.CSharp;
using PulseApp.Pulse;
using PulseApp.Views;

namespace PulseApp.Views.Tiles
{
	/// <summary>
	/// A single podcast discovery hit (see PulseClient.SearchPodcasts): art,
	/// title, author, and a Subscribe button that adds the feed by its FeedUrl.
	/// Unlike PodcastRowTile these rows are not catalogued (no Id), so there is
	/// no tap-to-open - the only action is Subscribe.
	/// </summary>
	public class PodcastResultTile : PulseAppView
	{
		private ArtImage m_art;
		private Label m_nameLabel;
		private Label m_subtitleLabel;
		private Button m_subscribeButton;
		private PulsePodcast m_podcast;

		public PodcastResultTile() : base(MainView.Self)
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
			ColumnDefinition buttonColumn = new ColumnDefinition();
			buttonColumn.Width = GridLength.Auto;
			grid.ColumnDefinitions.Add(artColumn);
			grid.ColumnDefinitions.Add(textColumn);
			grid.ColumnDefinitions.Add(buttonColumn);

			grid.Children.Add(BuildArt());
			grid.Children.Add(BuildText());
			grid.Children.Add(BuildButton());

			Content = grid;
		}

		private View BuildArt()
		{
			m_art = new ArtImage(56);
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
			textStack.Padding = new Thickness(12, 0, 8, 0);

			m_nameLabel = new Label();
			m_nameLabel.Text = "Podcast title";
			m_nameLabel.TextColor = PulseAppColors.OnBackground;
			m_nameLabel.FontSize = 16;
			m_nameLabel.LineBreakMode = LineBreakMode.TailTruncation;
			textStack.Children.Add(m_nameLabel);

			m_subtitleLabel = new Label();
			m_subtitleLabel.Text = "";
			m_subtitleLabel.TextColor = PulseAppColors.TextSecondary;
			m_subtitleLabel.FontSize = 12;
			m_subtitleLabel.LineBreakMode = LineBreakMode.TailTruncation;
			textStack.Children.Add(m_subtitleLabel);

			Grid.SetColumn(textStack, 1);
			return textStack;
		}

		private View BuildButton()
		{
			m_subscribeButton = new Button();
			m_subscribeButton.Text = "Subscribe";
			m_subscribeButton.TextColor = PulseAppColors.OnBackground;
			m_subscribeButton.BackgroundColor = PulseAppColors.Surface;
			m_subscribeButton.CornerRadius = 8;
			m_subscribeButton.FontSize = 13;
			m_subscribeButton.Padding = new Thickness(16, 6);
			m_subscribeButton.VerticalOptions = LayoutOptions.Center;
			m_subscribeButton.Clicked += OnSubscribeClicked;

			Grid.SetColumn(m_subscribeButton, 2);
			return m_subscribeButton;
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
			m_subtitleLabel.Text = podcast.Author;
			m_art.SetCoverArt(podcast.CoverArt);
			// Tiles are recycled across binds; reset the button for the new item.
			m_subscribeButton.Text = "Subscribe";
			m_subscribeButton.IsEnabled = true;
		}

		private void OnSubscribeClicked(object sender, EventArgs e)
		{
			if (m_podcast == null || string.IsNullOrEmpty(m_podcast.FeedUrl))
			{
				return;
			}
			m_subscribeButton.IsEnabled = false;
			m_subscribeButton.Text = "Adding…";
			MainView.MediaClient.AddPodcast(m_podcast.FeedUrl, true, (added) =>
			{
				if (added == null)
				{
					m_subscribeButton.IsEnabled = true;
					m_subscribeButton.Text = "Subscribe";
					return;
				}
				m_subscribeButton.Text = "Subscribed ✓";
			});
		}
	}
}
