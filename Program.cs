using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
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
                return;
            
            var targetPath = args[0].EndsWith(Path.DirectorySeparatorChar) ? args[0] : (args[0] + Path.DirectorySeparatorChar);
            var url = args[1];

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

                    // try 720p then regular
                    var task = new WebClient().DownloadFileTaskAsync(data.Value.Replace("240p", "720p"), targetPath + file);
                    var retry = task.ContinueWith(async _ => { await new WebClient().DownloadFileTaskAsync(data.Value, targetPath + file); }, TaskContinuationOptions.OnlyOnFaulted);
                    
                    tasks.Add(Task.WhenAll(task, retry).ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously));

                    imgIndex++;
                }
            }

            desc.InnerHtml = desc.InnerHtml.Replace("\r", "").Replace("\n", "").Replace("<br>", "\r\n").Replace("</li>", "\r\n").Replace("<li>", "-");
            var innerText = desc.InnerText;
            var i = innerText.IndexOf("Problems during installation?", StringComparison.Ordinal);
            if (i >= 0)
                innerText = innerText[..i];
            innerText = innerText.Trim('\r', '\n', ' ', '\t');
            
            await File.WriteAllTextAsync(targetPath + "readme.txt", innerText);
            await Task.WhenAll(tasks);
        }
    }
}
