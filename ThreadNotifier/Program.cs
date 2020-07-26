using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

// Discord API lib
using Discord;
using Discord.Commands;
using Discord.WebSocket;

// HTML Parser
using HtmlAgilityPack;
using System.Configuration;
using System.IO;

namespace ThreadNotifier
{

    class Program
    {
        static void Main(string[] args) => new Program().RunBotAsync().GetAwaiter().GetResult();

        // Const things
        private static int DELAY = 60 * 1000; // 60 sec (milliseconds)
        private static int MAX_SECONDS_BETWEEN_UPDATE = 15 * 60; // 15 min (seconds)


        private static string TOKEN = "NzM1ODE1MDYwMDUxNDYwMTU1.XxlvQQ.ilGSoF8cm-HJ73PZNQztL1NBrsU";//"YOUR_TOKEN_HERE";
        private static string ANNOUNCEMENT_MESSAGE = "АХТУНГ ! ШОТОПРОИЗОШЛО !";

        private static string ROOT_URL = @"https://forum.wowcircle.net";
        // Mimic mozilla 
        private static string USER_AGENT = @"User-Agent: Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:78.0) Gecko/20100101 Firefox/78.0";

        public static string CFG_PREFIX = "[Username]:";
        public static string CFG_SEPARATOR = "\r\n";


        private DiscordSocketClient _client;
        private CommandService _commands;
        private IServiceProvider _services;

        // Public data
        static public Dictionary<string, WowCircleForum> snapshot = new Dictionary<string, WowCircleForum>();
        static public Dictionary<string, List<string>> subscribers = new Dictionary<string, List<string>>();
        static Configuration cfg = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        public async Task RunBotAsync()
        {
            // Read resources prev settings
            LoadResources();

            _client = new DiscordSocketClient();
            _commands = new CommandService();

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .BuildServiceProvider();

            _client.Log += HandleLog;

            _client.Ready += OnClientReady;

            await RegisterCommandsAsync();

            await _client.LoginAsync(TokenType.Bot, TOKEN);

            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private void LoadResources()
        {
            //Console.OutputEncoding = Encoding.UTF8;

            foreach (var key in cfg.AppSettings.Settings.AllKeys)
            {
                if (key.StartsWith(CFG_PREFIX))
                {
                    string value = cfg.AppSettings.Settings[key].Value;
                    string username = key.Substring(CFG_PREFIX.Length);
                    if (!subscribers.ContainsKey(username))
                    {
                        subscribers.Add(username, new List<string>());
                    }

                    string[] forums = value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var forumUrl in forums)
                    {
                        if (!subscribers[username].Contains(forumUrl))
                        {
                            subscribers[username].Add(forumUrl);
                            snapshot[forumUrl] = new WowCircleForum()
                            {
                                Title = forumUrl,
                                Url = forumUrl,
                                threads = fetchThreads(forumUrl),
                                subForums = fetchForum(forumUrl)
                            };
                        }
                    }
                }
            }
        }

        public static void AddServer(string username, string forumUrl)
        {
            if (cfg.AppSettings.Settings[CFG_PREFIX + username] == null)
                cfg.AppSettings.Settings.Add(CFG_PREFIX + username, "");

            string value = cfg.AppSettings.Settings[CFG_PREFIX + username].Value;
            string[] forumns = value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);

            bool found = false;
            StringBuilder sb = new StringBuilder();
            foreach (var forum in forumns)
            {
                sb.Append(forum + CFG_SEPARATOR);
                if (forum.Equals(forumUrl)) found = true;
            }

            if (!found) sb.Append(forumUrl + CFG_SEPARATOR);

            cfg.AppSettings.Settings[CFG_PREFIX + username].Value = sb.ToString();
            cfg.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
            //
            snapshot[forumUrl] = new WowCircleForum()
            {
                Title = forumUrl,
                Url = forumUrl,
                threads = fetchThreads(forumUrl),
                subForums = fetchForum(forumUrl)
            };
        }
        public static void RemoveServer(string username, string rmForum)
        {
            if (cfg.AppSettings.Settings[CFG_PREFIX + username] == null)
                cfg.AppSettings.Settings.Add(CFG_PREFIX + username, "");

            string value = cfg.AppSettings.Settings[CFG_PREFIX + username].Value;
            string[] servers = value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);

            StringBuilder sb = new StringBuilder();
            foreach (var server in servers)
            {
                if (server.Equals(rmForum)) continue;
                sb.Append(server + CFG_SEPARATOR);
            }

