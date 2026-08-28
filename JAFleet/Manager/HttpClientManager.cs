namespace JAFleet.Manager
{
    public static class HttpClientManager
    {
        private static readonly HttpClient _client = new();

        public static HttpClient GetInstance() => _client;
    }
}
