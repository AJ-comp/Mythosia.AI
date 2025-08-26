namespace Mythosia.AI.Models
{
    /// <summary>
    /// 라운드 처리 결과
    /// </summary>
    public class RoundResult
    {
        public bool IsComplete { get; }
        public string Content { get; }

        private RoundResult(bool isComplete, string content = null)
        {
            IsComplete = isComplete;
            Content = content;
        }

        public static RoundResult Complete(string content)
            => new RoundResult(true, content);

        public static RoundResult Continue()
            => new RoundResult(false);
    }
}