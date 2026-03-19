using System;
using System.Collections.Generic;
using UnityEngine;

public class BOLAABRController : MonoBehaviour
{
    // Bitrates (kbps) and utilities, matching SegmentFetcher
    public List<float> availableBitrates = new List<float>
      { 2000f, 6000f, 9500f, 15000f, 30000f, 85000f };
    public List<float> qualityUtilities = new List<float>
      { 1f, 2f, 3f, 4f, 5f, 6f };

    public int maxBufferSegments = 15;

    public float reservoir = 5f;
    public float cushion   = 10f;

    // Weight for the linear ramp term
    [Tooltip("Higher = stronger linear ramp in score")]  
    public float rampWeight = 1f;

    private float uMin, uMax, Vp;

    private void Start()
    {
        uMin = Mathf.Min(qualityUtilities.ToArray());
        uMax = Mathf.Max(qualityUtilities.ToArray());
        Vp  = (uMax - uMin) / Mathf.Log(1f + cushion / reservoir);

        Debug.Log($"Hybrid BOLA+Log params → reservoir={reservoir}s, cushion={cushion}s, Vp={Vp}, rampWeight={rampWeight}");
    }

    
    public int BOLAABRDecision(float currentBuffer, float segmentDuration)
    {
        float bufferSeconds = currentBuffer * segmentDuration;

        if (bufferSeconds < reservoir)
            return 0;

        int bestIndex = 0;
        float bestScore = float.NegativeInfinity;

        float linearRatio = Mathf.Clamp01(currentBuffer / (float)maxBufferSegments);

        for (int i = 0; i < availableBitrates.Count; i++)
        {
            float utilityGap = qualityUtilities[i] - uMin;
            float brateKbps  = availableBitrates[i];

            float bolaTerm   = utilityGap + Vp * Mathf.Log(bufferSeconds / reservoir);
            float linearTerm = rampWeight * (linearRatio * utilityGap);

            float score = (bolaTerm + linearTerm) / brateKbps;

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        Debug.Log($"Hybrid BOLA+Log chose idx={bestIndex} (bitrate={availableBitrates[bestIndex]} kbps, score={bestScore}) for bufferSegments={currentBuffer}");
        return bestIndex;
    }
}
