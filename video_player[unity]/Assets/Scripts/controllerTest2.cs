using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.XR;

public class InputTracker : MonoBehaviour 
{
    private XRNode rightHand = XRNode.RightHand;
    private XRNode leftHand = XRNode.LeftHand;
    private string logFileName;
    public int segmentNumber = 0;
    private int fileIndex = 0;
    public int trackQueueSize = 0;
    public static int MaxSegments = 150;
    public int segmentsLogged = 0;
    private SegmentFetcher segmentFetcher;

    public int[] inputs_for_segemnts = new int[MaxSegments+1];

    private DashVideoPlayer dashVideoPlayer;

    public Queue<InputLogEntry> inputQueue = new Queue<InputLogEntry>();
    private bool waitingForInput = false;
    private int videoIndex = 0;
    private DashVideoMaster dashVideoMaster;

    void Start()
    {
        dashVideoPlayer = FindObjectOfType<DashVideoPlayer>();
        segmentFetcher = FindObjectOfType<SegmentFetcher>();
        dashVideoMaster = FindObjectOfType<DashVideoMaster>();
        try{
        videoIndex = dashVideoPlayer.currentVideoID;
        int currentGroupIndex = dashVideoMaster.currentGroupIndex;
        string activeABR = segmentFetcher.activeABR;
        int randInt = UnityEngine.Random.Range(0, 1000);
        Debug.Log("----->>>> debug 22 input tracker log file name: " + $"controller_inputs{videoIndex}_{currentGroupIndex}_{activeABR}_{randInt}.txt");
        logFileName = $"controller_inputs{videoIndex}_{currentGroupIndex}_{activeABR}_{randInt}.txt";
        fileIndex = GetNextLogFileIndex(); // Get the next available file index
        }
        catch(Exception e){
            Debug.Log("debug 19 input tracekr error--->>>> " + e.Message);
            
            logFileName = $"controller_inputs{videoIndex}txt";
        }
        LogInput("InputTracker started.");
        for(int i = 0; i < MaxSegments+1; i++){
            inputs_for_segemnts[i] = 0;
        }
    }

    public void StartTracking()
    {
        Debug.Log("debug 19 ----->>>>> StartInputTracking() called.");

        StartCoroutine(TrackingLoop());
    }

    private IEnumerator TrackingLoop()
    {
        while(true)
        {
            SendImpulseAndTrackInput(segmentNumber);
            segmentNumber++;
            yield return new WaitForSeconds(4f);
        }
    }

    public void SendImpulseAndTrackInput(int segmentNumber)
    {
        trackQueueSize += 1;
        if (trackQueueSize > 1)
        {
            LogInput("Already waiting for input. Adding segment to queue, size: " + trackQueueSize + ", queue Size: " + inputQueue.Count + " segment: " + segmentNumber);
            Debug.LogWarning("Already waiting for input. Adding segment to queue, size: " + trackQueueSize + ", queue Size: " +  inputQueue.Count + " segment: " + segmentNumber);
            
            inputQueue.Enqueue(new InputLogEntry(segmentNumber));
            return;
        }
        StartCoroutine(WaitForButtonPressAndLog(segmentNumber));
    }

    private IEnumerator WaitForButtonPressAndLog(int segmentNumber)
    {
        LogInput($"Waiting for button press to log segment number {segmentNumber}...");

        // Send impulse to both controllers
        SendHapticImpulse(rightHand);
        SendHapticImpulse(leftHand);

        bool inputDetected = false;

        while (!inputDetected)
        {
            if (CheckForButtonPress(rightHand) || CheckForButtonPress(leftHand))
            {
                inputDetected = true;
                trackQueueSize -= 1;
                segmentsLogged += 1;
                LogInput($"Segment number {segmentNumber} logged with button press.");

                // Log segment number and timestamp to "controller_inputsX.txt" file
                string logMessage = $"Timestamp: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}, Segment Number: {segmentNumber}";
                File.AppendAllText(Path.Combine(Application.persistentDataPath, logFileName), logMessage + "\n");
                yield return new WaitForSeconds(0.5f);
            }
            else {
                yield return null;
            }
        }

        // Check if there are queued inputs
        Debug.Log("debug 19 ----->>>>> Queue size: " + inputQueue.Count);
        if (inputQueue.Count > 0)
        {
            yield return new WaitForSeconds(0.7f);
            InputLogEntry nextInput = inputQueue.Dequeue();
            Debug.Log("debug 19 ----->>>>> Queue size: " + inputQueue.Count);
            StartCoroutine(WaitForButtonPressAndLog(nextInput.segmentNumber));
        }
        else
        {
            int lastSegment = segmentFetcher.segmentNumber;
            if(segmentNumber >= lastSegment-5 && segmentNumber > 10)
            {
                Debug.Log("debug 19 ----->>>>> All inputs have been provided, calling EndPlayback(). queue size: " + inputQueue.Count + " , segment number: " + segmentNumber);
                dashVideoPlayer.InputTracker_EndPlayback();
            }
        }
    }

    private bool CheckForButtonPress(XRNode hand)
    {
        List<InputFeatureUsage> features = new List<InputFeatureUsage>();
        InputDevice device = InputDevices.GetDeviceAtXRNode(hand);

        if (device.isValid)
        {
            device.TryGetFeatureUsages(features);
            foreach (var feature in features)
            {
                if (feature.type == typeof(bool))
                {
                    bool value;
                    if (device.TryGetFeatureValue(feature.As<bool>(), out value) && value)
                    {
                        if(feature.name != "IsTracked")
                        { 
                            LogInput($"{hand} - {feature.name} pressed");
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    private void SendHapticImpulse(XRNode hand)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(hand);

        if (device.isValid)
        {
            HapticCapabilities capabilities;
            if (device.TryGetHapticCapabilities(out capabilities) && capabilities.supportsImpulse)
            {
                uint channel = 0;
                device.SendHapticImpulse(channel, 1.0f, 0.7f); // Adjust intensity and duration as needed
                LogInput($"Haptic feedback sent to {hand}.");
            }
            else
            {
                LogInput($"Haptic feedback not supported for {hand}.");
            }
        }
        else
        {
            LogInput($"{hand} device is not valid for haptic feedback.");
        }
    }

    private void LogInput(string message)
    {
        Debug.Log("debug 19 --->>>> " + message);
        if(logFileName == null){
            try{
            logFileName = $"controller_inputs{videoIndex}_{dashVideoMaster.currentGroupIndex}_{segmentFetcher.activeABR}_{fileIndex}.txt";
            }
            catch(Exception e){
                Debug.Log("debug 19 --->>>> " + e.Message);
            }
            if(logFileName == null)
            {
                int randomInt2 = UnityEngine.Random.Range(0, 1000);
                logFileName = $"controller_inputs{videoIndex}_{randomInt2}.txt";
            }
        }
        File.AppendAllText(Path.Combine(Application.persistentDataPath, logFileName), message + "\n");
    }

    private int GetNextLogFileIndex()
    {
        int index = 0;
        while (File.Exists(Path.Combine(Application.persistentDataPath, $"controller_inputs{index}.txt")))
        {
            index++;
        }
        return index;
    }

    public struct InputLogEntry
    {
        public int segmentNumber;

        public InputLogEntry(int segmentNumber)
        {
            this.segmentNumber = segmentNumber;
        }
    }
}
