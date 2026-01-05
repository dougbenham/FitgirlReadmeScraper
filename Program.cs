using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;
using HtmlAgilityPack;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace FitgirlReadmeScraper
{
	static class Program
	{
		public static readonly Regex RegexYear = new Regex(@" \((?<year>\d{4})\)");
		public static readonly Regex RegexReleaseDate = new Regex(@"Release Date:\s+(?<releaseDate>\w+\s\d{1,2},\s\d{4})");
		private static readonly HttpClient HttpClient = new HttpClient();

		/// <summary>
		///  The main entry point for the application.
		/// </summary>
		[STAThread]
		static async Task Main(string[] args)
		{
			AttachFirefoxCookiesAndUserAgent(HttpClient);
            
			var cd = Path.GetDirectoryName(Environment.ProcessPath);
			if (Environment.CurrentDirectory != cd && cd != null)
				Environment.CurrentDirectory = cd;

			/*await FixModifiedDatesAsync();
			return;*/

			try
			{
				if (args.Length < 2)
				{
					if (File.Exists("names.txt"))
						File.Delete("names.txt");
					if (File.Exists("failed.txt"))
						File.Delete("failed.txt");

					var tasks = new List<Task>();
					foreach (var f in new DirectoryInfo(@"E:\Games\").EnumerateDirectories("*", SearchOption.AllDirectories))
					{
						var folderPath = f.FullName;

						if (f.EnumerateFiles("cover.jpg").Any())
							continue;

						if (!f.EnumerateDirectories("MD5").Any())
							continue;

						var regex = new Regex(@"^(?<name>.+?)(\s\(.+ Edition\))?(\s\(\d+\))?$");
						var match = regex.Match(f.Name);
						if (match.Success)
						{
							var name = match.Groups["name"].Value;

							await File.AppendAllTextAsync("names.txt", folderPath + Environment.NewLine);

							var web = new HtmlWeb();
							var searchResults = await web.LoadFromWebAsync("https://fitgirl-repacks.site/?s=" + HttpUtility.UrlEncode(name));
							//var searchResults = new HtmlDocument();
							//searchResults.Load("temp.html");
							var firstResult = searchResults.DocumentNode.SelectNodes("//article[contains(@class,'post')]")?.FirstOrDefault();
							if (firstResult != null)
							{
								var titleNode = firstResult.SelectSingleNode("//*[contains(@class, 'entry-title')]/a");
								var title = HttpUtility.HtmlDecode(titleNode.InnerHtml);
								var titleLink = titleNode.GetAttributeValue("href", null);
								if (MessageBox.Show("Found match for.." + Environment.NewLine + folderPath + Environment.NewLine + Environment.NewLine + title + Environment.NewLine + "Is this correct?", "",
									    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
								{
									tasks.Add(Task.Run(async () =>
									{
										try
										{
											var doc = await web.LoadFromWebAsync(titleLink);
											var linkNode = doc.DocumentNode.SelectNodes("//*[contains(@class, 'entry-content')]//ul//a")?.FirstOrDefault(node => node.InnerText == "1337x");
											if (linkNode != null)
												await ScrapeAsync(linkNode.GetAttributeValue("href", null), folderPath);
											else
												await File.AppendAllTextAsync("failed.txt", folderPath + "\r\n");
										}
										catch
										{
											await File.AppendAllTextAsync("failed.txt", folderPath + "\r\n");
										}
									}));
								}
								else
									await File.AppendAllTextAsync("failed.txt", folderPath + "\r\n");
							}
							else
								await File.AppendAllTextAsync("failed.txt", folderPath + "\r\n");
						}
					}

					await Task.WhenAll(tasks);
					MessageBox.Show("Done!");

					return;
				}

				await ScrapeAsync(args[1], args[0]);
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.ToString());
			}
		}

		private static async Task FixModifiedDatesAsync()
		{
			foreach (var f in new DirectoryInfo(@"E:\Games\").EnumerateDirectories("*", SearchOption.AllDirectories))
			{
				var folderPath = f.FullName;

				var yearMatch = RegexYear.Match(f.Name);
				if (yearMatch.Success)
				{
					var year = int.Parse(yearMatch.Groups["year"].Value);
					var readmePath = folderPath + "\\readme.txt";
					if (File.Exists(readmePath))
					{
						var readmeContent = await File.ReadAllTextAsync(readmePath);
						var releaseDate = GetReleaseDate(readmeContent);
						if (releaseDate != null)
							f.LastWriteTime = releaseDate.Value;
						else if (f.LastWriteTime.Year != year)
							f.LastWriteTime = new(year, 1, 1);
					}
					else if (f.LastWriteTime.Year != year)
						f.LastWriteTime = new(year, 1, 1);
				}
			}
		}

		private static async Task ScrapeAsync(string url, string targetFolder)
		{
			targetFolder = targetFolder.EndsWith(Path.DirectorySeparatorChar) ? targetFolder : (targetFolder + Path.DirectorySeparatorChar);

			var response = await HttpClient.GetStringAsync(url);
			var doc = new HtmlDocument();
			doc.LoadHtml(response);
			//var doc = new HtmlDocument();
			//doc.Load("temp.html");
			var desc = doc.DocumentNode.SelectSingleNode("//*[@id=\"description\"]");
			if (desc == null)
				return;

			var tasks = new List<Task>();
			var imgIndex = 0;
			foreach (var img in desc.SelectNodes("//img"))
			{
				var data = img.GetDataAttribute("original");
				if (data?.Value != null && !data.Value.EndsWith("fakes.jpg") && !data.Value.EndsWith("fakes2.jpg"))
				{
					var ext = Path.GetExtension(data.Value);
					var cImgIndex = imgIndex;
					var file = cImgIndex + ext;
					if (imgIndex == 0)
						file = "cover" + ext;

					var targetPath = targetFolder + file;
					var targetFileInfo = new FileInfo(targetPath);
					if (!targetFileInfo.Exists || targetFileInfo.Length == 0)
						tasks.Add(Task.Run(() => DownloadImageAsync(data.Value, targetPath)));

					/*var task = new WebClient().DownloadFileTaskAsync(data.Value.Replace("240p", "720p"), targetPath + file);
					var retry = task.ContinueWith(async _ => { await new WebClient().DownloadFileTaskAsync(data.Value, targetPath + file); }, TaskContinuationOptions.OnlyOnFaulted);

					tasks.Add(Task.WhenAll(task, retry).ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously));*/

					imgIndex++;
				}
			}

			desc.InnerHtml = desc.InnerHtml.Replace("\r", "").Replace("\n", "").Replace("<br>", "\r\n").Replace("</li>", "\r\n").Replace("<li>", "-");
			var innerText = desc.InnerText;
			var i = innerText.IndexOf("Problems during installation?", StringComparison.Ordinal);
			if (i >= 0)
				innerText = innerText[..i];
			innerText = innerText.Trim('\r', '\n', ' ', '\t');

			var readmePath = targetFolder + "readme.txt";
			if (!File.Exists(readmePath))
				await File.WriteAllTextAsync(readmePath, innerText);

			await Task.WhenAll(tasks);

			var title = GetTitle(innerText);
			var newFolder = Path.Combine(Path.GetDirectoryName(targetFolder.TrimEnd(Path.DirectorySeparatorChar)) ?? "", title);

			if (targetFolder != newFolder &&
                MessageBox.Show("Rename to..\r\n" + title, "", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
                for (int t = 0; t < 3; t++)
				{
					try
					{
						Directory.Move(targetFolder, newFolder);
						break;
					}
					catch (IOException ex) when (ex.Message == "The process cannot access the file because it is being used by another process.")
					{
						MessageBox.Show("Directory in use, hit OK to try again.");
					}
				}
			}
		}

		private static async Task DownloadImageAsync(string url, string targetPath)
		{
			var url720p = url.Replace("240p", "720p");

			for (var i = 0; i < 3; i++)
			{
				try
				{
					await new WebClient().DownloadFileTaskAsync(url720p, targetPath);
					return;
				}
				catch
				{
				}
			}

			if (url == url720p)
				return;

			for (var i = 0; i < 3; i++)
			{
				try
				{
					await new WebClient().DownloadFileTaskAsync(url, targetPath);
					return;
				}
				catch
				{
				}
			}
		}

		private static readonly Regex RegexEdition = new Regex(@"[:\-] (?<edition>[\w\s'""]+) Edition| \((?<edition>[\w\s'""]+) Edition\)", RegexOptions.IgnoreCase);

		private static string GetTitle(string innerText)
		{
			Match editionMatch = null;
			string edition = null;
			var split = innerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var line in split)
			{
				editionMatch = RegexEdition.Match(line);
				if (editionMatch.Success)
				{
					edition = editionMatch.Groups["edition"].Value;
					break;
				}
			}

			var title = split[0].Trim();
			if (edition != null)
			{
				title = title.Replace(editionMatch.Value, "").Trim();
				title += " (" + edition + " Edition)";
			}
			var releaseDate = GetReleaseDate(innerText);
			if (releaseDate != null)
				title += " (" + releaseDate.Value.Year + ")";

            title = title.Replace(": ", " - ");

            foreach (var c in Path.GetInvalidFileNameChars())
                title = title.Replace(c, '_');

            return title;
		}

		private static DateTime? GetReleaseDate(string innerText)
		{
			foreach (var line in innerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
			{
				var match = RegexReleaseDate.Match(line);
				if (match.Success && DateTime.TryParse(match.Groups["releaseDate"].Value, out var r))
					return r;
			}

			return null;
		}

		private static IEnumerable<string> EnumerateFirefoxCookieDbPaths()
		{
			var profiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Mozilla\Firefox\Profiles");
            foreach (var dir in Directory.EnumerateDirectories(profiles))
            {
                var cookieDbPath = Path.Combine(dir, "cookies.sqlite");
                if (File.Exists(cookieDbPath))
                    yield return cookieDbPath;
            }
        }

		private static void AttachFirefoxCookiesAndUserAgent(HttpClient client)
		{
			var cookieDbPath = EnumerateFirefoxCookieDbPaths().OrderByDescending(File.GetLastWriteTime).First();
			var connectionString = $"Data Source={cookieDbPath}";
			using var connection = new SqliteConnection(connectionString);
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT name, value, path FROM moz_cookies WHERE host = '.1337x.to'";
			using var reader = command.ExecuteReader();
			while (reader.Read())
			{
				var name = reader.GetString(0);
                var value = reader.GetString(1);
                var path = reader.GetString(2);
                var cookie = new Cookie(name, value, path, "1337x.to");
                client.DefaultRequestHeaders.Add("Cookie", cookie.ToString());
            }

			var firefoxPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", "firefox.exe");
            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(firefoxPath);
            var version = fvi.ProductMajorPart + "." + fvi.ProductMinorPart;
            client.DefaultRequestHeaders.Add("User-Agent", $"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:{version}) Gecko/20100101 Firefox/{version}");
        }
    }
}