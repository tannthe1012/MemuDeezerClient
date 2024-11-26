namespace BoosterClient.Models
{
    public class Source : SourceData
    {
        public string source_id { get; set; } = "";

        public int index { get; set; }
    }

    public class SourceData
    {
        public string artist_url { get; set; }

        public string album_urls { get; set; }

        public string info { get; set; }

        public string network { get; set; }

        public int limit { get; set; }
    }
}
