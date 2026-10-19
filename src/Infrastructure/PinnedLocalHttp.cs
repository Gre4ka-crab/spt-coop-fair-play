using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PrivateStack.Infrastructure
{
    public static class PinnedLocalHttp
    {
        public static string Request(string url, string method, string certificatePath, string bearer, string body)
        {
            var uri = new Uri(url);
            if (!uri.IsLoopback || uri.Scheme != "https")
                throw new InvalidOperationException("Only HTTPS loopback is permitted for this admin client");
            using (var certificate = X509Certificate2.CreateFromPem(System.IO.File.ReadAllText(certificatePath)))
            using (var handler = new HttpClientHandler())
            {
                var expected = certificate.GetCertHash(HashAlgorithmName.SHA256);
                handler.AllowAutoRedirect = false;
                handler.ServerCertificateCustomValidationCallback = (request, remote, chain, errors) =>
                    remote != null && request.RequestUri.IsLoopback &&
                    CryptographicOperations.FixedTimeEquals(expected, remote.GetCertHash(HashAlgorithmName.SHA256));
                using (var client = new HttpClient(handler))
                using (var message = new HttpRequestMessage(new HttpMethod(method), uri))
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    message.Headers.Add("responsecompressed", "0");
                    message.Headers.Add("requestcompressed", "0");
                    if (!string.IsNullOrEmpty(bearer))
                        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
                    if (body != null)
                        message.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                    using (var response = client.SendAsync(message).GetAwaiter().GetResult())
                    {
                        response.EnsureSuccessStatusCode();
                        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                }
            }
        }
    }
}
