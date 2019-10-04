using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using IE.Sby.OAuth2.Service.Types;
using IE.Sby.Common.Service.Types;
using IE.Sby.Web.Config.PSD2;
using IE.Web.Framework.Services;
using IE.Web.Portal.Helpers;
using IE.Web.Portal.Services.Contracts;
using log4net;
using Newtonsoft.Json;

namespace IE.Web.Portal.Services
{
    public class OauthService : IOauthService
    {
        private const string ServiceName = "oauth2";
        private const string ProfileIdTag = "arn::sb::FIS:ProfileID:";
        private Psd2Section psd2Section;
        protected readonly IHttpService HttpService;
        protected readonly ILog Logger;

        public OauthService(IHttpService httpService)
        {
            HttpService = httpService;
            psd2Section = (Psd2Section)ConfigurationManager.GetSection("sby.psd2");
            Logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        }

        public async Task<GetAccessTokenResponse> GetAccessToken(GetAccessTokenRequest request)
        {
            using (var client = new HttpClient(SetHttpClientHandler()))
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                client.BaseAddress = new Uri(psd2Section.SignOnSettings.IamServiceBaseUri);
                var parameters = string.Format("grant_type=authorization_code&code={0}&redirect_uri={1}",
                    HttpUtility.UrlEncode(request.AuthenticationCode),
                    HttpUtility.UrlEncode(psd2Section.SignOnSettings.RedirectUri));

                var authorizationHeader = EncodingHelper.Base64Encode(string.Format("{0}:{1}",
                    psd2Section.SignOnSettings.ClientId, psd2Section.SignOnSettings.ClientSecret));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authorizationHeader);

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, psd2Section.SignOnSettings.IamServiceSuffixUri)
                {
                    Content = new StringContent(parameters, Encoding.UTF8, "application/x-www-form-urlencoded")
                };
                
                HttpResponseMessage httpResponse = null;

                await client.SendAsync(httpRequest)
                  .ContinueWith(responseTask =>
                  {
                      httpResponse = responseTask.Result;
                  });

                Logger.Info(string.Format("IAM Access Token retrieval Web Service call. URL: {0}. Authorization Header: {1}. Parameters: {2}. Status Code: {3}.",
                    psd2Section.SignOnSettings.IamServiceBaseUri + psd2Section.SignOnSettings.IamServiceSuffixUri,
                    authorizationHeader,
                    parameters,
                    (int)httpResponse.StatusCode
                    ));

                if (httpResponse.IsSuccessStatusCode)
                {
                    var result = httpResponse.Content.ReadAsStringAsync().Result;

                    Logger.Info(string.Format("Original IAM Access Token retrieval Web Service call response. Authentiacation Code: {0}. " + 
                        "{1}Response: {2}", request.AuthenticationCode, Environment.NewLine, result));

                    var response = JsonConvert.DeserializeObject<IAMResponse>(result);

                    var accessTokens = response.AccessToken.Split('.');
                    var idTokens = response.IdToken.Split('.');
                    var accessTokenPart1 = EncodingHelper.Base64Decode(EncodingHelper.NormalizeBase64String(accessTokens[0]));
                    var accessTokenPart2 = EncodingHelper.Base64Decode(EncodingHelper.NormalizeBase64String(accessTokens[1]));
                    var idToken1 = EncodingHelper.Base64Decode(EncodingHelper.NormalizeBase64String(idTokens[0]));
                    var idToken2 = EncodingHelper.Base64Decode(EncodingHelper.NormalizeBase64String(idTokens[1]));

                    Logger.Info(string.Format("IAM Access Token retrieval Web Service call response encoded values. Authentiacation Code: {0}. " +
                                              "{1}Access Token: {2}. {3}ID Token: {4}. {5}Token Type: {6}. {7}Scope: {8}. {9}Expires in: {10}.",
                        request.AuthenticationCode,
                        Environment.NewLine, response.AccessToken,
                        Environment.NewLine, response.IdToken,
                        Environment.NewLine, response.TokenType,
                        Environment.NewLine, response.Scope,
                        Environment.NewLine, response.ExpiresIn));

                    Logger.Info(string.Format("IAM Access Token retrieval Web Service call response decoded values. Authentiacation Code: {0}. " +
                                              "{1}Access Token part 1: {2}. {3}Access Token part 2: {4}. {5}ID Token part 1: {6}. {7}ID Token part 2: {8}.",
                        request.AuthenticationCode,
                        Environment.NewLine, accessTokenPart1,
                        Environment.NewLine, accessTokenPart2,
                        Environment.NewLine, idToken1,
                        Environment.NewLine, idToken2));

                    var profileId = string.Empty;
                    int inetUserAccess = 0;

                    try
                    {
                        var token2Response = JsonConvert.DeserializeObject<Token>(idToken2);
                        profileId = new List<string>(token2Response.ProductAccountId).
                            FirstOrDefault(x => x.StartsWith(ProfileIdTag)).
                            Replace(ProfileIdTag, string.Empty);
                        inetUserAccess = token2Response.InetUserAccess;
                    }
                    catch (Exception ex)
                    {
                        var token2Response = JsonConvert.DeserializeObject<WorkaroundToken>(idToken2);
                        profileId = token2Response.ProductAccountId.Replace(ProfileIdTag, string.Empty);
                        inetUserAccess = token2Response.InetUserAccess;
                    }

                    return Map(true, profileId, inetUserAccess);
                }

                return Map(false);
            }
        }

        private HttpClientHandler SetHttpClientHandler()
        {
            var handler = new HttpClientHandler();

            if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["IamProxyUri"]))
            {
                var proxyUri = ConfigurationManager.AppSettings["IamProxyUri"];
                IWebProxy proxy = new WebProxy(new Uri(proxyUri));

                handler.UseCookies = true;
                handler.AllowAutoRedirect = true;
                handler.Proxy = proxy;
            }

            return handler;
        }

        private static GetAccessTokenResponse Map(bool success, string profileId = null, int? inetUserAccess = null)
        {
            return new GetAccessTokenResponse
            {
                ProfileId = profileId,
                InetUserAccess = (UserAccess)inetUserAccess,
                Status = success ? ResponseStatusTypes.Success : ResponseStatusTypes.Failed
            };
        }
    }
}