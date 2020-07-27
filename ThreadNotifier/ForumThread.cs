using System;
using System.Collections.Generic;
using System.Text;

namespace ThreadNotifier
{
    public class ForumThread
    {
        public string Title { get; set; }
        public ForumUser Author { get; set; }
        public string Url { get; set; }
        public string ID { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsNew { get; set; }
    }
}
