using System;
using System.Collections.Generic;
using DT_WordPdfLink.Models;

namespace DT_WordPdfLink.Storage
{
    public class WordLinkRegistry
    {
        public int Version { get; set; }
        public List<WordLinkRecord> Links { get; set; }

        public WordLinkRegistry()
        {
            Version = 1;
            Links = new List<WordLinkRecord>();
        }
    }
}
