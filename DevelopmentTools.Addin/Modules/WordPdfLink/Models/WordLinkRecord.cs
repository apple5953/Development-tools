using System;

namespace DT_WordPdfLink.Models
{
    public class WordLinkRecord
    {
        public string SourceWordPath { get; set; }
        public string CachePdfPath { get; set; }
        public string ImageTypeUniqueId { get; set; }
        public int PageNumber { get; set; }
        public int Dpi { get; set; }
        public string Orientation { get; set; }
        public double PageWidthMm { get; set; }
        public double PageHeightMm { get; set; }
        public string LastWordWriteUtc { get; set; }
        public string LastSyncUtc { get; set; }
        public long WordFileLength { get; set; }
        public string SheetUniqueId { get; set; }
        public string LastSheetName { get; set; }
        public string LastSheetNumber { get; set; }
        public string LastSheetRevision { get; set; }
        public string LastSheetDate { get; set; }
    }
}
