using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Thump.Views
{
	// Thin app-wide bar that appears when the server is unreachable. Lives in the
	// top row of MainView's root grid so it spans every page. Hidden by default;
	// the Auto-height row collapses to nothing while IsVisible is false.
	public class OfflineBanner : ThumpView
	{
		private static readonly Color s_offlineColor = Color.FromArgb("#3a2a2a");

		public OfflineBanner(MainView mainView) : base(mainView)
		{
		}

		protected override void BuildLayout()
		{
			BackgroundColor = s_offlineColor;
			IsVisible = false;

			Label label = new Label();
			label.Text = "Offline";
			label.TextColor = ThumpColors.OnBackground;
			label.FontSize = 12;
			label.HorizontalTextAlignment = TextAlignment.Center;
			label.VerticalTextAlignment = TextAlignment.Center;
			label.Padding = new Thickness(0, 4);

			Content = label;
		}

		public void SetIsOnline(bool online)
		{
			IsVisible = !online;
		}
	}
}
