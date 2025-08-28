// Mythosia.AI/Models/Functions/FunctionCallingPolicy.cs
namespace Mythosia.AI.Models.Functions
{
    /// <summary>
    /// Function calling 실행 정책
    /// </summary>
    public class FunctionCallingPolicy
    {
        /// <summary>
        /// 최대 라운드 수 (무한루프 방지)
        /// </summary>
        public int MaxRounds { get; set; } = 20;

        /// <summary>
        /// 전체 실행 타임아웃 (초)
        /// </summary>
        public int? TimeoutSeconds { get; set; } = 100;

        /// <summary>
        /// 병렬 실행 시 최대 동시 실행 수
        /// </summary>
        public int MaxConcurrency { get; set; } = 5;

        /// <summary>
        /// 디버그 로깅 활성화
        /// </summary>
        public bool EnableLogging { get; set; } = false;

        /// <summary>
        /// 기본 정책
        /// </summary>
        public static FunctionCallingPolicy Default => new FunctionCallingPolicy();

        /// <summary>
        /// 빠른 실행용 정책
        /// </summary>
        public static FunctionCallingPolicy Fast => new FunctionCallingPolicy()
        {
            MaxRounds = 10,
            TimeoutSeconds = 30,
            MaxConcurrency = 10
        };


        /// <summary>
        /// 이미지 작업용 정책
        /// </summary>
        public static FunctionCallingPolicy Vision => new FunctionCallingPolicy()
        {
            MaxRounds = 20,
            TimeoutSeconds = 200,
            MaxConcurrency = 3
        };


        /// <summary>
        /// 복잡한 작업용 정책
        /// </summary>
        public static FunctionCallingPolicy Complex => new FunctionCallingPolicy()
        {
            MaxRounds = 50,
            TimeoutSeconds = 300,
            MaxConcurrency = 3
        };

        /// <summary>
        /// 무제한 정책 (주의해서 사용)
        /// </summary>
        public static FunctionCallingPolicy Unlimited => new FunctionCallingPolicy()
        {
            MaxRounds = 100,
            TimeoutSeconds = null,
            MaxConcurrency = 20
        };


        /// <summary>
        /// 정책 복제
        /// </summary>
        public FunctionCallingPolicy Clone()
        {
            return new FunctionCallingPolicy
            {
                MaxRounds = this.MaxRounds,
                TimeoutSeconds = this.TimeoutSeconds,
                MaxConcurrency = this.MaxConcurrency,
                EnableLogging = this.EnableLogging
            };
        }
    }
}