using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SallaWinFormsDemo
{
    public class SallaApi
    {
        private const string TokenUrl = "https://accounts.salla.sa/callback/1126924571";
        private const string BaseUrl = "https://api.salla.dev/admin/v2";//toBe updated in production environment with real cust url

        private readonly HttpClient _http;

        public SallaApi()
        {
            _http = new HttpClient();
        }

        /// <summary>
        /// احصل على Access Token من الـ refresh_token
        /// </summary>
        public async Task<string> RefreshTokenAsync(string clientId, string clientSecret, string refreshToken)
        {
            var form = new FormUrlEncodedContent(new[]
            {
               // new KeyValuePair<string,string>("grant_type","refresh_token"),
                new KeyValuePair<string,string>("client_id",clientId),
                new KeyValuePair<string,string>("client_secret",clientSecret),
                new KeyValuePair<string,string>("refresh_token",refreshToken)
            });

            var res = await _http.PostAsync(TokenUrl, form);
            res.EnsureSuccessStatusCode();

            var json = await res.Content.ReadAsStringAsync();
            var obj = JObject.Parse(json);
            return (string)obj["access_token"];
        }

        private void SetBearer(string accessToken)
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        }

        /// <summary>
        /// إرجاع صفحة من الطلبات (Orders). استخدم page و created_from (UTC) لو عايز تقرأ Incremental.
        /// </summary>
        public async Task<JObject> GetOrdersPageAsync(string accessToken, int page, DateTime? createdFromUtc)
        {
            SetBearer(accessToken);

            var url = BaseUrl + "/orders?page=" + page;
            if (createdFromUtc.HasValue)
            {
                // ISO Zulu
                var iso = createdFromUtc.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
                url += "&created_from=" + Uri.EscapeDataString(iso);
            }

            var res = await _http.GetAsync(url);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }

        /// <summary>
        /// عناصر الطلب الواحد
        /// </summary>
        public async Task<JObject> GetOrderItemsAsync(string accessToken, long orderId)
        {
            SetBearer(accessToken);

            var url = BaseUrl + "/order-items?order_id=" + orderId;
            var res = await _http.GetAsync(url);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }
    }
}
