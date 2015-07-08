using System;
using System.Net.Http;
using System.Web;
using MetricsExtractor.Core.Integration.Abstract;

namespace MetricsExtractor.Core.Integration.Concrete
{
    public class SlackIntegration : ISlackIntegration
    {
        private string token;
        const string urlPostMessage = "https://slack.com/api/chat.postMessage";

        public SlackIntegration(string tokenParam)
        {
            token = tokenParam;
        }


        public string PostMessage(string channel, string message, string userName)
        {
            var c = new HttpClient();
            var url = string.Format("{0}?token={1}&channel={2}&text={3}&username={4}&pretty=1",
                urlPostMessage, HttpUtility.UrlEncode(token), HttpUtility.UrlEncode(channel),
                HttpUtility.UrlEncode(message), HttpUtility.UrlEncode(userName));
            var uri = new Uri(url);


            c.BaseAddress = uri;
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = c.SendAsync(req).Result;
            var result = response;
            var responseMessage = response.Content.ReadAsStringAsync().Result;
            if (!result.IsSuccessStatusCode)
                throw new HttpRequestException(string.Format("Invalid Status: {0}. Details: {1}", result.StatusCode, responseMessage));
            return responseMessage;
        }
    }
}
