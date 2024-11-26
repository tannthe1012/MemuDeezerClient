using BoosterClient.API.Exceptions;
using BoosterClient.Branchs;
using BoosterClient.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Data.SqlClient;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MemuDeezerClient;

namespace BoosterClient
{
    public class APIClient : IDisposable
    {
        private readonly HttpClient http;

        public string BearerToken { get; private set; }

        public ClientBranch Client { get; private set; }

        public ProfileBranch Profile { get; private set; }

        public ProxyBranch Proxy { get; private set; }

        public SettingBranch Setting { get; private set; }

        public SourceBranch Source { get; private set; }

        public APIClient()
        {
            http = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = true
            }, true)
            {
                //BaseAddress = new Uri("http://127.0.0.1:3030"),
                //Timeout = TimeSpan.FromSeconds(20)
                BaseAddress = new Uri("http://103.156.91.125:3030"),
                Timeout = TimeSpan.FromSeconds(20)
            };

            http.DefaultRequestHeaders
                .TryAddWithoutValidation("User-Agent", $"{Build.ASSEMBLY_NAME}/{Build.ASSEMBLY_MAJOR_VERSION}.{Build.ASSEMBLY_MINOR_VERSION}");

            Client = new ClientBranch(this);
            Profile = new ProfileBranch(this);
            Proxy = new ProxyBranch(this);
            Setting = new SettingBranch(this);
            Source = new SourceBranch(this);
        }

        public async Task AuthorizeAsync(int client_id, string secret)
        {
            HttpRequestMessage req = null;
            HttpResponseMessage res = null;

            try
            {
                req = new HttpRequestMessage(HttpMethod.Post, "api/client/authorize")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(new
                    {
                        client_id,
                        secret
                    }), Encoding.UTF8, "application/json")
                };
                res = await http.SendAsync(req);

                var text = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                {
                    BearerToken = JsonConvert.DeserializeObject<string>(text);
                }
                else
                {
                    if (res.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new UnauthorizedException();
                    }

                    JObject jobj = null;
                    try
                    {
                        jobj = JObject.Parse(text);
                    }
                    catch { }

                    if (jobj != null)
                    {
                        var title = jobj["title"]?.Value<string>();
                        var detail = jobj["detail"]?.Value<string>();
                        var status = jobj["status"]?.Value<int>() ?? 0;
                        throw new APIException(title, detail, status);
                    }
                    throw new APIException(text);
                }
            }
            finally
            {
                req?.Dispose();
                res?.Dispose();
            }
        }

        public async Task<byte[]> RequestAsync(HttpMethod method, string path, HttpContent body)
        {
            HttpRequestMessage req = null;
            HttpResponseMessage res = null;

            try
            {
                req = new HttpRequestMessage(method, path);

                if (body != null)
                {
                    req.Content = body;
                }

                if (BearerToken != null)
                {
                    req.Headers.Add("Authorization", "Bearer " + BearerToken);
                }

                res = await http.SendAsync(req);

                if (res.IsSuccessStatusCode)
                {
                    return await res.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    if (res.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        if (BearerToken != null)
                        {
                            BearerToken = null;
                        }
                        throw new UnauthorizedException();
                    }

                    var text = await res.Content.ReadAsStringAsync();

                    JObject jobj = null;
                    try
                    {
                        jobj = JObject.Parse(text);
                    }
                    catch { }

                    if (jobj != null)
                    {
                        var title = jobj["title"]?.Value<string>();
                        var detail = jobj["detail"]?.Value<string>();
                        var status = jobj["status"]?.Value<int>() ?? 0;
                        throw new APIException(title, detail, status);
                    }
                    throw new APIException(text);
                }
            }
            catch (HttpRequestException ex)
            {
                throw ex;
            }
            finally
            {
                req?.Dispose();
                res?.Dispose();
            }
        }

        public Task<byte[]> RequestAsync(HttpMethod method, string path)
        {
            return RequestAsync(method, path, null);
        }

        public Task<byte[]> RequestAsync(HttpMethod method, string path, object body)
        {
            var json = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            return RequestAsync(method, path, content);
        }

        public async Task<T> RequestAsync<T>(HttpMethod method, string path)
        {
            var bytes = await RequestAsync(method, path, null);
            var json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject<T>(json);
        }

        public async Task<T> RequestAsync<T>(HttpMethod method, string path, object body)
        {
            var json = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var bytes = await RequestAsync(method, path, content);
            json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject<T>(json);
        }

        public async Task<Stream> DownloadAsync(HttpMethod method, string path)
        {
            var bytes = await RequestAsync(method, path);
            return new MemoryStream(bytes);
        }

        public Task UploadAsync(HttpMethod method, string path, Stream stream)
        {
            var content = new StreamContent(stream);
            return RequestAsync(method, path, content);
        }

        public void Dispose()
        {
            http.Dispose();
        }
    }
}
