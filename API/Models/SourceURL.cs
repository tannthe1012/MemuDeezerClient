namespace BoosterClient.Models
{
    public class SourceURL
    {
        public string source_id { get; set; } = "";

        public string url { get; set; }

        public SourceUrlType type { get; set; }
    }

    public enum SourceUrlType
    {
        ALBUM = 0,
        ARTIST = 1
    }
}
