using System;

namespace Mythosia.VectorDb
{
    /// <summary>Per-request options for normalized weighted reciprocal rank fusion.</summary>
    public sealed class HybridSearchOptions
    {
        /// <summary>Dense contribution in [0, 1]. Text contribution is one minus this value.</summary>
        public float VectorWeight { get; set; } = 0.5f;
        /// <summary>Positive multiplier controlling the candidate count fetched from each active leg.</summary>
        public int CandidateMultiplier { get; set; } = 2;
        /// <summary>Positive RRF smoothing constant. Defaults to 60.</summary>
        public int RrfK { get; set; } = 60;

        /// <summary>Creates and validates an independent copy for one request.</summary>
        public HybridSearchOptions Snapshot()
        {
            var copy = new HybridSearchOptions
            {
                VectorWeight = VectorWeight,
                CandidateMultiplier = CandidateMultiplier,
                RrfK = RrfK
            };
            copy.Validate();
            return copy;
        }

        /// <summary>Throws if any option is outside its supported range.</summary>
        public void Validate()
        {
            if (float.IsNaN(VectorWeight) || float.IsInfinity(VectorWeight) || VectorWeight < 0 || VectorWeight > 1)
                throw new ArgumentOutOfRangeException(nameof(VectorWeight), "Vector weight must be finite and between zero and one.");
            if (CandidateMultiplier <= 0)
                throw new ArgumentOutOfRangeException(nameof(CandidateMultiplier), "Candidate multiplier must be positive.");
            if (RrfK <= 0)
                throw new ArgumentOutOfRangeException(nameof(RrfK), "RRF constant must be positive.");
        }

        /// <summary>Returns the expanded candidate count, rejecting invalid or overflowing counts.</summary>
        public int GetCandidateCount(int topK)
        {
            Validate();
            if (topK <= 0 || topK > int.MaxValue / CandidateMultiplier)
                throw new ArgumentOutOfRangeException(nameof(topK), "Top-K must be positive and its expanded candidate count must fit in an integer.");
            return topK * CandidateMultiplier;
        }
    }
}
