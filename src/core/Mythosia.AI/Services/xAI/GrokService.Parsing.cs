namespace Mythosia.AI.Services.xAI
{
    public partial class GrokService
    {
        #region Response Parsing

        protected override string ExtractResponseContent(string responseContent)
            => _protocol.ExtractResponse(responseContent);

        protected override string StreamParseJson(string jsonData)
            => _protocol.ParseStreamChunk(jsonData);

        #endregion
    }
}
