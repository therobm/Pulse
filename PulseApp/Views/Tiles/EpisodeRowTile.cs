using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using PulseAPI.CSharp;
using PulseApp.Pulse;
using PulseApp.Views;

namespace PulseApp.Views.Tiles
{
	// Display-only row tile for a PulsePodcastEpisode. The owning
	// PodcastDetailView wires its CollectionView.SelectionChanged for taps,
	// so no TapGestureRecognizer is attached here.
	public class EpisodeRowTile : PulseAppView
	{
		private Label m_titleLabel;
		private Label m_subtitleLabel;
		private PulsePodcastEpisode m_episode;

		public EpisodeRowTile() : base(MainView.Self)
		{

		}

		protected override void BuildLayout()
		{
			Grid grid = new Grid();
			grid.Padding = new Thickness(16, 8);

			ColumnDefinition textColumn = new ColumnDefinition();
			textColumn.Width = GridLength.Star;
			grid.ColumnDefinitions.Add(textColumn);

			grid.Children.Add(BuildText());

			Content = grid;
		}

		private View BuildText()
		{
			StackLayout textStack = new StackLayout();
			textStack.VerticalOptions = LayoutOptions.Center;
			textStack.Spacing = 2;

			m_titleLabel = new Label();
			m_titleLabel.Text = "Episode title";
			m_titleLabel.TextColor = PulseAppColors.OnBackground;
			m_titleLabel.FontSize = 15;
			m_titleLabel.LineBreakMode = LineBreakMode.TailTruncation;
			textStack.Children.Add(m_titleLabel);

			m_subtitleLabel = new Label();
			m_subtitleLabel.Text = "";
			m_subtitleLabel.TextColor = PulseAppColors.TextSecondary;
			m_subtitleLabel.FontSize = 12;
			m_subtitleLabel.LineBreakMode = LineBreakMode.TailTruncation;
			textStack.Children.Add(m_subtitleLabel);

			Grid.SetColumn(textStack, 0);
			return textStack;
		}


		protected override void OnBindingContextChanged()
		{
			base.OnBindingContextChanged();
			PulsePodcastEpisode episode = BindingContext as PulsePodcastEpisode;
			if (episode == null)
			{
				return;
			}
			m_episode = episode;
			m_titleLabel.Text = episode.Title;
			m_subtitleLabel.Text = BuildSubtitle(episode);
		}

		private static string BuildSubtitle(PulsePodcastEpisode episode)
		{
			string datePart = FormatPublishedDate(episode.PublishedDate);
			string durationPart = FormatDuration(episode.Duration);
			if (string.IsNullOrEmpty(datePart))
			{
				return durationPart;
			}
			if (string.IsNullOrEmpty(durationPart))
			{
				return datePart;
			}
			return datePart + "  ·  " + durationPart;
		}

		// The server hands PublishedDate down as an ISO-ish string. Try to
		// shorten it for display; if it doesn't parse, fall back to the raw
		// value so we never show an empty subtitle for a real episode.
		private static string FormatPublishedDate(string published)
		{
			if (string.IsNullOrEmpty(published))
			{
				return "";
			}
			DateTime parsed;
			if (DateTime.TryParse(published, out parsed))
			{
				return parsed.ToString("MMM d, yyyy");
			}
			return published;
		}

		private static string FormatDuration(int totalSeconds)
		{
			if (totalSeconds <= 0)
			{
				return "";
			}
			int minutes = totalSeconds / 60;
			int seconds = totalSeconds % 60;
			string secondsText;
			if (seconds < 10)
			{
				secondsText = "0" + seconds;
			}
			else
			{
				secondsText = seconds.ToString();
			}
			return minutes + ":" + secondsText;
		}
	}
}