            cfg.AppSettings.Settings[CFG_PREFIX + username].Value = sb.ToString();
            cfg.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }

        private Task OnClientReady()
        {
            Thread th = new Thread(FetchLoop);
            th.IsBackground = true;
            th.Start(ROOT_URL);
            return Task.CompletedTask;
        }

        private static List<WowCircleForum> fetchForum(string forumUrl)
        {
            List<WowCircleForum> forums = new List<WowCircleForum>();

            HtmlWeb web = new HtmlWeb();
            web.UserAgent = USER_AGENT;
            web.UseCookies = true;
            web.UsingCache = false;
            //web.CaptureRedirect = true;

            try
            {
                web.UseCookies = true;
                web.CacheOnly = false;
                web.UsingCache = false;
                web.UsingCacheIfExists = false;

                HtmlDocument doc = web.Load(forumUrl);
                HtmlNode[] nodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'forumrow')]").ToArray();

                var subForums = new List<WowCircleForum>();

                foreach (HtmlNode item in nodes)
                {
                    try
                    {
                        var title = item.SelectSingleNode(".//h2[contains(@class, 'forumtitle')]");
                        if (title == null) continue;
                        var link = title.SelectSingleNode(".//a");
                        if (link == null) continue;


                        var forum = new WowCircleForum()
                        {
                            Title = link.InnerText,
                            Url = resolveFullPath(link.GetAttributeValue("href", string.Empty))
                        };

                        Console.WriteLine(forum.Url);
                        forum.threads = fetchThreads(forum.Url);
                        forums.Add(forum);
                    }
                    catch (Exception ex) { }

                }

                return forums;

            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: {0}", ex.Message);
            }

            return forums;
        }

        private static List<ForumThread> fetchThreads(string forumUrl)
        {
            List<ForumThread> threads = new List<ForumThread>();

            HtmlWeb web = new HtmlWeb();
            web.UserAgent = USER_AGENT;
            web.UseCookies = true;
            web.UsingCache = false;
            //web.CaptureRedirect = true;

            try
            {
                web.UseCookies = true;
                web.CacheOnly = false;
                web.UsingCache = false;
                web.UsingCacheIfExists = false;

                HtmlDocument doc = web.Load(forumUrl);
                //var ol = doc.GetElementbyId("threads");
                var nodes = doc.DocumentNode.SelectNodes("//li[contains(@class, 'threadbit')]").ToArray();
                foreach (var node in nodes)
                {
                    var threadTitle = node.SelectSingleNode(".//h3[contains(@class, 'threadtitle')]");
                    var threadMeta = node.SelectSingleNode(".//div[contains(@class, 'threadmeta')]");

                    var threadTitleLink = threadTitle.SelectSingleNode(".//a");
                    var threadMetaLink = threadMeta.SelectSingleNode(".//a");
                    
                    var span = threadMeta.SelectSingleNode(".//span");
                    span.RemoveChild(threadMetaLink);
                    string dateTime = span.InnerText.Trim();

                    var thread = new ForumThread()
                    {
                        Title = threadTitleLink.InnerText.Trim(),
                        ID = threadTitleLink.GetAttributeValue("id", string.Empty),
                        Url = resolveFullPath(threadTitleLink.GetAttributeValue("href", string.Empty)),
                        CreatedAt = parseCreatedAt(dateTime),
                        Author = new ForumUser()
                        {
                            NickName = threadMetaLink.InnerText.Trim(),
                            ProfileUrl = resolveFullPath(threadMetaLink.GetAttributeValue("href", string.Empty))
                        }
                    };

                    threads.Add(thread);
                }
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine("Error: {0}", ex.Message);
            }
            
            return threads;
        }

        public static string dumpForum(WowCircleForum forum, int depth = 0)
        {
            if (forum == null) return "";
            StringBuilder sb = new StringBuilder();

            sb.Append(forum.Title + Environment.NewLine);
            string spaces = new string(' ', depth + 1);
            foreach(var sub in forum.subForums)
            {
                sb.Append(spaces + sub.Title + Environment.NewLine);
                sb.Append(dumpForum(sub, depth + 1) + Environment.NewLine + Environment.NewLine);
            }

            return sb.ToString();
        }

        private void FetchLoop(object obj)
        {
            string rootForumUrl = (string)obj;

            HtmlWeb web = new HtmlWeb();
            web.UserAgent = USER_AGENT;
            web.UseCookies = true;
            web.UsingCache = false;
            //web.CaptureRedirect = true;

            /*var rootForum = new WowCircleForum()
            {
                Title = "WowCircleForum",
                Url = rootForumUrl
            };*/

            //rootForum.subForums = fetchForum(rootForumUrl);
            //rootForum.threads = fetchThreads(rootForumUrl);
            

            Console.WriteLine("Complete");

            while (true)
            {
                try
                {
                    foreach(var nickName in subscribers.Keys)
                    {
                        foreach(var forum in subscribers[nickName])
                        {
                            var newThreads = fetchThreads(forum);

                        }
                    }

                    //dumpForum(rootForum);
                    /*
                    web.UseCookies = true;
                    web.CacheOnly = false;
                    web.UsingCache = false;
                    web.UsingCacheIfExists = false;

                    HtmlDocument doc = web.Load(URL);
                    HtmlNode[] nodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'forumrow')]").ToArray();


                    foreach (HtmlNode item in nodes)
                    {
                        try
                        {
                            var link = item.SelectSingleNode(".//h2[contains(@class, 'forumtitle')]").SelectSingleNode(".//a");
                            if (link == null) continue;


                            var forum = new WowCircleForum()
                            {
                                Title = link.InnerText,
                                Url = link.GetAttributeValue("href", string.Empty),
                                LastModified = DateTime.Now,//////////////////

                            };

                            Console.WriteLine("{0}  {1}", forum.Title, forum.Url);
                            
                        }
                        catch(Exception ex) { }
                        
                    }
                    */


                    /*int i = 0;
                    foreach (HtmlNode node in nodes)
                    {
                        File.WriteAllText("D:\\temp\\parse\\" + i + ".txt", node.InnerHtml);
                        i++;
                    }*/

                    //doc.Save("D:\\temp\\parse\\forum.txt");
                    //return;

                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error: {0}", ex.Message);
                }

                int sleepTimeout = new Random().Next(DELAY, 2 * DELAY);
                Thread.Sleep(sleepTimeout);
            }
        }

        private void updateServers()
        {
            Console.WriteLine("{0,-20}  {1,18}  {2, 16}", "Server", "LastModified", "Status");
            foreach (var serverName in snapshot.Keys)
            {
                /*
                long secondsBetweenUpdate = Convert.ToInt64(-1 * cache[serverName].UptimeLastModified.Subtract(DateTime.Now).TotalSeconds);

                Console.WriteLine("{0,-20}  {1,18}  {2, 16}",
                    serverName, secondsBetweenUpdate, cache[serverName].Status.ToString());
                // Uptime == 0 
                // Stats last modf >= 10 min
                if (cache[serverName].Uptime.Equals(TimeSpan.FromSeconds(0))
                    || secondsBetweenUpdate >= MAX_SECONDS_BETWEEN_UPDATE)
                {
                    cache[serverName].Status = WowCircleForum.StatusEnum.DOWN;
                }
                else
                {
                    cache[serverName].Status = WowCircleForum.StatusEnum.UP;
                }*/
            }
            Console.WriteLine();
        }

        private void checkSubscribers()
        {
            foreach (var userName in subscribers.Keys)
            {
                foreach (var server in subscribers[userName])
                {
                    //if (cache[server].Status == WowCircleForum.StatusEnum.DOWN)
                    {
                        string[] chunks = userName.Split('#');
                        string username = chunks[0];
                        string discriminator = chunks[1];

                        SocketUser userSocket = _client.GetUser(username, discriminator);
                        userSocket.SendMessageAsync(string.Format("{0} ->>>> {1}\n", ANNOUNCEMENT_MESSAGE, server));
                    }
                }
            }
        }

        private static string resolveFullPath(string relativePath)
        {
            if (!relativePath.StartsWith(ROOT_URL))
                return ROOT_URL + "/" + relativePath;
            return relativePath;
        }

        private static DateTime parseCreatedAt(string createdAt)
        {
            try
            {
                createdAt = createdAt.Trim(',');
                createdAt = createdAt.Replace("&nbsp;", " ").Trim();
                //"{0} д. {1} ч. {2} м. {3} с.".
                string[] chunks = createdAt.Split(' ', StringSplitOptions.RemoveEmptyEntries);


                int year = 0, month = 0, day = 0, hour = 0, min = 0;
                if (chunks.Length == 2)
                {
                    // Date
                    string[] dateChunks = chunks[0].Split('.', StringSplitOptions.RemoveEmptyEntries);

                    if (dateChunks.Length == 3)
                    {
                        Int32.TryParse(dateChunks[0], out day);
                        Int32.TryParse(dateChunks[1], out month);
                        Int32.TryParse(dateChunks[2], out year);
                    }

                    // Time
                    string[] timeChunks = chunks[1].Split(':', StringSplitOptions.RemoveEmptyEntries);
                    if (timeChunks.Length == 2)
                    {
                        Int32.TryParse(timeChunks[0], out hour);
                        Int32.TryParse(timeChunks[1], out min);
                    }

                    return new DateTime(year, month, day, hour, min, 0);
                }
            } catch(Exception ex) { }

            return DateTime.Now;
        }

        private Task HandleLog(LogMessage arg)
        {
            Console.WriteLine(arg);
            return Task.CompletedTask;
        }

        public async Task RegisterCommandsAsync()
        {
            _client.MessageReceived += HandleCommandAsync;
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        }

        private async Task HandleCommandAsync(SocketMessage arg)
        {
            var message = arg as SocketUserMessage;
            var ctx = new SocketCommandContext(_client, message);

            // Do nothing for self messages
            if (message.Author.IsBot) return;

            int argPos = 0;
            if (message.HasStringPrefix("!", ref argPos))
            {
                var res = await _commands.ExecuteAsync(ctx, argPos, _services);
                if (!res.IsSuccess)
                {
                    Console.WriteLine(res.ErrorReason);
                    await ctx.Channel.SendMessageAsync(res.ErrorReason);

                }
            }
        }
    }
}
