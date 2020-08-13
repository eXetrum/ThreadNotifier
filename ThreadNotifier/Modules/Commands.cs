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
            string[] commands = { 
                "help - показать это сообщение", 
                "list - показать список подписок(форумов)", 
                "watch_forum <link> - добавить форум в список слежения", 
                "unwatch_forum <link> - убрать форум из списка слежения" 
            };

            StringBuilder sb = new StringBuilder();
            foreach (var item in commands) sb.Append(item + Environment.NewLine);

            await ReplyAsync(sb.ToString());
        }

        [Command("list")]
        public async Task ListServers()
        {
            string nickname = string.Format("{0}#{1}", Context.User.Username, Context.User.Discriminator);

            var userList = await Program.GetSubscriptionsAsync();

            if (userList.Count == 0) 
            {
                await ReplyAsync("Список слежения пуст");
                return;
            }

            StringBuilder sb = new StringBuilder();
            foreach (var forumUrl in userList)
                sb.Append(string.Format("{0}{1}", forumUrl, Environment.NewLine));

            await ReplyAsync(sb.ToString());
        }

        [Command("unwatch_forum"), RequireOwner()]
        public async Task UnWatch(string forumUrl)
        {
            string nickname = string.Format("{0}#{1}", Context.User.Username, Context.User.Discriminator);

            if (forumUrl.Equals("*"))
            {
                await Program.RemoveAllAsync();
                await ReplyAsync(string.Format("{0} более не следит ни за одни форумом{1}", Context.User.Username, Environment.NewLine));
                return;
            }

            var exists = await Program.IsSubscriptionExistsAsync(forumUrl);
            if (!exists)
            {
                await ReplyAsync(string.Format("{0} еще не следит за {1}{2}", Context.User.Username, forumUrl, Environment.NewLine));
                return;
            }

            await Program.RemoveServerAsync(forumUrl);            
            await ReplyAsync(string.Format("{0} более не следит за {1}{2}", Context.User.Username, forumUrl, Environment.NewLine));            
        }

        [Command("watch_forum"), RequireOwner()]
        public async Task Watch(string forumUrl)
        {
            string nickname = string.Format("{0}#{1}", Context.User.Username, Context.User.Discriminator);

            var exists = await Program.IsSubscriptionExistsAsync(forumUrl);
            if (exists)
            {
                await ReplyAsync(string.Format("{0} уже следит за {1}{2}", Context.User.Username, forumUrl, Environment.NewLine));
                return;
            }

            await Program.AddServer(forumUrl);
            await ReplyAsync(string.Format("{0} теперь следит за {1}{2}", Context.User.Username, forumUrl, Environment.NewLine));
        }

    }
}
