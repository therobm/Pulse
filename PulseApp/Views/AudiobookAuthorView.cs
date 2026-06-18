using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using PulseAPI.CSharp;
using PulseApp.Pulse;
using PulseApp.Utility;
using PulseApp.Views.Tiles;

namespace PulseApp.Views
{
	// One author's audiobooks, filtered client-side from the full list. Tapping a
	// book opens the existing AudiobookDetailView (via AudiobookRowTile).
	public class AudiobookAuthorView : PulseAppView
	{
		private string m_authorName;
		private Label m_titleLabel;
		private CollectionView m_bookList;
		private QuietObservableCollection<PulseAudiobook> m_books = new QuietObservableCollection<PulseAudiobook>();

		public AudiobookAuthorView(MainView mainView, string authorName) : base(mainView)
		{
			m_authorName = authorName;
		}

		protected override void BuildLayout()
		{
			BackgroundColor = PulseAppColors.Background;

			Grid grid = new Grid();

			RowDefinition headerRow = new RowDefinition();
			headerRow.Height = GridLength.Auto;
			RowDefinition listRow = new RowDefinition();
			listRow.Height = GridLength.Star;
			grid.RowDefinitions.Add(headerRow);
			grid.RowDefinitions.Add(listRow);

			grid.Children.Add(BuildHeader());
			grid.Children.Add(BuildBookList());

			Content = grid;
		}

		private View BuildHeader()
		{
			HorizontalStackLayout headerStack = new HorizontalStackLayout();
			headerStack.Padding = new Thickness(8, 8, 8, 8);
			headerStack.Spacing = 4;

			Button backButton = new Button();
			backButton.Text = "‹";
			backButton.FontSize = 22;
			backButton.TextColor = PulseAppColors.OnBackground;
			backButton.BackgroundColor = Colors.Transparent;
			backButton.WidthRequest = 44;
			backButton.HeightRequest = 44;
			backButton.Clicked += OnBackClicked;
			headerStack.Children.Add(backButton);

			m_titleLabel = new Label();
			m_titleLabel.Text = "Author";
			m_titleLabel.FontSize = 20;
			m_titleLabel.TextColor = PulseAppColors.OnBackground;
			m_titleLabel.VerticalOptions = LayoutOptions.Center;
			m_titleLabel.LineBreakMode = LineBreakMode.TailTruncation;
			headerStack.Children.Add(m_titleLabel);

			Grid.SetRow(headerStack, 0);
			return headerStack;
		}

		private View BuildBookList()
		{
			m_bookList = new CollectionView();
			m_bookList.ItemTemplate = new DataTemplate(typeof(AudiobookRowTile));
			m_bookList.ItemsSource = m_books;

			Grid.SetRow(m_bookList, 1);
			return m_bookList;
		}

		public override void Initialize()
		{
			m_titleLabel.Text = m_authorName;
			base.Initialize();
		}

		protected override void RefreshData()
		{
			MainView.MediaClient.GetAudiobooks(OnAudiobooksLoaded);
			base.RefreshData();
		}

		private void OnAudiobooksLoaded(List<PulseAudiobook> audiobooks)
		{
			if (audiobooks == null)
			{
				audiobooks = new List<PulseAudiobook>();
			}
			List<PulseAudiobook> mine = new List<PulseAudiobook>();
			for (int index = 0; index < audiobooks.Count; index++)
			{
				if (MatchesAuthor(audiobooks[index]))
				{
					mine.Add(audiobooks[index]);
				}
			}
			SyncFrom<PulseAudiobook>(m_books, mine);
			Sort<PulseAudiobook>(m_books, CompareByTitle);
		}

		private bool MatchesAuthor(PulseAudiobook book)
		{
			string author = book.Author;
			if (string.IsNullOrEmpty(author))
			{
				return m_authorName == "Unknown Author";
			}
			return string.Equals(author, m_authorName, StringComparison.Ordinal);
		}

		private static int CompareByTitle(PulseAudiobook first, PulseAudiobook second)
		{
			return string.Compare(first.Title, second.Title, StringComparison.OrdinalIgnoreCase);
		}

		private void OnBackClicked(object sender, EventArgs e)
		{
			m_mainView.OnBackPressed();
		}
	}
}
