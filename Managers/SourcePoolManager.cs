using BoosterClient.Exceptions;
using BoosterClient.Models;
using MemuDeezerClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BoosterClient.Managers
{
    public class SourcePoolManager
    {
        private readonly APIClient client;

        private readonly string AlbumFile = Path.Combine(Build.DATA_DIR, "album.txt");

        private readonly string ArtistFile = Path.Combine(Build.DATA_DIR, "artist.txt");

        private readonly List<string> AlbumList;

        private readonly List<string> ArtistList;

        public SourcePoolManager(APIClient client)
        {
            this.client = client;
            if (Build.IS_LITE)
            {
                if (!File.Exists(AlbumFile))
                {
                    File.Create(AlbumFile).Close();
                }
                if (!File.Exists(ArtistFile))
                {
                    File.Create(ArtistFile).Close();
                }
                

                AlbumList = new List<string>(File.ReadAllLines(AlbumFile));
                ArtistList = new List<string>(File.ReadAllLines(ArtistFile));
            }
        }

        public Task<SourceURL> PickAsync()
        {
            //if (Build.IS_LITE)
            //{
            //    Config config = Config.Instance;
            //    Random rnd = new Random();
                
            //    int seed = rnd.Next(100);

            //    if (seed < 70 && AlbumList.Any())
            //    {
            //        int index = rnd.Next(AlbumList.Count);
            //        //string data = AlbumList[index].Split('/').Last();
            //        string data = AlbumList[index];
            //        SourceURL sourceURL = new SourceURL();
            //        sourceURL.source_id = data;
            //        sourceURL.url = data;
            //        sourceURL.type = SourceUrlType.ALBUM;
            //        //return new SourceURL
            //        //{
            //        //    //AlbumID = new string[] { data },
            //        //    source_id = ""
            //        //    url = "data"
            //        //    type = SourceUrlType.ALBUM
            //        //};
            //        return ;
            //    }
            //    //else if (ArtistList.Any())
            //    //{
            //    //    int index = rnd.Next(ArtistList.Count);
            //    //    string data = ArtistList[index].Split('/').Last();
            //    //    return new Source
            //    //    {
            //    //        ArtistID = data
            //    //    };
            //    //}
            //    //else if (AlbumList.Any())
            //    //{
            //    //    int index = rnd.Next(AlbumList.Count);
            //    //    string data = AlbumList[index].Split('/').Last();
            //    //    return new Source
            //    //    {
            //    //        AlbumID = new string[] { data }
            //    //    };
            //    //}
            //    //else
            //    //{
            //    //    return null;
            //    //}
            //}
            return client.Source.GET_PoolPick() ?? throw new SourcePoolOverException();
        }

        public SourceURL PickSourceLite()
        {
            Random rnd = new Random();

            int seed = rnd.Next(100);

            if (seed < 70 && AlbumList.Any())
            {
                int index = rnd.Next(AlbumList.Count);
                string data = AlbumList[index];
                SourceURL sourceURL = new SourceURL();
                sourceURL.source_id = data;
                sourceURL.url = data;
                sourceURL.type = SourceUrlType.ALBUM;
                return sourceURL;
            }
            else if (ArtistList.Any())
            {
                int index = rnd.Next(ArtistList.Count);
                string data = ArtistList[index];
                SourceURL sourceURL = new SourceURL();
                sourceURL.source_id = data;
                sourceURL.url = data;
                sourceURL.type = SourceUrlType.ARTIST;
                return sourceURL;
            }
            else if (AlbumList.Any())
            {
                int index = rnd.Next(AlbumList.Count);
                string data = AlbumList[index];
                SourceURL sourceURL = new SourceURL();
                sourceURL.source_id = data;
                sourceURL.url = data;
                sourceURL.type = SourceUrlType.ALBUM;
                return sourceURL;
            }
            else
            {
                return null;
            }
        }

        public Task<int> CountAsync()
        {
            return client.Source.GET_PoolCount();
        }
    }
}
