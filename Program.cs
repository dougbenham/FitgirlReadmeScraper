using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using HtmlAgilityPack;

namespace FitgirlReadmeScraper
{
    static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static async Task Main(string[] args)
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

        private static async Task ScrapeAsync(string url, string targetFolder)
        {
            targetFolder = targetFolder.EndsWith(Path.DirectorySeparatorChar) ? targetFolder : (targetFolder + Path.DirectorySeparatorChar);

            var web = new HtmlWeb();
            var doc = await web.LoadFromWebAsync(url);
            //var doc = new HtmlDocument();
            //doc.Load("temp.html");
            var desc = doc.DocumentNode.SelectSingleNode("//*[@id=\"description\"]");

            var tasks = new List<Task>();
            var imgIndex = 0;
            foreach (var img in desc.SelectNodes("//img"))
            {
                var data = img.GetDataAttribute("original");
                if (data?.Value != null && !data.Value.EndsWith("fakes.jpg"))
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

            var readmePath = targetFolder + "readme.txt";
            if (!File.Exists(readmePath))
            {
                desc.InnerHtml = desc.InnerHtml.Replace("\r", "").Replace("\n", "").Replace("<br>", "\r\n").Replace("</li>", "\r\n").Replace("<li>", "-");
                var innerText = desc.InnerText;
                var i = innerText.IndexOf("Problems during installation?", StringComparison.Ordinal);
                if (i >= 0)
                    innerText = innerText[..i];
                innerText = innerText.Trim('\r', '\n', ' ', '\t');

                await File.WriteAllTextAsync(readmePath, innerText);
            }

            await Task.WhenAll(tasks);
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
    }
}
