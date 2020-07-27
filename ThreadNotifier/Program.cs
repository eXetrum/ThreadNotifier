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
        private static int MAX_SECONDS_BETWEEN_UPDATE = 5 * 60; // 5 min (seconds)


        private static string TOKEN = "NzM1ODE1MDYwMDUxNDYwMTU1.XxlvQQ.ilGSoF8cm-HJ73PZNQztL1NBrsU";//"YOUR_TOKEN_HERE";
        private static string ANNOUNCEMENT_MESSAGE = "АХТУНГ ! ШОТОПРОИЗОШЛО !";

        public static string ROOT_URL = @"https://forum.wowcircle.net";
        // Mimic mozilla 
        private static string USER_AGENT = @"User-Agent: Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:78.0) Gecko/20100101 Firefox/78.0";

        public static string CFG_PREFIX = "[Username]:";
        public static string CFG_SEPARATOR = "\r\n";


        private DiscordSocketClient _client;
        private CommandService _commands;
        private IServiceProvider _services;

        // Public data
        // Pairs: ForumUrl str -> WowCircleForum object
        public static Dictionary<string, WowCircleForum> snapshot = new Dictionary<string, WowCircleForum>();
        // Pairs: Discord Nickname str -> List of strings of forums (urls)
        public static Dictionary<string, List<string>> subscribers = new Dictionary<string, List<string>>();
        // Pairs: Discord Nickname str -> ThreadID str (global cache)
        //public static Dictionary<string, HashSet<string>> globalThreadCache = new Dictionary<string, HashSet<string>>();
        public static HashSet<string> globalThreadCache = new HashSet<string>();

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

        #region Discord Handlers
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
        #endregion

        #region Load/Add/Remove forum methods
        private void LoadResources()
        {
            Console.WriteLine("Loading resources...");
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

                    Console.WriteLine("Caching data for user [{0}]", username);
                    string[] forums = value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var forumUrl in forums)
                    {
                        var path = resolveFullPath(forumUrl);
                        Console.WriteLine("\t{0}", path);
                        if (!subscribers[username].Contains(path))
                        {
                            subscribers[username].Add(path);
                            
                            if(!snapshot.ContainsKey(path))
                                snapshot[path] = fetchForum(path);

                            for(int i = 0; i < snapshot[path].threads.Count; ++i)
                            {
                                ForumThread thread = snapshot[path].threads[i];
                                
                                //
                                //ensureUserExist(username);
                                if(!globalThreadCache.Contains(thread.ID))
                                {
                                    globalThreadCache.Add(thread.ID);
                                }
                                snapshot[path].threads[i].IsNew = false;
                            }
                        }
                    }
                }
            }

            Console.WriteLine("Loading resources complete");
        }
        

        public static void AddServer(string username, string forumUrl)
        {
            forumUrl = resolveFullPath(forumUrl);

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
            snapshot[forumUrl] = fetchForum(forumUrl);
            foreach(var thread in snapshot[forumUrl].threads)
            {
                //ensureUserExist(username);
                if (!globalThreadCache.Contains(thread.ID))
                    globalThreadCache.Add(thread.ID);
            }
        }
        public static void RemoveServer(string username, string forumUrl)
        {
            forumUrl = resolveFullPath(forumUrl);

            if (cfg.AppSettings.Settings[CFG_PREFIX + username] == null)
                cfg.AppSettings.Settings.Add(CFG_PREFIX + username, "");

            string value = cfg.AppSettings.Settings[CFG_PREFIX + username].Value;
            string[] servers = value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);

            StringBuilder sb = new StringBuilder();
            foreach (var server in servers)
            {
                if (server.Equals(forumUrl)) continue;
                sb.Append(server + CFG_SEPARATOR);
            }

            cfg.AppSettings.Settings[CFG_PREFIX + username].Value = sb.ToString();
            cfg.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
        #endregion

        #region WorkLoop methods
        private Task OnClientReady()
        {
            Thread th = new Thread(WorkLoop);
            th.IsBackground = true;
            th.Start();
            return Task.CompletedTask;
        }
        private void WorkLoop()
        {

            while (true)
            {
                try
                {
                    updateCache();
                    notifySubscribers();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error: {0}|||{1}", ex.Message, ex.StackTrace);
                }

                int sleepTimeout = new Random().Next(DELAY, 2 * DELAY);
                Thread.Sleep(sleepTimeout);
            }
        }

        private static void updateCache()
        {
            //Dictionary<string, WowCircleForum> oldsnapshot = new Dictionary<string, WowCircleForum>(snapshot);

            Console.WriteLine("=================[Update cache: {0}]=================", DateTime.Now);

            foreach (var forumUrl in snapshot.Keys.ToArray())
            {
                snapshot[forumUrl] = fetchForum(forumUrl);                
                // Update thread status
                for (int i = 0; i < snapshot[forumUrl].threads.Count; ++i)
                {
                    
                    double elapsed = DateTime.Now.Subtract(snapshot[forumUrl].threads[i].CreatedAt).TotalSeconds;    

                    if (elapsed < MAX_SECONDS_BETWEEN_UPDATE
                        && !globalThreadCache.Contains(snapshot[forumUrl].threads[i].ID))
                    {
                        globalThreadCache.Add(snapshot[forumUrl].threads[i].ID);
                        snapshot[forumUrl].threads[i].IsNew = true;
                        Console.WriteLine("New thread found: {0}\n\n", snapshot[forumUrl].threads[i].ID);
                    }
                }
            }
            // Sort
            foreach (var forumUrl in snapshot.Keys.ToArray())
            {
                snapshot[forumUrl].threads.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
                Console.WriteLine("Forum: {0}", snapshot[forumUrl].Title);
                for (int i = 0; i < snapshot[forumUrl].threads.Count; ++i)
                {
                    double elapsed = DateTime.Now.Subtract(snapshot[forumUrl].threads[i].CreatedAt).TotalSeconds;

                    Console.WriteLine("{0,-25} {1,-25} {2, -20}",
                        snapshot[forumUrl].threads[i].ID,
                        snapshot[forumUrl].threads[i].CreatedAt,
                        elapsed);
                }
                Console.WriteLine();
            }
            Console.WriteLine("=================[Cache timestamp: {0}]=================", DateTime.Now);
        }

        private void notifySubscribers()
        {
            foreach (var userName in subscribers.Keys)
            {
                string[] chunks = userName.Split('#');
                string username = chunks[0];
                string discriminator = chunks[1];

                foreach (var forumUrl in subscribers[userName])
                {
                    for (int i = 0; i < snapshot[forumUrl].threads.Count; ++i)
                    {
                        if (snapshot[forumUrl].threads[i].IsNew)
                        {

                            SocketUser userSocket = _client.GetUser(username, discriminator);

                            ///////////// Construct message
                            var thread = snapshot[forumUrl].threads[i];

                            var fields = new List<EmbedFieldBuilder>();

                            fields.Add(new EmbedFieldBuilder()
                            {
                                IsInline = false,
                                Name = "createdAt",
                                Value = thread.CreatedAt
                            });

                            fields.Add(new EmbedFieldBuilder()
                            {
                                IsInline = false,
                                Name = "ID",
                                Value = thread.ID
                            });

                            var embed = new EmbedBuilder()
                            {
                                Title = thread.Title,
                                Url = thread.Url,
                                Author = new EmbedAuthorBuilder()
                                {
                                    Name = thread.Author.NickName,
                                    Url = thread.Author.ProfileUrl,
                                    IconUrl = thread.Author.AvatarUrl
                                },
                                //ThumbnailUrl = thread.Url,
                                //ImageUrl = @"https://forum.wowcircle.net/image.php?u=1843&type=thumb",
                                Fields = fields
                            };

                            userSocket.SendMessageAsync(ANNOUNCEMENT_MESSAGE, false, embed.Build());
                        }
                    }
                }
            }


            // RESET
            foreach (var userName in subscribers.Keys)
            {
                string[] chunks = userName.Split('#');
                string username = chunks[0];
                string discriminator = chunks[1];

                foreach (var forumUrl in subscribers[userName])
                {
                    for (int i = 0; i < snapshot[forumUrl].threads.Count; ++i)
                    {
                        snapshot[forumUrl].threads[i].IsNew = false;
                    }
                }
            }

        }
        #endregion

        #region Parser
        private static string fetchForumTitle(string forumUrl)
        {
            string forumTitle = forumUrl;

            forumUrl = resolveFullPath(forumUrl);
            HtmlWeb web = new HtmlWeb();
            web.UserAgent = USER_AGENT;
            web.UseCookies = true;
            web.UsingCache = false;

            try
            {
                web.UseCookies = true;
                web.CacheOnly = false;
                web.UsingCache = false;
                web.UsingCacheIfExists = false;

                HtmlDocument doc = web.Load(forumUrl);
                var title = doc.DocumentNode.SelectSingleNode("//span[contains(@class, 'forumtitle')]");
                forumTitle = title.InnerText.Trim();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: {0}", ex.Message);
            }

            return forumTitle;
        }

        private static string getAvatarUrl(string profileUrl)
        {
            profileUrl = resolveFullPath(profileUrl);
            var uri = new Uri(profileUrl);
            return resolveFullPath(@"/image.php" + uri.Query);
        }

        private static WowCircleForum fetchForum(string forumUrl)
        {
            forumUrl = resolveFullPath(forumUrl);

            WowCircleForum forum = new WowCircleForum()
            {
                Title = fetchForumTitle(forumUrl),
                Url = forumUrl,
                threads = fetchThreads(forumUrl)
            };

            return forum;
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
                            ProfileUrl = resolveFullPath(threadMetaLink.GetAttributeValue("href", string.Empty)),
                            AvatarUrl = getAvatarUrl(threadMetaLink.GetAttributeValue("href", string.Empty))
                        },
                        IsNew = false
                    };

                    threads.Add(thread);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: {0}", ex.Message);
            }

            return threads;
        }

        private static string resolveFullPath(string relativePath)
        {
            if (!relativePath.StartsWith(ROOT_URL))
                return ROOT_URL + "/" + relativePath;
            return relativePath;
        }

        private static void parseTime(string[] timeChunks, out int hour, out int min)
        {
            hour = min = 0;
            if (timeChunks.Length == 2)
            {
                Int32.TryParse(timeChunks[0], out hour);
                Int32.TryParse(timeChunks[1], out min);
            }
        }

        private static void parseDate(string[] dateChunks, out int day, out int month, out int year)
        {
            day = month = year = 0;
            if (dateChunks.Length == 3)
            {
                Int32.TryParse(dateChunks[0], out day);
                Int32.TryParse(dateChunks[1], out month);
                Int32.TryParse(dateChunks[2], out year);
            }
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

                    if (createdAt.Contains("Сегодня"))
                    {
                        string[] timeChunks = chunks[1].Split(':', StringSplitOptions.RemoveEmptyEntries);
                        parseTime(timeChunks, out hour, out min);

                        var now = DateTime.Now;
                        return new DateTime(now.Year, now.Month, now.Day, hour, min, 0);
                    }
                    else if (createdAt.Contains("Вчера"))
                    {
                        string[] timeChunks = chunks[1].Split(':', StringSplitOptions.RemoveEmptyEntries);
                        parseTime(timeChunks, out hour, out min);

                        var now = DateTime.Now.AddDays(-1);
                        return new DateTime(now.Year, now.Month, now.Day, hour, min, 0);
                    }
                    else
                    {

                        // Date
                        string[] dateChunks = chunks[0].Split('.', StringSplitOptions.RemoveEmptyEntries);
                        parseDate(dateChunks, out day, out month, out year);

                        // Time
                        string[] timeChunks = chunks[1].Split(':', StringSplitOptions.RemoveEmptyEntries);
                        parseTime(timeChunks, out hour, out min);
                    }
                    return new DateTime(year, month, day, hour, min, 0);
                }
            }
            catch (Exception ex) { }

            return DateTime.Now;
        }
        #endregion

    }
}
