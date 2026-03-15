namespace Mythosia.AI.Models
{
    /// <summary>
    /// Structured output 실행 정책.
    /// 전체 서비스 기본값(<see cref="Services.Base.AIService.StructuredOutputMaxRetries"/>)과 별개로,
    /// 특정 호출에만 일회성으로 적용할 수 있습니다.
    /// </summary>
    public class StructuredOutputPolicy
    {
        /// <summary>
        /// LLM이 잘못된 JSON을 반환했을 때 자동 수정 프롬프트로 재시도하는 최대 횟수.
        /// null이면 서비스 기본값(<see cref="Services.Base.AIService.StructuredOutputMaxRetries"/>)을 사용합니다.
        /// </summary>
        public int? MaxRepairAttempts { get; set; }

        /// <summary>
        /// 기본 정책 (서비스 기본값을 그대로 사용)
        /// </summary>
        public static StructuredOutputPolicy Default => new StructuredOutputPolicy();

        /// <summary>
        /// retry 없이 1회만 시도
        /// </summary>
        public static StructuredOutputPolicy NoRetry => new StructuredOutputPolicy
        {
            MaxRepairAttempts = 0
        };

        /// <summary>
        /// 엄격 모드 (최대 3회 재시도)
        /// </summary>
        public static StructuredOutputPolicy Strict => new StructuredOutputPolicy
        {
            MaxRepairAttempts = 3
        };
    }
}
