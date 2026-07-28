using EnableBanking.Models;
using Newtonsoft.Json;
using System.Text;

namespace EnableBanking.Services
{
    public class HttpClientService
    {
        /// <summary>
        /// Nulls are omitted rather than sent. Several request models reuse the fat response
        /// metadata classes (StartAuthorizationRequest.Aspsp is the ASPSP listing type), so the
        /// defaults would post fields like auth_methods and required_psu_headers as explicit nulls
        /// into endpoints that never document them.
        /// </summary>
        private static readonly JsonSerializerSettings SerializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly HttpClient _httpClient;

        public HttpClientService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        protected async Task<ApiResponse<T>> GetAsync<T>(string requestUri, CancellationToken cancellationToken)
        {
            var responseMessage = await _httpClient.GetAsync(requestUri, cancellationToken);
            return await HandleResponse<T>(responseMessage, cancellationToken);
        }

        protected async Task<ApiResponse<T>> PostAsync<T>(string requestUri, object requestBody, CancellationToken cancellationToken)
        {
            var jsonContent = JsonConvert.SerializeObject(requestBody, SerializerSettings);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var responseMessage = await _httpClient.PostAsync(requestUri, httpContent, cancellationToken);
            return await HandleResponse<T>(responseMessage, cancellationToken);
        }

        protected async Task<ApiResponse<T>> DeleteAsync<T>(string requestUri, CancellationToken cancellationToken)
        {
            var responseMessage = await _httpClient.DeleteAsync(requestUri, cancellationToken);
            return await HandleResponse<T>(responseMessage, cancellationToken);
        }

        private async Task<ApiResponse<T>> HandleResponse<T>(HttpResponseMessage responseMessage, CancellationToken cancellationToken)
        {
            var response = new ApiResponse<T>();

            var content = await responseMessage.Content.ReadAsStringAsync(cancellationToken);
            response.StatusCode = responseMessage.StatusCode;

            if (responseMessage.IsSuccessStatusCode)
            {
                response.Data = JsonConvert.DeserializeObject<T>(content);
                return response;
            }

            response.Error = JsonConvert.DeserializeObject<ApiError>(content);
            return response;
        }
    }
}
