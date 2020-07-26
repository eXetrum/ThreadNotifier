using System;
using System.Collections.Generic;

namespace ThreadNotifier
{
    public class WowCircleForum
    {
        public string Title { get; set; }
        public string Url { get; set; }

        public List<ForumThread> threads { get; set; }

        public WowCircleForum()
        {
            threads = new List<ForumThread>();
        }
    }
}