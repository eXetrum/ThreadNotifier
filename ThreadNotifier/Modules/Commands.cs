using Discord;
using Discord.Commands;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ThreadNotifier.Modules
{
    public class Commands : ModuleBase<SocketCommandContext>
    {
        [Command("help")]
        public async Task Help()
        {
            string[] commands = { "help", "list", "watch", "unwatch", "status" };

            StringBuilder sb = new StringBuilder();
            foreach (var item in commands) sb.Append(item + "\n");

            await ReplyAsync(sb.ToString());
        }

        [Command("list")]
        public async Task ListServers()
        {
            string nickname = Context.User.Username + "#" + Context.User.Discriminator;

            // Ensure nickname exists
            if (!Program.subscribers.ContainsKey(nickname)) Program.subscribers.Add(nickname, new List<string>());


            StringBuilder sb = new StringBuilder();
            if (Program.subscribers[nickname].Count == 0)
            {
                sb.Append("Your watching list is empty.\n");
            }
            else
            {
                foreach (var serverName in Program.subscribers[nickname])
                {
                    sb.Append(string.Format("{0}\n", serverName));
                }
            }
            await ReplyAsync(sb.ToString());
        }

        [Command("unwatch")]
        public async Task UnWatch(string forumUrl)
        {
            string nickname = Context.User.Username + "#" + Context.User.Discriminator;

            // Ensure nickname exists
            if (!Program.subscribers.ContainsKey(nickname)) Program.subscribers.Add(nickname, new List<string>());

            if (forumUrl.Equals("*"))
            {
                foreach(var forum in Program.subscribers[nickname])
                {
                    Program.RemoveServer(nickname, forum);
                }
                Program.subscribers[nickname].Clear();
                await ReplyAsync(string.Format("{0} now is no more watching any forum\n", Context.User.Username));
                return;
            }


            if (Program.subscribers[nickname].Contains(forumUrl))
            {
                Program.subscribers[nickname].Remove(forumUrl);
                Program.RemoveServer(nickname, forumUrl);
                await ReplyAsync(string.Format("{0} is no more watching for {1}\n", Context.User.Username, forumUrl));
                return;
            }
            await ReplyAsync(string.Format("{0} is not watching yet for {1}\n", Context.User.Username, forumUrl));
        }

        [Command("watch")]
        public async Task Watch(string forumUrl)
        {
            string nickname = Context.User.Username + "#" + Context.User.Discriminator;

            // Ensure nickname exists
            if (!Program.subscribers.ContainsKey(nickname)) Program.subscribers.Add(nickname, new List<string>());

            if (Program.subscribers[nickname].Contains(forumUrl))
            {
                await ReplyAsync(string.Format("{0} already assigned to {1}\n", forumUrl, Context.User.Username));
                return;
            }
            Program.subscribers[nickname].Add(forumUrl);
            Program.AddServer(nickname, forumUrl);
            await ReplyAsync(string.Format("{0} now is watching for {1}\n", Context.User.Username, forumUrl));
        }

        [Command("status")]
        public async Task Status()
        {
            string nickname = Context.User.Username + "#" + Context.User.Discriminator;

            string DISCORD_FORMAT_STR = "{0,-20}\n{1,-16}\n{2,-16}\n";
            string CONSOLE_FORMAT_STR = "{0,-20}{1,-16}{2,-16}{3,-12}\n";

            string title = "WowCircle Forum Status";
            var fields = new List<EmbedFieldBuilder>();
            
            if(!Program.subscribers.ContainsKey(nickname))
            {
                await ReplyAsync(string.Format("No data available for user {0}\n", Context.User.Username));
                return;
            }

            StringBuilder sb = new StringBuilder();
            foreach (var forumUrl in Program.subscribers[nickname])
            {
                var wowCircleForum = Program.snapshot[forumUrl];
                sb.Append(wowCircleForum.Url + Environment.NewLine);
                foreach (var thread in wowCircleForum.threads)
                {
                    string discordText = string.Format("{0,-20}{1,-32}{2,-14}",//DISCORD_FORMAT_STR,
                        thread.Author.NickName,
                        thread.Url,
                        thread.CreatedAt
                    );
                    
                    fields.Add(new EmbedFieldBuilder() { IsInline = false, Name = thread.Title, Value = discordText });
                }
            }

            /*
            sb.Append(string.Format(CONSOLE_FORMAT_STR, "ServerName", "Online", "Uptime", "Status"));
            foreach (var item in Program.cache)
            {
                string consoleText = string.Format(CONSOLE_FORMAT_STR,
                    item.Value.Name,
                    item.Value.Online,
                    item.Value.Uptime,
                    item.Value.Status.ToString());

                string discordText = string.Format(DISCORD_FORMAT_STR,
                    item.Value.Online,
                    item.Value.Uptime,
                    item.Value.Status.ToString());

                sb.Append(consoleText);

                fields.Add(new EmbedFieldBuilder() { IsInline = true, Name = item.Value.Name, Value = discordText });
            }*/
            //Program.dumpForum(Program.ROOT_FORUM);


            var embed = new EmbedBuilder()
            {
                Title = title,
                Fields = fields
            };


            //Console.WriteLine(sb.ToString());
            await ReplyAsync("", false, embed.Build());
        }

    }
}
