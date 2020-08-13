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
using System.Collections.Concurrent;

namespace ThreadNotifier
{

    class Program
    {
        static void Main(string[] args) => new Program().RunBotAsync().GetAwaiter().GetResult();

        // Const things
        private static int DELAY = 60 * 1000;                           // 60 sec (milliseconds)
        private static int NEW_THREAD_MAX_TIMEOUT = 60 * 60;            // 60 min (seconds)
        private static int MAX_SECONDS_BETWEEN_REQUEST = 5 * 1000;      // 5 sec (miliseconds)
        private static ulong CHANNEL_ID = 730891176114389150;           // textchannel id

        private static string BOT_PREFIX = "!!";
        private static string TOKEN = "YOUR_TOKEN_HERE";

        public static string ROOT_URL = @"https://forum.wowcircle.net";
        // Mimic mozilla 
        private static string USER_AGENT = @"User-Agent: Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:78.0) Gecko/20100101 Firefox/78.0";

        private static string CFG_PREFIX = "[UserForumList]:";
        private static string CFG_SEPARATOR = "\r\n";


        private DiscordSocketClient _client;
        private CommandService _commands;
        private IServiceProvider _services;

        // Private data
        // Pairs: ForumUrl str -> WowCircleForum object
        private static ConcurrentDictionary<string, WowCircleForum> snapshot = new ConcurrentDictionary<string, WowCircleForum>();
        // List of subscribtions (forum urls)
        private static ConcurrentBag<string> subs = new ConcurrentBag<string>();
        // Cache of visited threads
        private static ConcurrentBag<string> visitedThreads = new ConcurrentBag<string>();

        private static Configuration cfg = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

        private static bool isWorkerThreadStart = false;

        private static object locker = new object();

        public async Task RunBotAsync()
        {
            // Read resources (list of forums)
            await LoadResourcesAsync();

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
            if (message.HasStringPrefix(BOT_PREFIX, ref argPos))
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
        private static async Task LoadResourcesAsync()
        {
            //cfg.AppSettings.Settings.Clear();
            Console.WriteLine("Loading resources...");

            if (cfg.AppSettings.Settings[CFG_PREFIX] == null) {
                cfg.AppSettings.Settings.Add(CFG_PREFIX, "");
                Console.WriteLine("Loading resources complete");
                return;
            }

            foreach (var key in cfg.AppSettings.Settings.AllKeys)
            {
                Console.WriteLine("KEY={0}, Value=\n{1}\n\n\n", key, cfg.AppSettings.Settings[key].Value);

                if (key.Equals(CFG_PREFIX))
                {
                    string value = cfg.AppSettings.Settings[key].Value;

                    Console.WriteLine("Caching data...");
                    List<string> forums = new List<string>(value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries).ToArray());

                    foreach (var forumUrl in forums)
                    {
                        var path = resolveFullPath(forumUrl);
                        Console.WriteLine("\t{0}", path);
                        if (!subs.Contains(path))
                        {
                            subs.Add(path);

                            var forumObj = await fetchForum(path);

                            snapshot.GetOrAdd(path, forumObj);

                            for(int i = 0; i < snapshot[path].threads.Count; ++i)
                            {
                                ForumThread thread = snapshot[path].threads[i];
                                
                                if(!visitedThreads.Contains(thread.ID))
                                {
                                    visitedThreads.Add(thread.ID);
                                }
                                snapshot[path].threads[i].IsNew = false;
                            }
                        }
                    }
                }
            }

            Console.WriteLine("Loading resources complete");
        }

        private static async Task<bool> RefreshCfgAsync(List<string> servers)
        {
            return await Task.Run(() =>
            {
                // Refresh cfg 
                cfg.AppSettings.Settings[CFG_PREFIX].Value = String.Join(CFG_SEPARATOR, servers.ToArray());
                // Save changes
                cfg.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
                return true;
            });
        }

        public static async Task<bool> AddServer(string forumUrl)
        {
            forumUrl = resolveFullPath(forumUrl);

            if (subs.Contains(forumUrl)) return false;

            string value = cfg.AppSettings.Settings[CFG_PREFIX].Value;
            List<string> forums = new List<string>(value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries));


