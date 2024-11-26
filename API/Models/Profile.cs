using System;

namespace BoosterClient.Models
{
    public class Profile
    {
        public int profile_id { get; set; }

        public int? proxy_id { get; set; }

        public string name { get; set; } = "";

        public string country { get; set; } = null;

        public DateTime? subs_start_date { get; set; } = null;

        public DateTime? subs_end_date { get; set; } = null;

        public ProfileType type { get; set; }

        public ProfileStatus status { get; set; }
    }

    public enum ProfileType
    {
        //OWN = 0,
        //SHARE = 1,
        //TRIAL = 2
        PRIVATE = 0,
        PREMIUM = 1,
        PREMBRAZIL = 2,
        FREE = 3
    }

    public enum ProfileStatus
    {
        GOOD = 0,
        BAD = 1
    }
}
