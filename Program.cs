using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TVPromo.Models;
using TVPromo.Templates;

namespace TVPromo
{
    class Program
    {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        /// First argument is the Show Name:
        ///     "G" (or any word starting with "G" or "g") for GCAST
        ///     "T" (or any word starting with "T" or "t") for Technology and Firends
        /// Second argument is the VideoId from YouTube
        /// <remarks>
        /// The following keys are required in the app.config file:
        ///     YouTubeApiKey
        ///     OutputFolder
        /// </remarks>
        /// <see cref=">"/>
        static void Main(string[] args)
        {
            var showArg = "GCAST";
            var videoId = "";
            if (args.Count()<2)
            {
                DisplayMissingArgsMessage();
                Console.ReadLine();
                return;
            }
            showArg = args[0];
            videoId = args[1];
            Show show = Show.TECHNOLOGYANDFRIENDS;
            var firstArg = showArg.ToUpper().Substring(0, 1);
            switch (firstArg)
            {
                case "G":
                    show = Show.GCAST;
                    break;
                case "T":
                    show = Show.TECHNOLOGYANDFRIENDS;
                    break;

                default:
                    DisplayMissingArgsMessage();
                    return;
            }

            GeneratePost(videoId, show).Wait();
            Console.ReadLine();
        }

        private static void DisplayMissingArgsMessage()
        {
            Console.WriteLine();
            Console.WriteLine("Syntax Error!");
            Console.WriteLine("Missing argument(s)");
            Console.WriteLine();
            Console.WriteLine("Syntax:");
            Console.WriteLine(" TVPromo <show> <videoId>");
            Console.WriteLine("where:");
            Console.WriteLine("-show='G' for GCAST or 'T' for Technology and Friends");
            Console.WriteLine("-videoId=YouTube video Id");
            Console.WriteLine();
            Console.WriteLine("e.g.,");
            Console.WriteLine(" TVPromo G ZobLLRBbN04");
            Console.WriteLine(" TVPromo T Xw80hwp4TdI&t");
            Console.WriteLine();
            Console.WriteLine("Press ENTER to end program");
        }

        private static async Task GeneratePost(string videoId, Show show)
        {

            try
            {
                Snippet snippet = await GetYouTubeData(videoId);
                if (snippet == null)
                {
                    return;
                }

                var episodeNumber = "";
                string title = snippet.title;
                string description = snippet.description;
                if (title == null)
                {
                    Console.WriteLine("No video title found for ID {0}", videoId);
                    return;
                }

                ParseOutEpisodeNumberAndTitle(ref episodeNumber, ref title, show);

                var appSettings = ConfigurationManager.AppSettings;
                var outputFolder = appSettings["OutputFolder"]?.ToString();
                if (outputFolder == null)
                {
                    Console.WriteLine("OutputFolder value is missing from Config file");
                    return;
                }
                string outputText = "";
                if (show == Show.TECHNOLOGYANDFRIENDS)
                {
                    outputText = GenerateTfOutputText(videoId, episodeNumber, title, description);
                }
                else
                {
                    outputText = GenerateGCastOutputText(videoId, episodeNumber, title, description);
                }

                string outputFilePrefix = "TF";
                if (show == Show.GCAST)
                {
                    outputFilePrefix = "GCAST";
                }
                string outputFile = string.Format("{0}{1}.txt", outputFilePrefix, episodeNumber);
                string outputFilePath = Path.Combine(outputFolder, outputFile);
                WriteOutputToFile(outputText, outputFilePath);

                Console.WriteLine(outputText);
                Console.WriteLine("File create at {0}", outputFilePath);

                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error");
                Console.WriteLine(ex.Message);
                return;
            }
        }

        private static void WriteOutputToFile(string outputText, string outputFilePath)
        {
            using (StreamWriter outputFile = new StreamWriter(outputFilePath))
            {
                outputFile.WriteLine(outputText);
            }
        }

        private static string GenerateTfOutputText(string videoId, string episodeNumber, string title, string description)
        {
            var tt = new TechnologyAndFriends();
            tt.Session = new Dictionary<string, object>();
            tt.Session["episodeNumber"] = episodeNumber;
            tt.Session["videoId"] = videoId;
            tt.Session["title"] = title;
            tt.Session["description"] = description;

            tt.Initialize();
            var output = tt.TransformText();
            return output;
        }

        private static string GenerateGCastOutputText(string videoId, string episodeNumber, string title, string description)
        {
            var tt = new GCast();
            tt.Session = new Dictionary<string, object>();
            tt.Session["episodeNumber"] = episodeNumber;
            tt.Session["videoId"] = videoId;
            tt.Session["title"] = title;
            tt.Session["description"] = description;

            tt.Initialize();
            var output = tt.TransformText();
            return output;
        }

        /// <summary>
        /// Parse out the Episode number and the actual title from the full title (which conatins both)
        /// </summary>
        /// <remarks>
        /// Assumption: Titles are in the following format:
        ///   Technology and Friends:
        ///     Episode xxx: lorem ipsum on lorem ipsum
        ///   GCast:
        ///     GCast xxx: Lorem ipsum
        /// </remarks>
        /// <param name="episodeNumber"></param>
        /// <param name="title"></param>
        /// <param name="show"></param>
        private static void ParseOutEpisodeNumberAndTitle(ref string episodeNumber, ref string title, Show show)
        {
            var firstWord = "Episode";
            if (show == Show.GCAST)
            {
                firstWord = "GCast";
            }
            var wordAfterEpisodeRegEx = string.Format(@"(?<=\b{0}\s)(\w+)", firstWord);
            var rx = new Regex(wordAfterEpisodeRegEx, RegexOptions.IgnoreCase);
            MatchCollection matches = rx.Matches(title);
            if (matches.Count > 0)
            {
                var firstMatch = matches[0];
                episodeNumber = firstMatch.Value;

                var episodeMarker = episodeNumber + ": ";

                var textAfterEpisodeNumberRegEx = string.Format("(?<={0}: ).*$", episodeNumber);
                rx = new Regex(textAfterEpisodeNumberRegEx);
                matches = rx.Matches(title);
                if (matches.Count > 0)
                {
                    firstMatch = matches[0];
                    title = firstMatch.Value;
                }
            }
            else
            {
                var msg = string.Format("Did not find the expected starting word '{0}' for title ({1}) of show {2}", firstWord, title, show.ToString());
                throw new ApplicationException(msg);
            }
        }

        static async Task<Snippet> GetYouTubeData(string videoId)
        {
            var appSettings = ConfigurationManager.AppSettings;
            var apiKey = appSettings["YouTubeApiKey"]?.ToString();
            if (apiKey == null)
            {
                Console.WriteLine("YouTubeApiKey value is missing from Config file");
                return null;
            }

            var url = string.Format("https://www.googleapis.com/youtube/v3/videos?part=snippet&id={0}&key={1}", videoId, apiKey);
            var client = new HttpClient();

            HttpResponseMessage response = await client.GetAsync(url);

            var videoAsString = await response.Content.ReadAsStringAsync();
            var youtubeVideo = JsonConvert.DeserializeObject<YouTubeVideo>(videoAsString);

            var snippet = new Snippet();
            if (youtubeVideo.items.Length > 0)
            {
                var item = youtubeVideo.items[0];
                snippet = item.snippet;
            }

            return snippet;
        }
    }
}
