using Pulse.DataStorage;
using System;
using System.Collections.Generic;

namespace Pulse.MusicLibrary
{
	public static class MusicComparers
	{
		public static int CompareTrackByTopRank(TrackData left, TrackData right)
		{
			int byScore = right.Score.WeightedScore.CompareTo(left.Score.WeightedScore);
			if (byScore != 0)
			{
				return byScore;
			}
			return right.Score.PlayCount.CompareTo(left.Score.PlayCount);
		}

		public static int CompareAlbumByName(AlbumData left, AlbumData right)
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

		public static int CompareAlbumByArtistThenName(AlbumData left, AlbumData right)
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

		public static int CompareAlbumYearAscending(AlbumData left, AlbumData right)
		{
			return left.Year.CompareTo(right.Year);
		}

		public static int CompareAlbumYearDescending(AlbumData left, AlbumData right)
		{
			return right.Year.CompareTo(left.Year);
		}

		public static int CompareAlbumPlayCountDescending(KeyValuePair<AlbumData, int> left, KeyValuePair<AlbumData, int> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		public static int CompareAlbumDateDescending(KeyValuePair<AlbumData, DateTime> left, KeyValuePair<AlbumData, DateTime> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		public static int CompareAlbumFloatDescending(KeyValuePair<AlbumData, float> left, KeyValuePair<AlbumData, float> right)
		{
			return right.Value.CompareTo(left.Value);
		}

		public static int CompareIntDescending(int left, int right)
		{
			return right.CompareTo(left);
		}
	}
}
