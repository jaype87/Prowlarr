using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.Gazelle
{
    public class GazelleRequestGenerator : IIndexerRequestGenerator
    {
        public GazelleSettings Settings { get; set; }
        public string BaseUrl { get; set; }

        public IDictionary<string, string> AuthCookieCache { get; set; }
        public IHttpClient HttpClient { get; set; }
        public IndexerCapabilities Capabilities { get; set; }
        public Logger Logger { get; set; }

        protected virtual string LoginUrl => BaseUrl + "login.php";
        protected virtual string APIUrl => BaseUrl + "ajax.php";
        protected virtual string DownloadUrl => BaseUrl + "torrents.php?action=download&usetoken=" + (Settings.UseFreeleechToken ? "1" : "0") + "&id=";
        protected virtual string DetailsUrl => BaseUrl + "torrents.php?torrentid=";
        protected virtual bool ImdbInTags => false;

        public Func<IDictionary<string, string>> GetCookies { get; set; }
        public Action<IDictionary<string, string>, DateTime?> CookiesUpdater { get; set; }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(GetRequest(null));

            return pageableRequests;
        }

        private IEnumerable<IndexerRequest> GetRequest(string searchParameters)
        {
            AuthCookieCache = GetCookies();

            Authenticate();

            var filter = "";
            if (searchParameters == null)
            {
            }

            var request =
                new IndexerRequest(
                    $"{APIUrl}?action=browse{searchParameters}{filter}",
                    HttpAccept.Json);

            var cookies = AuthCookieCache;
            foreach (var cookie in cookies)
            {
                request.HttpRequest.Cookies[cookie.Key] = cookie.Value;
            }

            yield return request;
        }

        private GazelleAuthResponse GetIndex(IDictionary<string, string> cookies)
        {
            var indexRequestBuilder = new HttpRequestBuilder($"{APIUrl}?action=index")
            {
                LogResponseContent = true
            };

            indexRequestBuilder.SetCookies(cookies);
            indexRequestBuilder.Method = HttpMethod.POST;

            var authIndexRequest = indexRequestBuilder
                .Accept(HttpAccept.Json)
                .Build();

            var indexResponse = HttpClient.Execute(authIndexRequest);

            var result = Json.Deserialize<GazelleAuthResponse>(indexResponse.Content);

            return result;
        }

        private void Authenticate()
        {
            var requestBuilder = new HttpRequestBuilder(LoginUrl)
            {
                LogResponseContent = true
            };

            requestBuilder.Method = HttpMethod.POST;
            requestBuilder.PostProcess += r => r.RequestTimeout = TimeSpan.FromSeconds(15);

            var cookies = AuthCookieCache;

            if (cookies == null)
            {
                AuthCookieCache = null;
                var authLoginRequest = requestBuilder
                    .AddFormParameter("username", Settings.Username)
                    .AddFormParameter("password", Settings.Password)
                    .AddFormParameter("keeplogged", "1")
                    .SetHeader("Content-Type", "multipart/form-data")
                    .Accept(HttpAccept.Json)
                    .Build();

                var response = HttpClient.Execute(authLoginRequest);

                cookies = response.GetCookies();

                AuthCookieCache = cookies;
                CookiesUpdater(cookies, DateTime.Now + TimeSpan.FromDays(30));
            }

            var index = GetIndex(cookies);

            if (index == null || index.Status.IsNullOrWhiteSpace() || index.Status != "success")
            {
                Logger.Debug("Gazelle authentication failed.");
                AuthCookieCache = null;
                CookiesUpdater(null, null);
                throw new Exception("Failed to authenticate with Gazelle.");
            }

            Logger.Debug("Gazelle authentication succeeded.");

            Settings.AuthKey = index.Response.Authkey;
            Settings.PassKey = index.Response.Passkey;
        }

        private string GetBasicSearchParameters(string searchTerm, int[] categories)
        {
            var searchString = GetSearchTerm(searchTerm);

            var parameters = "&action=browse&order_by=time&order_way=desc";

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                parameters += string.Format("&searchstr={0}", searchString);
            }

            if (categories != null)
            {
                foreach (var cat in Capabilities.Categories.MapTorznabCapsToTrackers(categories))
                {
                    parameters += string.Format("&filter_cat[{0}]=1", cat);
                }
            }

            return parameters;
        }

        public IndexerPageableRequestChain GetSearchRequests(MovieSearchCriteria searchCriteria)
        {
            var parameters = GetBasicSearchParameters(searchCriteria.SearchTerm, searchCriteria.Categories);

            if (searchCriteria.ImdbId != null)
            {
                if (ImdbInTags)
                {
                    parameters += string.Format("&taglist={0}", searchCriteria.ImdbId);
                }
                else
                {
                    parameters += string.Format("&cataloguenumber={0}", searchCriteria.ImdbId);
                }
            }

            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetRequest(parameters));
            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(MusicSearchCriteria searchCriteria)
        {
            var parameters = GetBasicSearchParameters(searchCriteria.SearchTerm, searchCriteria.Categories);

            if (searchCriteria.Artist != null)
            {
                parameters += string.Format("&artistname={0}", searchCriteria.Artist);
            }

            if (searchCriteria.Label != null)
            {
                parameters += string.Format("&recordlabel={0}", searchCriteria.Label);
            }

            if (searchCriteria.Album != null)
            {
                parameters += string.Format("&groupname={0}", searchCriteria.Album);
            }

            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetRequest(parameters));
            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(TvSearchCriteria searchCriteria)
        {
            var parameters = GetBasicSearchParameters(searchCriteria.SearchTerm, searchCriteria.Categories);

            if (searchCriteria.ImdbId != null)
            {
                if (ImdbInTags)
                {
                    parameters += string.Format("&taglist={0}", searchCriteria.ImdbId);
                }
                else
                {
                    parameters += string.Format("&cataloguenumber={0}", searchCriteria.ImdbId);
                }
            }

            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetRequest(parameters));
            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            throw new NotImplementedException();
        }

        // hook to adjust the search term
        protected virtual string GetSearchTerm(string term) => term;

        public IndexerPageableRequestChain GetSearchRequests(BasicSearchCriteria searchCriteria)
        {
            var parameters = GetBasicSearchParameters(searchCriteria.SearchTerm, searchCriteria.Categories);

            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetRequest(parameters));
            return pageableRequests;
        }
    }
}
