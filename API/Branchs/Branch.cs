using System;

namespace BoosterClient.Branchs
{
    public abstract class Branch
    {
        public APIClient Client { get; private set; }

        public Branch(APIClient client)
        {
            Client = client ?? throw new ArgumentNullException("client");
        }
    }
}
