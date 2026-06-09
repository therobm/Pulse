using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Thump.Views
{
	public enum eArtShape
	{
		RoundedRect,
		Circle,
	}

	public class ArtImage : ThumpView
	{
		private const double s_cornerRadius = 10;

		private Border m_border;
		private Image m_image;
		private eArtShape m_shape = eArtShape.RoundedRect;
		private string m_coverArtId;
		private bool m_bLoadFailed;
		private int m_artSize;
		public ArtImage(int artSize) : this(MainView.Self, artSize)
		{
		}

		public ArtImage(MainView mainView, int artSize) : base(mainView)
		{
			m_artSize = artSize;
		}

		protected override void BuildLayout()
		{
			m_image = new Image();
			m_image.Aspect = Aspect.AspectFill;
			m_image.IsVisible = false;

			m_border = new Border();
			m_border.StrokeThickness = 0;
			m_border.BackgroundColor = ThumpColors.PlaceholderArt;
			m_border.Content = m_image;

			Content = m_border;

			ApplyShape();
		}

	
		public void SetShape(eArtShape shape)
		{
			m_shape = shape;
			ApplyShape();
		}

		public void SetAspect(Aspect aspect)
		{
			m_image.Aspect = aspect;
		}

		private void ApplyShape()
		{
			if (m_shape == eArtShape.Circle)
			{
				m_border.StrokeShape = new Ellipse();
			}
			else
			{
				RoundRectangle rounded = new RoundRectangle();
				rounded.CornerRadius = new CornerRadius(s_cornerRadius);
				m_border.StrokeShape = rounded;
			}
		}

		public void SetCoverArt(string coverArtId)
		{
			if (coverArtId == m_coverArtId)
				return;

			m_image.Source = null;
			m_coverArtId = coverArtId;
			m_bLoadFailed = false;
			if (string.IsNullOrEmpty(coverArtId))
			{
				m_image.IsVisible = false;
				return;
			}
			// Podcast discovery hits carry a remote artwork URL instead of a server
			// cover id (see PulseClient.SearchPodcasts); load those straight from
			// the provider CDN rather than through the coverArt endpoint.
			if (coverArtId.StartsWith("http://") || coverArtId.StartsWith("https://"))
			{
				m_image.Source = ImageSource.FromUri(new System.Uri(coverArtId));
				m_image.IsVisible = true;
				return;
			}
			if (MainView.MediaClient == null)
			{
				m_image.IsVisible = false;
				m_bLoadFailed = true;
				return;
			}
			MainView.MediaClient.GetCoverArt(coverArtId, m_artSize, (data)=>
			{
				OnArtLoaded(coverArtId, data);
			});
		}

		// Retry a cover-art fetch that came up empty last time (offline, server
		// hiccup). A successful load left m_bLoadFailed false, so this no-ops once
		// the image is in place.
		
		public override void OnNavigatedTo()
		{
			if (m_bLoadFailed && !string.IsNullOrEmpty(m_coverArtId))
			{
				SetCoverArt(m_coverArtId);
			}
			base.OnNavigatedTo();
		}

		private void OnArtLoaded(string artID, ImageSource data)
		{
			if (artID != m_coverArtId)
				return;

			if (data == null)
			{
				m_bLoadFailed = true;
				return;
			}
			m_bLoadFailed = false;
			m_image.Source = data;
			m_image.IsVisible = true;
		}
	}
}