            // Add to local list
            if (!forums.Contains(forumUrl))
                forums.Add(forumUrl);

            // Add to subs list
            subs.Add(forumUrl);

            await RefreshCfgAsync(forums);
            
            return true;
        }
        public static async Task<bool> RemoveServerAsync(string forumUrl)
        {
            forumUrl = resolveFullPath(forumUrl);
                
            if (!subs.Contains(forumUrl)) return false;
                
            string value = cfg.AppSettings.Settings[CFG_PREFIX].Value;
            List<string> forums = new List<string>(value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries));
                
            // Remove from local list
            if (forums.Contains(forumUrl))
                forums.Remove(forumUrl);
                
            // Remove from subs list
            subs = new ConcurrentBag<string>(subs.Except(new string[] { forumUrl }));
                
            WowCircleForum forumObj = null;
            if (snapshot.ContainsKey(forumUrl))
                snapshot.Remove(forumUrl, out forumObj);

            await RefreshCfgAsync(forums);

            return true;
        }

        public static async Task<bool> RemoveAllAsync()
        {
            return await Task.Run( () =>
            {
                var urls = subs.ToArray();

                for (int i = 0; i < urls.Length; ++i)
                {
                    urls[i] = resolveFullPath(urls[i]);
                }

                string value = cfg.AppSettings.Settings[CFG_PREFIX].Value;
                List<string> forums = new List<string>(value.Split(CFG_SEPARATOR, StringSplitOptions.RemoveEmptyEntries));

                // Remove from subs list
                subs = new ConcurrentBag<string>(subs.Except(urls));

                WowCircleForum forumObj = null;
                foreach (var forumUrl in urls)
                {
                    if (snapshot.ContainsKey(forumUrl))
                        snapshot.Remove(forumUrl, out forumObj);
                }

                return RefreshCfgAsync(forums);
            });
        }
        public static async Task<bool> IsSubscriptionExistsAsync(string forumUrl)
        {
            return await Task.Run(() => subs.Contains(forumUrl));
        }

        public static async Task<ConcurrentBag<string>> GetSubscriptionsAsync()
        {
            return await Task.Run(() => subs);
        }

        #endregion

        #region WorkLoop methods
        private Task OnClientReady()
        {
            if (isWorkerThreadStart)
                return Task.CompletedTask;


            isWorkerThreadStart = true;

            Thread th = new Thread(WorkLoop);
            th.IsBackground = true;
            th.Start();
            return Task.CompletedTask;
        }
        private async void WorkLoop()
        {

            while (true)
            {
                try
                {
                    await updateCache();
                    await sendNotifications(_client);

                    int sleepTimeout = new Random().Next(DELAY, 2 * DELAY);
                    Console.Write("Pause for {0} ms....", sleepTimeout);
                    Thread.Sleep(sleepTimeout);
                    Console.WriteLine("Done.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error: {0}|||{1}", ex.Message, ex.StackTrace);
                }
            }
        }

        private static async Task updateCache()
        {
            try
            {
                if (subs.Count == 0)
                {
                    Console.WriteLine("=================[Update cache declined(subs list is empty): {0}]=================", DateTime.Now);
                    return;
                }
                Console.WriteLine("=================[Update cache starts at: {0}]=================", DateTime.Now);

                // Update snapshot data
                foreach (var forumUrl in subs)
                {
                    Console.WriteLine("Fetching data for {0}...", forumUrl);
                    WowCircleForum forumObj = null;
                    if (!snapshot.ContainsKey(forumUrl))
                        snapshot.GetOrAdd(forumUrl, forumObj);
                    snapshot[forumUrl] = await fetchForum(forumUrl);

                    // Update thread status
                    for (int i = 0; i < snapshot[forumUrl].threads.Count; ++i)
                    {
                        var thread = snapshot[forumUrl].threads[i];
                        double elapsed = DateTime.Now.Subtract(thread.CreatedAt).TotalSeconds;

                        if (elapsed < NEW_THREAD_MAX_TIMEOUT
                            && !visitedThreads.Contains(thread.ID))
                        {
                            visitedThreads.Add(thread.ID);
                            snapshot[forumUrl].threads[i].IsNew = true;
                            Console.WriteLine("New thread found: {0}\n\n", thread.ID);
                        }
                    }

                    int sleepTimeout = new Random().Next(MAX_SECONDS_BETWEEN_REQUEST + 1);
                    Console.WriteLine("Delay {0} ms", sleepTimeout);
                    Thread.Sleep(sleepTimeout);
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
            catch (Exception ex)
            {
                Console.WriteLine("updateCache failure: {0}{1}{2}", ex.Message, ex.StackTrace, Environment.NewLine);
            }
        }

        private static async Task sendNotifications(DiscordSocketClient client)
        {
            try
            {
                var channelSocket = client.GetChannel(CHANNEL_ID) as IMessageChannel;
                var newThreads = new List<string>();
                foreach (var forumUrl in subs)
                {
                    for (int i = 0; i < snapshot[forumUrl].threads.Count; ++i)
                    {
                        if (snapshot[forumUrl].threads[i].IsNew)
                        {
                            Console.WriteLine("Sending notification, threadID={0}", snapshot[forumUrl].threads[i].ID);
                            ///////////// Construct message
                            var thread = snapshot[forumUrl].threads[i];
                            newThreads.Add(thread.ID);

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
                                ThumbnailUrl = thread.Url,
                                Fields = fields,
                                Description = thread.Description
                            };


                            await channelSocket.SendMessageAsync("", true, embed.Build());
                        }
                    }
                }


                // RESET threads flag
                foreach (var forumUrl in snapshot.Keys.ToArray())
                {
                    for (int i = 0; i < snapshot[forumUrl].threads.Count; ++i)
                    {
                        snapshot[forumUrl].threads[i].IsNew = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("sendNotifications failed: {0}|||{1}{2}", ex.Message, ex.StackTrace, Environment.NewLine);
            }
        }
        #endregion

        #region Parser
        private static string fetchForumTitle(string forumUrl)
        {
            string forumTitle = forumUrl;

            try
            {
                forumUrl = resolveFullPath(forumUrl);
                HtmlWeb web = new HtmlWeb();
                web.UserAgent = USER_AGENT;
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
                Console.Error.WriteLine("fetchForumTitle failure: {0}", ex.Message);
            }

            return forumTitle;
        }

        private static string getAvatarUrl(string profileUrl)
        {
            profileUrl = resolveFullPath(profileUrl);
            var uri = new Uri(profileUrl);
            return resolveFullPath(@"/image.php" + uri.Query);
        }

        private static Task<WowCircleForum> fetchForum(string forumUrl)
        {
            return Task.Run(() =>
            {
                forumUrl = resolveFullPath(forumUrl);
                
                WowCircleForum forum = new WowCircleForum()
                {
                    Title = fetchForumTitle(forumUrl),
                    Url = forumUrl,
                    threads = fetchThreads(forumUrl)
                };
                
                return forum;
            });
        }

        private static List<ForumThread> fetchThreads(string forumUrl)
        {
            List<ForumThread> threads = new List<ForumThread>();

            try
            {
                HtmlWeb web = new HtmlWeb();
                web.UserAgent = USER_AGENT;
                web.UseCookies = true;
                web.UsingCache = false;
                web.CacheOnly = false;
                web.UsingCacheIfExists = false;

                HtmlDocument doc = web.Load(forumUrl);
                //var ol = doc.GetElementbyId("threads");
                var nodes = doc.DocumentNode.SelectNodes("//li[contains(@class, 'threadbit')]").ToArray();
                foreach (var node in nodes)
                {
                    var threadTitle = node.SelectSingleNode(".//h3[contains(@class, 'threadtitle')]");
                    var threadMeta = node.SelectSingleNode(".//div[contains(@class, 'threadmeta')]");
                    var threadInfo = node.SelectSingleNode(".//div[contains(@class, 'threadinfo')]");
                    var description = threadInfo.GetAttributeValue("title", string.Empty);


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
                        Description = description,
                        IsNew = false
                    };

                    threads.Add(thread);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("fetchThreads failure: {0}", ex.Message);
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
