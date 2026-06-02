using System.Net;

namespace GoveeMAUI.Tests.Services;

/// <summary>
/// Mock HttpMessageHandler para simular respuestas HTTP en tests
/// </summary>
internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseContent;
    private readonly Dictionary<string, string> _endpointResponses;

    public MockHttpMessageHandler(string responseContent)
    {
        _responseContent = responseContent;
        _endpointResponses = new();
    }

    /// <summary>
    /// Configura respuesta específica para un endpoint
    /// </summary>
    public void SetEndpointResponse(string endpoint, string response)
    {
        _endpointResponses[endpoint] = response;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buscar respuesta específica para el endpoint
        var content = _responseContent;
        if (request.RequestUri != null && _endpointResponses.Count > 0)
        {
            var path = request.RequestUri.AbsolutePath;
            foreach (var (endpoint, responseBody) in _endpointResponses)
            {
                if (path.Contains(endpoint))
                {
                    content = responseBody;
                    break;
                }
            }
        }

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        };

        return Task.FromResult(httpResponse);
    }
}
