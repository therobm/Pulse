using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace PulseIngestion.Reporting
{
	public class Report
	{
		DateTime m_started;
		DateTime m_finished;
		List<ReportSection> m_sections = new List<ReportSection>();
		int m_maxReports = 30;

		public void MarkStarted()
		{
			m_started = DateTime.Now;
		}

		public void MarkFinished()
		{
			m_finished = DateTime.Now;
		}

		public ReportSection AddSection(string name)
		{
			ReportSection section = new ReportSection(name);
			m_sections.Add(section);
			return section;
		}

		public void Write(IngestionConfig config)
		{
			string directory = config.ReportDirectory;
			if (string.IsNullOrEmpty(directory))
			{
				directory = Path.Combine(config.MusicImportDir, "PulseIngestion");
			}

			Directory.CreateDirectory(directory);
			string filename = Path.Combine(directory, "report_" + m_started.ToString("yyyyMMdd_HHmmss") + ".html");
			File.WriteAllText(filename, RenderHtml());
			PruneOldReports(directory);
			Log.Info("Report written: " + filename);
		}

		void PruneOldReports(string directory)
		{
			string[] files = Directory.GetFiles(directory, "report_*.html");
			if (files.Length <= m_maxReports)
			{
				return;
			}

			Array.Sort(files);
			int removeCount = files.Length - m_maxReports;
			for (int i = 0; i < removeCount; i++)
			{
				File.Delete(files[i]);
			}
		}

		string RenderHtml()
		{
			TimeSpan cycleDuration = m_finished - m_started;
			StringBuilder builder = new StringBuilder();

			builder.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>PulseIngestion Report</title>");
			builder.Append("<style>");
			builder.Append(s_styles);
			builder.Append("</style></head><body>");

			builder.Append("<h1>PulseIngestion Report</h1>");
			builder.Append("<p class=\"meta\">Generated ");
			builder.Append(WebUtility.HtmlEncode(m_started.ToString("yyyy-MM-dd HH:mm:ss")));
			builder.Append(" &middot; Cycle duration ");
			builder.Append(WebUtility.HtmlEncode(cycleDuration.ToString(@"hh\:mm\:ss")));
			builder.Append("</p>");

			for (int i = 0; i < m_sections.Count; i++)
			{
				if (m_sections[i].PinToTop)
				{
					RenderSection(builder, m_sections[i]);
				}
			}

			RenderSummary(builder);

			for (int i = 0; i < m_sections.Count; i++)
			{
				if (!m_sections[i].PinToTop)
				{
					RenderSection(builder, m_sections[i]);
				}
			}

			builder.Append("</body></html>");
			return builder.ToString();
		}

		void RenderSummary(StringBuilder builder)
		{
			builder.Append("<h2>Summary</h2>");
			if (m_sections.Count == 0)
			{
				builder.Append("<p class=\"meta\">No scanners ran this cycle.</p>");
				return;
			}

			builder.Append("<table class=\"summary\"><thead><tr><th>Scanner</th><th>Duration</th><th>Info</th><th>Warnings</th><th>Errors</th></tr></thead><tbody>");
			for (int i = 0; i < m_sections.Count; i++)
			{
				ReportSection section = m_sections[i];
				builder.Append("<tr><td>");
				builder.Append(WebUtility.HtmlEncode(section.Name));
				builder.Append("</td><td>");
				builder.Append(WebUtility.HtmlEncode(section.Duration.ToString(@"hh\:mm\:ss")));
				builder.Append("</td><td>");
				builder.Append(section.CountOf(eReportLevel.Info));
				builder.Append("</td><td class=\"warn\">");
				builder.Append(section.CountOf(eReportLevel.Warning));
				builder.Append("</td><td class=\"error\">");
				builder.Append(section.CountOf(eReportLevel.Error));
				builder.Append("</td></tr>");
			}
			builder.Append("</tbody></table>");
		}

		void RenderSection(StringBuilder builder, ReportSection section)
		{
			builder.Append("<h2>");
			builder.Append(WebUtility.HtmlEncode(section.Name));
			builder.Append("</h2>");

			if (section.Items.Count == 0)
			{
				builder.Append("<p class=\"meta\">No items recorded.</p>");
				return;
			}

			builder.Append("<ul class=\"items\">");
			for (int i = 0; i < section.Items.Count; i++)
			{
				ReportItem item = section.Items[i];
				builder.Append("<li class=\"");
				builder.Append(LevelClass(item.Level));
				builder.Append("\">");
				builder.Append(WebUtility.HtmlEncode(item.Message));
				builder.Append("</li>");
			}
			builder.Append("</ul>");
		}

		static string LevelClass(eReportLevel level)
		{
			if (level == eReportLevel.Warning)
			{
				return "warn";
			}
			if (level == eReportLevel.Error)
			{
				return "error";
			}
			return "info";
		}

		static readonly string s_styles =
			"body{background:#1e1e1e;color:#d4d4d4;font-family:Consolas,'Courier New',monospace;margin:2rem;}" +
			"h1{color:#4ec9b0;border-bottom:1px solid #333;padding-bottom:.3rem;}" +
			"h2{color:#9cdcfe;margin-top:1.5rem;}" +
			".meta{color:#808080;}" +
			"table.summary{border-collapse:collapse;width:100%;margin:1rem 0;}" +
			"table.summary th,table.summary td{border:1px solid #333;padding:.4rem .6rem;text-align:left;}" +
			"table.summary th{background:#252526;color:#9cdcfe;}" +
			"ul.items{list-style:none;padding:0;}" +
			"ul.items li{padding:.25rem .5rem;border-left:3px solid #555;margin:.15rem 0;background:#252526;word-break:break-all;}" +
			"li.info{color:#d4d4d4;}" +
			"li.warn{border-left-color:#d7ba7d;color:#d7ba7d;}" +
			"li.error{border-left-color:#f48771;color:#f48771;}" +
			"td.warn{color:#d7ba7d;}" +
			"td.error{color:#f48771;}";
	}
}
