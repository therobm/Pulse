using System;

namespace Thump.Views
{
	public partial class SettingsView : ThumpView
	{
		public SettingsView(MainView mainView) : base(mainView)
		{
			InitializeComponent();
		}

		public override void Initialize()
		{
			base.Initialize();
		}

		private void OnSaveClicked(object sender, EventArgs e)
		{
		}

		private void OnClearCacheClicked(object sender, EventArgs e)
		{
		}
	}
}
