using System;
using Thump.Pulse;

namespace Thump.Views.Tiles
{
	public partial class GenreRowTile : ThumpView
	{
		private PulseGenre m_genre;

		public GenreRowTile() : base(MainView.Self)
		{
			InitializeComponent();
		}

		public override void Initialize()
		{
			base.Initialize();
		}

		protected override void OnBindingContextChanged()
		{
			base.OnBindingContextChanged();
			PulseGenre genre = BindingContext as PulseGenre;
			if (genre == null)
			{
				return;
			}
			m_genre = genre;
			m_nameLabel.Text = genre.Name;
			m_subtitleLabel.Text = genre.SongCount + " songs  ·  " + genre.AlbumCount + " albums";
		}

		private void OnTapped(object sender, EventArgs e)
		{
			if (m_genre == null)
			{
				return;
			}
			m_mainView.OnGenreSelected(m_genre);
		}
	}
}
