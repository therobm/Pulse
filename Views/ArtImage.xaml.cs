using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Thump.Views
{
	public partial class ArtImage : ThumpView
	{
		public ArtImage() : base(MainView.Self)
		{
			InitializeComponent();
		}

		public ArtImage(MainView mainView) : base(mainView)
		{
			InitializeComponent();
		}

		public override void Initialize()
		{
			base.Initialize();
		}

		public void MakeCircular()
		{
			m_border.StrokeShape = new Ellipse();
		}

		public void SetCoverArt(string coverArtId)
		{
			if (string.IsNullOrEmpty(coverArtId))
			{
				m_image.IsVisible = false;
				return;
			}
			if (MainView.Data == null)
			{
				m_image.IsVisible = false;
				return;
			}
			MainView.Data.GetCoverArt(coverArtId, OnArtLoaded);
		}

		private void OnArtLoaded(byte[] data)
		{
			if (data == null || data.Length == 0)
			{
				return;
			}
			m_image.Source = ImageSource.FromStream(() => new System.IO.MemoryStream(data));
			m_image.IsVisible = true;
		}
	}
}
