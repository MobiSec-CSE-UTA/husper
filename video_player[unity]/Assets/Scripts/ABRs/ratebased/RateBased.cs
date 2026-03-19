using System;
using UnityEngine;

public class RateBasedABRController : MonoBehaviour
{
    private readonly int[] bitrates = { 2000, 6000, 9500, 15000, 30000, 85000  };
    
    private const float RateSafetyMargin = 0.85f; 
    private float lastThroughput = 0f;

    public int RateBasedABRDecision(float currentThroughput)
    {
        lastThroughput = currentThroughput;
        int selectedBitrateIndex = 0;

        for (int i = 0; i < bitrates.Length; i++)
        {
            if (bitrates[i] <= currentThroughput * RateSafetyMargin)
            {
                selectedBitrateIndex = i;
            }
            else
            {
                break;
            }
        }

       
        return bitrates[selectedBitrateIndex];
    }

}