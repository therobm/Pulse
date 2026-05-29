using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Thump;

namespace Thump.Views
{
	public class NavFooter : ThumpView
	{
		private Label m_homeIcon;
		private Label m_homeLabel;
		private Label m_libraryIcon;
		private Label m_libraryLabel;
		private Label m_searchIcon;
		private Label m_searchLabel;
		private Label m_settingsIcon;
		private Label m_settingsLabel;

		public NavFooter(MainView mainView) : base(mainView)
		{

		}

		protected override void BuildLayout()
		{
			BackgroundColor = Color.FromArgb("#060606");
			HeightRequest = 72;

			Grid grid = new Grid();

			ColumnDefinition homeColumn = new ColumnDefinition();
			homeColumn.Width = GridLength.Star;
			ColumnDefinition libraryColumn = new ColumnDefinition();
			libraryColumn.Width = GridLength.Star;
			ColumnDefinition searchColumn = new ColumnDefinition();
			searchColumn.Width = GridLength.Star;
			ColumnDefinition settingsColumn = new ColumnDefinition();
			settingsColumn.Width = GridLength.Star;
			grid.ColumnDefinitions.Add(homeColumn);
			grid.ColumnDefinitions.Add(libraryColumn);
			grid.ColumnDefinitions.Add(searchColumn);
			grid.ColumnDefinitions.Add(settingsColumn);

			grid.Children.Add(BuildHomeTab());
			grid.Children.Add(BuildLibraryTab());
			grid.Children.Add(BuildSearchTab());
			grid.Children.Add(BuildSettingsTab());

			Content = grid;
		}

		private View BuildHomeTab()
		{
			m_homeIcon = new Label();
			m_homeIcon.FontFamily = "MaterialIcons";
			m_homeIcon.FontSize = 22;
			m_homeIcon.Text = "\uE88A";
			m_homeIcon.TextColor = ThumpColors.TextDim;
			m_homeIcon.HorizontalOptions = LayoutOptions.Center;

			m_homeLabel = new Label();
			m_homeLabel.Text = "Home";
			m_homeLabel.FontFamily = "PoppinsSemiBold";
			m_homeLabel.FontSize = 11;
			m_homeLabel.TextColor = ThumpColors.TextDim;
			m_homeLabel.HorizontalOptions = LayoutOptions.Center;

			StackLayout stack = new StackLayout();
			stack.Orientation = StackOrientation.Vertical;
			stack.HorizontalOptions = LayoutOptions.Center;
			stack.Spacing = 2;
			stack.Children.Add(m_homeIcon);
			stack.Children.Add(m_homeLabel);

			TapGestureRecognizer tap = new TapGestureRecognizer();
			tap.Tapped += OnHomeClicked;
			stack.GestureRecognizers.Add(tap);

			Grid.SetColumn(stack, 0);
			return stack;
		}

		private View BuildLibraryTab()
		{
			m_libraryIcon = new Label();
			m_libraryIcon.FontFamily = "MaterialIcons";
			m_libraryIcon.FontSize = 22;
			m_libraryIcon.Text = "\uE030";
			m_libraryIcon.TextColor = ThumpColors.TextDim;
			m_libraryIcon.HorizontalOptions = LayoutOptions.Center;

			m_libraryLabel = new Label();
			m_libraryLabel.Text = "Library";
			m_libraryLabel.FontFamily = "PoppinsSemiBold";
			m_libraryLabel.FontSize = 11;
			m_libraryLabel.TextColor = ThumpColors.TextDim;
			m_libraryLabel.HorizontalOptions = LayoutOptions.Center;

			StackLayout stack = new StackLayout();
			stack.Orientation = StackOrientation.Vertical;
			stack.HorizontalOptions = LayoutOptions.Center;
			stack.Spacing = 2;
			stack.Children.Add(m_libraryIcon);
			stack.Children.Add(m_libraryLabel);

			TapGestureRecognizer tap = new TapGestureRecognizer();
			tap.Tapped += OnLibraryClicked;
			stack.GestureRecognizers.Add(tap);

			Grid.SetColumn(stack, 1);
			return stack;
		}

