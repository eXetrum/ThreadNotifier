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
            string[] commands = { "help - показать это сообщение", "list - показать список подписок(форумов)", "watch <link> - добавить форум", "unwatch <link> - убрать форум" };

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

    }
}
