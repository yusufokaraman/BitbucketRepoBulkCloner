using BitbucketRepoBulkCloner.Models;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;

namespace BitbucketRepoBulkCloner.Managers
{
    /// <summary>
    /// Bitbucket API işlemlerini yöneten yardımcı sınıf
    /// </summary>
    public class BitbucketManager
    {
        private readonly string _bitbucketApiBaseUrl;
        private readonly string _bitbucketUsername;
        private readonly string _bitbucketAppPassword;

        public BitbucketManager(string bitbucketApiBaseUrl, string bitbucketUsername, string bitbucketAppPassword)
        {
            _bitbucketApiBaseUrl = bitbucketApiBaseUrl;
            _bitbucketUsername = bitbucketUsername;
            _bitbucketAppPassword = bitbucketAppPassword;
        }

        /// <summary>
        /// Belirtilen workspace ve projectKey'e ait tüm repository'leri getirir.
        /// Pagelen ile sayfalama yapılır, obj.next varsa döngüde devam eder.
        /// </summary>
        public async Task<List<BitbucketRepo>> GetRepositoriesByProjectKeyAsync(string workspace, string projectKey)
        {
            var result = new List<BitbucketRepo>();

            // ?q=project.key="XXX" filtrelemesi
            string url = $"{_bitbucketApiBaseUrl}/repositories/{workspace}?q=project.key=\"{projectKey}\"&pagelen=100";

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = GetAuthHeader();

                string? nextUrl = url;
                while (!string.IsNullOrEmpty(nextUrl))
                {
                    var response = await client.GetAsync(nextUrl);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Bitbucket API hata döndürdü. Kod: {response.StatusCode}");
                        break;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    dynamic? obj = JsonConvert.DeserializeObject(json);
                    if (obj == null) break;

                    if (obj.values != null)
                    {
                        foreach (var item in obj.values)
                        {
                            var repoName = (string)item.name;
                            var slug = (string)item.slug;

                            string? cloneUrl = null;
                            if (item.links != null && item.links.clone != null)
                            {
                                foreach (var cloneInfo in item.links.clone)
                                {
                                    string href = cloneInfo.href;
                                    string cloneName = cloneInfo.name;
                                    if (cloneName == "https")
                                    {
                                        cloneUrl = href;
                                        break;
                                    }
                                }
                            }

                            result.Add(new BitbucketRepo
                            {
                                Name = repoName,
                                Slug = slug,
                                CloneUrl = cloneUrl
                            });
                        }
                    }

                    // Sayfalama
                    nextUrl = obj.next != null ? (string)obj.next : null;
                }
            }

            return result;
        }

        /// <summary>
        /// Basit Basic Auth Header
        /// </summary>
        private AuthenticationHeaderValue GetAuthHeader()
        {
            var credentials = $"{_bitbucketUsername}:{_bitbucketAppPassword}";
            return new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials))
            );
        }
    }
}
