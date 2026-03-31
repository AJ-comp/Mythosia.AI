namespace Mythosia.AI.Providers.Alibaba
{
    public partial class QwenService
    {
        protected override string ExtractResponseContent(string responseContent)
            => _protocol.ExtractResponse(responseContent);

        protected override string StreamParseJson(string jsonData)
            => _protocol.ParseStreamChunk(jsonData);
    }
}