		private View BuildSearchTab()
		{
			m_searchIcon = new Label();
			m_searchIcon.FontFamily = "MaterialIcons";
			m_searchIcon.FontSize = 22;
			m_searchIcon.Text = "\uE8B6";
			m_searchIcon.TextColor = ThumpColors.TextDim;
			m_searchIcon.HorizontalOptions = LayoutOptions.Center;

			m_searchLabel = new Label();
			m_searchLabel.Text = "Search";
			m_searchLabel.FontFamily = "PoppinsSemiBold";
			m_searchLabel.FontSize = 11;
			m_searchLabel.TextColor = ThumpColors.TextDim;
			m_searchLabel.HorizontalOptions = LayoutOptions.Center;

			StackLayout stack = new StackLayout();
			stack.Orientation = StackOrientation.Vertical;
			stack.HorizontalOptions = LayoutOptions.Center;
			stack.Spacing = 2;
			stack.Children.Add(m_searchIcon);
			stack.Children.Add(m_searchLabel);

			TapGestureRecognizer tap = new TapGestureRecognizer();
			tap.Tapped += OnSearchClicked;
			stack.GestureRecognizers.Add(tap);

			Grid.SetColumn(stack, 2);
			return stack;
		}

		private View BuildSettingsTab()
		{
			m_settingsIcon = new Label();
			m_settingsIcon.FontFamily = "MaterialIcons";
			m_settingsIcon.FontSize = 22;
			m_settingsIcon.Text = "\uE8B8";
			m_settingsIcon.TextColor = ThumpColors.TextDim;
			m_settingsIcon.HorizontalOptions = LayoutOptions.Center;

			m_settingsLabel = new Label();
			m_settingsLabel.Text = "Settings";
			m_settingsLabel.FontFamily = "PoppinsSemiBold";
			m_settingsLabel.FontSize = 11;
			m_settingsLabel.TextColor = ThumpColors.TextDim;
			m_settingsLabel.HorizontalOptions = LayoutOptions.Center;

			StackLayout stack = new StackLayout();
			stack.Orientation = StackOrientation.Vertical;
			stack.HorizontalOptions = LayoutOptions.Center;
			stack.Spacing = 2;
			stack.Children.Add(m_settingsIcon);
			stack.Children.Add(m_settingsLabel);

			TapGestureRecognizer tap = new TapGestureRecognizer();
			tap.Tapped += OnSettingsClicked;
			stack.GestureRecognizers.Add(tap);

			Grid.SetColumn(stack, 3);
			return stack;
		}

		public override void Initialize()
		{
			base.Initialize();
		}

		public void SetActiveTab(eTab tab)
		{
			m_homeIcon.TextColor = ThumpColors.TextDim;
			m_homeLabel.TextColor = ThumpColors.TextDim;
			m_libraryIcon.TextColor = ThumpColors.TextDim;
			m_libraryLabel.TextColor = ThumpColors.TextDim;
			m_searchIcon.TextColor = ThumpColors.TextDim;
			m_searchLabel.TextColor = ThumpColors.TextDim;
			m_settingsIcon.TextColor = ThumpColors.TextDim;
			m_settingsLabel.TextColor = ThumpColors.TextDim;

			if (tab == eTab.Home)
			{
				m_homeIcon.TextColor = ThumpColors.OnBackground;
				m_homeLabel.TextColor = ThumpColors.OnBackground;
			}
			else if (tab == eTab.Library)
			{
				m_libraryIcon.TextColor = ThumpColors.OnBackground;
				m_libraryLabel.TextColor = ThumpColors.OnBackground;
			}
			else if (tab == eTab.Search)
			{
				m_searchIcon.TextColor = ThumpColors.OnBackground;
				m_searchLabel.TextColor = ThumpColors.OnBackground;
			}
			else if (tab == eTab.Settings)
			{
				m_settingsIcon.TextColor = ThumpColors.OnBackground;
				m_settingsLabel.TextColor = ThumpColors.OnBackground;
			}
		}

		private void OnHomeClicked(object sender, EventArgs e)
		{
			m_mainView.NavigateToHome();
		}

		private void OnLibraryClicked(object sender, EventArgs e)
		{
			m_mainView.NavigateToLibrary();
		}

		private void OnSearchClicked(object sender, EventArgs e)
		{
			m_mainView.NavigateToSearch();
		}

		private void OnSettingsClicked(object sender, EventArgs e)
		{
			m_mainView.NavigateToSettings();
		}
	}
}
