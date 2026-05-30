using System;
using System.Collections.Generic;

namespace Pulse.MusicLibrary
{
	public static class MusicComparers
	{
		public static int CompareTrackByTopRank(TrackInfo left, TrackInfo right)
		{
			int byScore = right.Score.WeightedScore.CompareTo(left.Score.WeightedScore);
			if (byScore != 0)
			{
				return byScore;
			}
			return right.Score.PlayCount.CompareTo(left.Score.PlayCount);
		}

		public static int CompareAlbumByName(AlbumInfo left, AlbumInfo right)
		{
			string leftName = left.Name;
			if (leftName == null)
			{
				leftName = "";
			}
			string rightName = right.Name;
			if (rightName == null)
			{
				rightName = "";
			}
			return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
		}

		public static int CompareAlbumByArtistThenName(AlbumInfo left, AlbumInfo right)
		{
			string leftArtist = left.ArtistName;
			if (leftArtist == null)
			{
				leftArtist = "";
			}
			string rightArtist = right.ArtistName;
			if (rightArtist == null)
			{
				rightArtist = "";
			}
			int byArtist = string.Compare(leftArtist, rightArtist, StringComparison.OrdinalIgnoreCase);
			if (byArtist != 0)
			{
				return byArtist;
			}
			return CompareAlbumByName(left, right);
		}

		public static int CompareAlbumYearAscending(AlbumInfo left, AlbumInfo right)
		{
			return left.Year.CompareTo(right.Year);
		}

		public static int CompareAlbumYearDescending(AlbumInfo left, AlbumInfo right)
		{
			return right.Year.CompareTo(left.Year);
		}

		public static int CompareAlbumPlayCountDescending(KeyValuePair<AlbumInfo, int> left, KeyValuePair<AlbumInfo, int> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		public static int CompareAlbumDateDescending(KeyValuePair<AlbumInfo, DateTime> left, KeyValuePair<AlbumInfo, DateTime> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		public static int CompareAlbumFloatDescending(KeyValuePair<AlbumInfo, float> left, KeyValuePair<AlbumInfo, float> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		public static int CompareIntDescending(int left, int right)
		{
			return right.CompareTo(left);
		}
	}
}
