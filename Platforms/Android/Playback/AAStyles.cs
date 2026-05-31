using AndroidX.Media3.Session;
using System;
using System.Collections.Generic;
using System.Text;

namespace Thump.Platforms
{
	public class AAStyles
	{
		public static MediaLibraryService.LibraryParams BuildContentStyleParams()
		{
			Android.OS.Bundle extras = new Android.OS.Bundle();
			extras.PutInt(MediaConstants.ExtrasKeyContentStyleBrowsable, MediaConstants.ExtrasValueContentStyleGridItem);
			extras.PutInt(MediaConstants.ExtrasKeyContentStylePlayable, MediaConstants.ExtrasValueContentStyleListItem);
			MediaLibraryService.LibraryParams.Builder builder = new MediaLibraryService.LibraryParams.Builder();
			builder.SetExtras(extras);
			return builder.Build();
		}
	}
}
