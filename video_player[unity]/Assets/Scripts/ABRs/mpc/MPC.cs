using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MPEGDASHABRController implements a Model Predictive Control (MPC) based ABR algorithm.
/// It uses a planning horizon (e.g., three segments) to simulate the evolution of the playback buffer,
/// and for each candidate quality sequence it computes a reward composed of quality utility and a rebuffer penalty.
/// The quality decision for the next segment is the first quality in the sequence that yields the highest total reward.
/// 
/// This is a research-level example; in production you may wish to adjust the planning horizon,
/// utility functions, rebuffer penalty, or add additional constraints (such as switching penalties).
/// </summary>
public class MPEGDASHABRController : MonoBehaviour
{
    // Available bitrates (in kbps). These should match the values in your SegmentFetcher.
    public List<float> availableBitrates = new List<float> { 2000f, 6000f, 9500f, 15000f, 30000f, 85000f };
    
    // Utility values for each quality level. Typically, higher quality yields a higher utility.
    public List<float> qualityUtilities = new List<float> { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f };

    // Planning horizon (number of segments to simulate into the future).
    // A horizon of 3 means we simulate the next 3 segments.
    public int horizon = 3;

    // Duration of each segment (in seconds). This should match the segmentDuration used in SegmentFetcher.
    public float segmentDuration = 4.0f;

    // Rebuffering penalty weight. This value penalizes any rebuffering time in the reward calculation.
    // (A higher value means the algorithm is more averse to rebuffering.)
    public float rebufferPenalty = 4.3f;

    /// <summary>
    /// Computes the MPC-based ABR decision based on the predicted throughput and current buffer occupancy.
    /// The method simulates future downloads over a planning horizon and selects the candidate sequence
    /// that maximizes the overall reward (quality utility minus rebuffer penalty). The quality decision returned
    /// is the quality for the very next segment.
    /// 
    /// Note: This implementation exhaustively searches over all quality combinations over the horizon.
    /// For horizon=3 and 6 quality levels, that yields 6^3 = 216 candidate sequences.
    /// </summary>
    /// <param name="predictedThroughput">Predicted throughput in kbps.</param>
    /// <param name="currentBuffer">Current buffer occupancy in seconds.</param>
    /// <returns>Index of the selected quality level for the next segment.</returns>
    public int MPEGDASHABRDecision(float predictedThroughput, float currentBuffer)
    {
        int numLevels = availableBitrates.Count;
        float bestReward = float.MinValue;
        currentBuffer = currentBuffer*4;
        int bestQuality = 0; // quality for the first segment in the best sequence

        // Iterate over all candidate sequences for the next 'horizon' segments.
        // For each sequence, simulate the buffer evolution and compute the total reward.
        for (int q0 = 0; q0 < numLevels; q0++)
        {
            for (int q1 = 0; q1 < numLevels; q1++)
            {
                for (int q2 = 0; q2 < numLevels; q2++)
                {
                    float totalReward = 0f;
                    float buffer = currentBuffer;

                    // Candidate quality sequence for the horizon
                    int[] sequence = new int[] { q0, q1, q2 };

                    // Simulate the download and playback for each segment in the horizon.
                    for (int i = 0; i < horizon; i++)
                    {
                        // Compute the segment size in kilobits.
                        // Segment size (in kilobits) = bitrate (kbps) * segmentDuration (s)
                        float segSize = availableBitrates[sequence[i]] * segmentDuration;

                        // Compute the download time (in seconds) using the predicted throughput.
                        // (Assuming predictedThroughput is in kbps as well.)
                        float downloadTime = segSize / predictedThroughput;

                        // Rebuffer time occurs if the download time exceeds the current buffer.
                        float rebufferTime = Math.Max(0f, downloadTime - buffer);

                        // Update the buffer:
                        // If no rebuffering, buffer is decreased by downloadTime;
                        // then the segment's playback duration (segmentDuration) is added.
                        buffer = Math.Max(0f, buffer - downloadTime) + segmentDuration;

                        // The reward for this segment is its quality utility minus the penalty for any rebuffering.
                        totalReward += qualityUtilities[sequence[i]] - rebufferPenalty * rebufferTime;
                    }

                    // If this sequence yields a higher reward than previously seen,
                    // record its first segment's quality decision.
                    if (totalReward > bestReward)
                    {
                        bestReward = totalReward;
                        bestQuality = q0;
                    }
                }
            }
        }

        return bestQuality;
    }
}
