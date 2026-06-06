using Microsoft.Maui;
using Microsoft.Maui.Controls;
using PulseAPI.CSharp;
using Thump.Pulse;
using Thump.Views;

namespace Thump.Views.Tiles
{
	public class ChapterRowTile : ThumpView
	{
		private Label m_titleLabel;
		private Label m_subtitleLabel;
		private PulseChapter m_chapter;

		public ChapterRowTile() : base(MainView.Self)
		{

		}

		protected override void BuildLayout()
		{
			StackLayout stack = new StackLayout();
			stack.Padding = new Thickness(16, 10);
			stack.Spacing = 2;

			m_titleLabel = new Label();
			m_titleLabel.Text = "Chapter title";
			m_titleLabel.TextColor = ThumpColors.OnBackground;
			m_titleLabel.FontSize = 15;
			m_titleLabel.LineBreakMode = LineBreakMode.TailTruncation;
			stack.Children.Add(m_titleLabel);

			m_subtitleLabel = new Label();
			m_subtitleLabel.Text = "";
			m_subtitleLabel.TextColor = ThumpColors.TextSecondary;
			m_subtitleLabel.FontSize = 12;
			stack.Children.Add(m_subtitleLabel);

			Content = stack;
		}

		public override void Initialize()
		{
			base.Initialize();
		}

		protected override void OnBindingContextChanged()
		{
			base.OnBindingContextChanged();
			PulseChapter chapter = BindingContext as PulseChapter;
			if (chapter == null)
			{
				return;
			}
			m_chapter = chapter;
			m_titleLabel.Text = chapter.Title;
			m_subtitleLabel.Text = BuildSubtitle(chapter);
		}

		private static string BuildSubtitle(PulseChapter chapter)
		{
			string duration = FormatDuration(chapter.Duration);
			if (chapter.Completed)
			{
				if (string.IsNullOrEmpty(duration))
				{
					return "Finished";
				}
				return duration + "  ·  Finished";
			}
			if (chapter.PositionSeconds > 0)
			{
				if (string.IsNullOrEmpty(duration))
				{
					return "In progress";
				}
				return duration + "  ·  In progress";
			}
			return duration;
		}

		private static string FormatDuration(int totalSeconds)
		{
			if (totalSeconds <= 0)
			{
				return "";
			}
			int hours = totalSeconds / 3600;
			int minutes = (totalSeconds % 3600) / 60;
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
			if (hours > 0)
			{
				string minutesText;
				if (minutes < 10)
				{
					minutesText = "0" + minutes;
				}
				else
				{
					minutesText = minutes.ToString();
				}
				return hours + ":" + minutesText + ":" + secondsText;
			}
			return minutes + ":" + secondsText;
		}
	}
}
