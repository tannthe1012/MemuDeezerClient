namespace BoosterClient.Models
{
    public class ProfileSession
    {
        public string session_id { get; set; } = "";

        public int profile_id { get; set; }

        public long expires_time { get; set; }
    }
}
