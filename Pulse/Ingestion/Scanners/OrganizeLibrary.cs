using PulseIngestion.Metadata;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PulseIngestion.Scanners
{
	public class OrganizeLibrary : Scanner
	{
		private enum eOrganizeResult
		{
			Moved,
			Skipped,
			Warned,
			Failed
		}

		// Cached once; SearchValues gives a vectorized membership test.
		private static readonly SearchValues<char> s_invalidFileNameChars = SearchValues.Create(Path.GetInvalidFileNameChars());

		private MetadataReader m_reader;
		private string m_musicOutputDir;
		public OrganizeLibrary(string musicOutputDir, IngestionConfig config) : base(config)
		{
			m_musicOutputDir = musicOutputDir;
			m_bIsActive = m_config.EnableOrganization;
			m_reader = new TagLibReader();
		}

		public override void Initialize()
		{
			m_workingDirectory = m_config.MusicImportDir;
			base.Initialize();
		}

		protected override void DoWork()
		{
			OrganizeFiles();
			base.DoWork();
		}

		private void OrganizeFiles()
		{
			string root = m_musicOutputDir;
			int moved = 0;
			int skipped = 0;
			int warned = 0;
			int failed = 0;

			// Snapshot first: we move files within the same tree, so enumerating
			// lazily would risk re-visiting files we just relocated.
			List<string> files = new List<string>(Directory.EnumerateFiles(m_workingDirectory, "*.*", SearchOption.AllDirectories));

			for (int i = 0; i < files.Count; i++)
			{
				string file = files[i];
				ReportProgress();
				if (!MusicFormat.IsAudioFile(file))
				{
					continue;
				}

				eOrganizeResult result = OrganizeFile(file, root);
				switch (result)
				{
					case eOrganizeResult.Moved:
						moved++;
						break;
					case eOrganizeResult.Skipped:
						skipped++;
						break;
					case eOrganizeResult.Warned:
						warned++;
						break;
					default:
						failed++;
						break;
				}
			}

			RecordInfo("Organized " + moved + " files, " + skipped + " already in place, " + warned + " skipped (tags/conflicts), " + failed + " failed.");
			OnComplete();
		}

		private eOrganizeResult OrganizeFile(string file, string root)
		{
			TrackTags tags;
			try
			{
				tags = m_reader.Read(file);
			}
			catch (Exception ex)
			{
				RecordWarning("Could not read tags, left in place: " + file + " - " + ex.Message);
				return eOrganizeResult.Warned;
			}

			string artist = Sanitize(tags.Artist);
			string album = Sanitize(tags.Album);
			if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(album))
			{
				RecordWarning("Missing artist/album tags, left in place: " + file);
				return eOrganizeResult.Warned;
			}

			string extension = Path.GetExtension(file);
			string fileName = BuildFileName(file, tags, extension);
			string targetDirectory = Path.Combine(root, artist, album);
			string targetPath = Path.Combine(targetDirectory, fileName);

			if (PathsEqual(file, targetPath))
			{
				return eOrganizeResult.Skipped;
			}

			try
			{
				Directory.CreateDirectory(targetDirectory);
				if (File.Exists(targetPath))
				{
					RecordWarning("Target already exists, left in place: " + file + " -> " + targetPath);
					return eOrganizeResult.Warned;
				}
				File.Move(file, targetPath);
				RecordInfo("Organized: " + file + " -> " + targetPath);
				return eOrganizeResult.Moved;
			}
			catch (Exception ex)
			{
				RecordError("Failed to organize: " + file + " - " + ex.Message);
				return eOrganizeResult.Failed;
			}
		}

		private string BuildFileName(string file, TrackTags tags, string extension)
		{
			if (!m_config.RenameFilesToTrackTitle)
			{
				return Path.GetFileName(file);
			}

			string title = Sanitize(tags.Title);
			if (string.IsNullOrEmpty(title))
			{
				return Path.GetFileName(file);
			}

			if (tags.TrackNumber > 0)
			{
				return tags.TrackNumber.ToString("00") + " - " + title + extension;
			}
			return title + extension;
		}

		private static bool PathsEqual(string left, string right)
		{
			string fullLeft = Path.GetFullPath(left);
			string fullRight = Path.GetFullPath(right);
			return string.Equals(fullLeft, fullRight, StringComparison.OrdinalIgnoreCase);
		}

		private static string Sanitize(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return "";
			}

			// Most tags are clean - a single scan lets us skip the rebuild entirely.
			string cleaned = value;
			if (value.AsSpan().ContainsAny(s_invalidFileNameChars))
			{
				StringBuilder builder = new StringBuilder(value.Length);
				for (int i = 0; i < value.Length; i++)
				{
					char current = value[i];
					if (!s_invalidFileNameChars.Contains(current))
					{
						builder.Append(current);
					}
				}
				cleaned = builder.ToString();
			}

			string result = cleaned.Trim();
			result = result.TrimEnd('.');
			result = result.Trim();
			return result;
		}
	}
}
