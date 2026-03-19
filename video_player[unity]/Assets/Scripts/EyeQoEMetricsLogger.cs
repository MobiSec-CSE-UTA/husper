using Newtonsoft.Json;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Wave.Essence;
using Wave.Essence.Eye;
using static Wave.Essence.Eye.EyeManager;
using Debug = UnityEngine.Debug;
using Wave.Essence.LipExpression;
using System.Text;

public class EyeQoEMetricsLogger : MonoBehaviour
{
    public EyeManager eyeManager;
    public Stopwatch stopwatch;
    public string logFilePath;
    public string csvFilePath;
    public string mlLogFilePath = "";
    public StreamWriter csvWriter;
    public int fileIndex = 1;
    public ulong sequenceNumber = 0;
    public int segmentNumber = 0;
    public bool isLogging = false;
    public DashVideoMaster dashVideoMaster;

    public const int MaxSegments = 301;
    public List<SegmentData> segmentEyeDataBuffer;
    public SegmentStatus[] segmentStatuses;
    private float[] lipExpressionValues;
    private float[] eyeExpressionValues;

    private bool isLipExpEnabled = false;
    private int currentVideoID = 0;
    private bool csvInitialized = false;
    private PingSender pingSender;

    private int lipReadCount = 0;

    public enum SegmentStatus
    {
        Recording,
        ReadyForModel,
        CollectedByModel
    }

    public class SegmentData
    {
        public ulong SegmentNumber;
        public List<Dictionary<string, object>> EyeDataEntries;
        public List<Dictionary<string, object>> LipDataEntries;
        public List<Dictionary<string, object>> HeadDataEntries;
        public ulong TotalEyeDataEntries;   // Track the number of eye data entries
        public ulong TotalLipDataEntries;   // Track the number of lip data entries
        public ulong TotalHeadDataEntries;  // Track the number of head data entries

        public SegmentData(ulong segmentNumber)
        {
            SegmentNumber = segmentNumber;
            EyeDataEntries = new List<Dictionary<string, object>>();
            LipDataEntries = new List<Dictionary<string, object>>();
            HeadDataEntries = new List<Dictionary<string, object>>();
            TotalEyeDataEntries = 0;
            TotalLipDataEntries = 0;
            TotalHeadDataEntries = 0;
        }
    }

    public SegmentData tempSegmentData = new SegmentData(0);

    void Awake()
    {
        dashVideoMaster = FindObjectOfType<DashVideoMaster>();
        if (dashVideoMaster != null)
        {
            currentVideoID = dashVideoMaster.currentVideoID;
        }
        else
        {
            Debug.LogError("DashVideoMaster instance not found in the scene.");
        }

        if (EyeManager.Instance != null)
        {
            EyeManager.Instance.EnableEyeTracking = true;
        }
        else
        {
            Debug.LogError("EyeManager instance is null.");
        }

        pingSender = FindObjectOfType<PingSender>();

        GameObject eyeManagerObject = GameObject.Find("EyeManager");
        if (eyeManagerObject != null)
        {
            Debug.Log("--->>>  EyeManager component found.");
            eyeManager = eyeManagerObject.GetComponent<EyeManager>();
            if (eyeManager != null)
            {
                Debug.Log("--->>>  EyeManager component enabled.");
                stopwatch = Stopwatch.StartNew();
                // logFilePath = Path.Combine(Application.persistentDataPath, $"eye_metrics_log_{fileIndex}.txt");
                // while (File.Exists(logFilePath))
                // {
                //     fileIndex++;
                //     logFilePath = Path.Combine(Application.persistentDataPath, $"eye_metrics_log_{fileIndex}.txt");
                // }
                // mlLogFilePath = Path.Combine(Application.persistentDataPath, $"ml_interaction_log_{fileIndex}.txt");
                InitializeSegmentBuffers();
                InitializeCSVLogging();
            }
            else
            {
                Debug.LogError("EyeManager component not found on EyeManager GameObject.");
            }
        }
        else
        {
            Debug.LogError("EyeManager GameObject not found in the scene.");
        }

        // Initialize Lip Expressions
        InitializeLipExpressions();

        // Initialize Eye Expressions
        InitializeEyeExpressions();
        logFilePath = Path.Combine(Application.persistentDataPath, $"eye_metrics_log_{fileIndex}.txt");
        mlLogFilePath = Path.Combine(Application.persistentDataPath, $"ml_interaction_log_{fileIndex}.txt");
        // Initialize head movement tracking if needed
    }

    void InitializeLipExpressions()
    {
        if (LipExpManager.Instance != null)
        {
            // Start lip expression tracking
            LipExpManager.Instance.StartLipExp();

            // Check if lip expressions are enabled
            isLipExpEnabled = LipExpManager.Instance.IsLipExpEnabled();

            // Initialize the array to store lip expression values
            lipExpressionValues = new float[(int)LipExp.Max];

            Debug.Log($"Lip expressions enabled: {isLipExpEnabled}");
            if (!isLipExpEnabled)
            {
                Debug.LogWarning("Lip expressions are not enabled. Retrying...");
                LipExpManager.Instance.StartLipExp();
                isLipExpEnabled = LipExpManager.Instance.IsLipExpEnabled();

                string retryLogPath = Path.Combine(Application.persistentDataPath, $"checklist_{dashVideoMaster.currentVideoID}.txt");
                using (StreamWriter writer = new StreamWriter(retryLogPath, true))
                {
                    writer.WriteLine($"[{System.DateTime.Now}] Retried StartLipExp(). Lip expressions enabled: {isLipExpEnabled}");
                }
            }

            // Log the detailed checklist
            // string checklistFilePath = Path.Combine(Application.persistentDataPath, $"checklist_{dashVideoMaster.currentVideoID}.txt");
            // using (StreamWriter writer = new StreamWriter(checklistFilePath, true))
            // {
            //     writer.WriteLine($"[{System.DateTime.Now}] LipExpManager instance is available: True");
            //     writer.WriteLine($"[{System.DateTime.Now}] Lip expressions enabled: {isLipExpEnabled}");
            // }
        }
        else
        {
            // Log error to the console
            Debug.LogError("LipExpManager instance is not available.");

            // Log the detailed checklist
            // string checklistFilePath = Path.Combine(Application.persistentDataPath, $"checklist_{dashVideoMaster.currentVideoID}.txt");
            // using (StreamWriter writer = new StreamWriter(checklistFilePath, true))
            // {
            //     writer.WriteLine($"[{System.DateTime.Now}] LipExpManager instance is available: False");
            //     writer.WriteLine($"[{System.DateTime.Now}] Lip expressions enabled: Not Applicable");
            // }
        }
    }

    void InitializeEyeExpressions()
    {
        if (Wave.OpenXR.InputDeviceEye.IsEyeExpressionAvailable())
        {
            eyeExpressionValues = new float[(int)Wave.OpenXR.InputDeviceEye.Expressions.MAX];
            Debug.Log("Eye expressions initialized.");
            // string checklistpath = Path.Combine(Application.persistentDataPath, $"checklist_{currentVideoID}.txt");
            // File.AppendAllText(checklistpath, $"{System.DateTime.Now}: Eye expressions initialized.\n");
        }
        else
        {
            // Debug.LogWarning("Eye expressions are not available.");
        }
    }

    void InitializeCSVLogging()
    {
        csvFilePath = Path.Combine(Application.persistentDataPath, $"EyeTrackingData_video{dashVideoMaster.currentGroupVideoIndex}.csv");
        csvWriter = new StreamWriter(csvFilePath, true);

        // Create CSV header
        string header = "Timestamp,SequenceNumber,SegmentNumber,DataType,IsEyeTrackingAvailable,HasEyeTrackingData,EyeTrackingStatus," +
                        "LeftEyeOriginX,LeftEyeOriginY,LeftEyeOriginZ," +
                        "RightEyeOriginX,RightEyeOriginY,RightEyeOriginZ," +
                        "CombinedEyeOriginX,CombinedEyeOriginY,CombinedEyeOriginZ," +
                        "LeftEyeDirectionX,LeftEyeDirectionY,LeftEyeDirectionZ," +
                        "RightEyeDirectionX,RightEyeDirectionY,RightEyeDirectionZ," +
                        "CombinedEyeDirectionX,CombinedEyeDirectionY,CombinedEyeDirectionZ," +
                        "LeftEyeOpenness,RightEyeOpenness," +
                        "LeftEyePupilDiameter,RightEyePupilDiameter," +
                        "LeftEyePupilPositionX,LeftEyePupilPositionY," +
                        "RightEyePupilPositionX,RightEyePupilPositionY," +
                        "HeadPosePositionX,HeadPosePositionY,HeadPosePositionZ," +
                        "HeadPoseRotationX,HeadPoseRotationY,HeadPoseRotationZ";

        // Add Lip Expression columns
        for (int i = 0; i < (int)LipExp.Max; i++)
        {
            header += $",LipExpression_{(LipExp)i}";
        }

        // Add an empty column (reserved for future use)
        header += ",Empty";

        // Add Eye Expression columns
        for (int i = 0; i < (int)Wave.OpenXR.InputDeviceEye.Expressions.MAX; i++)
        {
            header += $",EyeExpression_{(Wave.OpenXR.InputDeviceEye.Expressions)i}";
        }

        // Write header to CSV
        csvWriter.WriteLine(header);
        csvInitialized = true;
    }

    void InitializeSegmentBuffers()
    {
        segmentEyeDataBuffer = new List<SegmentData>(MaxSegments);
        segmentStatuses = new SegmentStatus[MaxSegments];

        for (int i = 0; i < MaxSegments; i++)
        {
            segmentEyeDataBuffer.Add(new SegmentData((ulong)i));
            segmentStatuses[i] = SegmentStatus.Recording;
        }
    }

    public void StartLoggingEverySecond()
    {
        if (!isLogging)
        {
            isLogging = true;
            if (!csvInitialized)
            {
                InitializeCSVLogging();
            }
            // Log 120 times per second
            InvokeRepeating(nameof(sendPing), 0, 10f);
            InvokeRepeating(nameof(CollectAndLogEyeData), 0, 1f / 120f);
        }
    }

    public void sendPing()
    {
        string message = "Eyee Tracking is ";
        if (eyeManager != null)
        {
            bool isEyeTrackingAvailable = eyeManager.IsEyeTrackingAvailable();
            bool hasEyeTrackingData = eyeManager.HasEyeTrackingData();
            EyeTrackingStatus status = eyeManager.GetEyeTrackingStatus();
            message += isEyeTrackingAvailable ? "available" : "not available";
            message += ", has data: " + hasEyeTrackingData;
            message += ", status: " + status;
        }
        else
        {
            message += "not available";
        }
        message+="\n";
        message += "Lip Tracking is ";
        if (LipExpManager.Instance != null)
        {
            message += "available";
        }
        else
        {
            message += "not available";
        }
        pingSender.SendPing(message);

    }

    public void StopLogging()
    {
        if (isLogging)
        {
            isLogging = false;
            CancelInvoke(nameof(CollectAndLogEyeData));
            csvWriter.Close();
        }
    }

    public void IncreaseSegmentNumber()
    {
        if(segmentNumber%10==0){
            
            InitializeSegmentBuffers();
        }
        int bufferIndexEye = (int)(segmentNumber % MaxSegments);
        segmentStatuses[bufferIndexEye] = SegmentStatus.ReadyForModel;
        segmentEyeDataBuffer[bufferIndexEye] = tempSegmentData;
        ++bufferIndexEye;
        tempSegmentData = new SegmentData((ulong)bufferIndexEye);
        if(bufferIndexEye >= segmentEyeDataBuffer.Count) Debug.Log("Debug 19 ->>>> Segment number exceeds buffer size.");
        else
        segmentEyeDataBuffer[bufferIndexEye] = new SegmentData((ulong)segmentNumber);  // Reset for next segment
    }

    // public void deleteEyeData(int segmentIndex)
    // {
        
    // }

    /// <summary>
    /// Collects and logs eye, lip, and head data entries.
    /// </summary>
    public void CollectAndLogEyeData()
    {
        Dictionary<string, object> eyeData = new Dictionary<string, object>
        {
            { "Timestamp", stopwatch.Elapsed.TotalMilliseconds },
            { "SequenceNumber", sequenceNumber },
            { "SegmentNumber", segmentNumber },
            { "DataType", "Eye" }
        };

        Dictionary<string, object> lipData = new Dictionary<string, object>
        {
            { "Timestamp", stopwatch.Elapsed.TotalMilliseconds },
            { "SequenceNumber", sequenceNumber },
            { "SegmentNumber", segmentNumber },
            { "DataType", "Lip" }
        };

        Dictionary<string, object> headData = new Dictionary<string, object>
        {
            { "Timestamp", stopwatch.Elapsed.TotalMilliseconds },
            { "SequenceNumber", sequenceNumber },
            { "SegmentNumber", segmentNumber },
            { "DataType", "Head" }
        };

        if (eyeManager != null)
        {
            bool isEyeTrackingAvailable = eyeManager.IsEyeTrackingAvailable();
            bool hasEyeTrackingData = eyeManager.HasEyeTrackingData();
            EyeTrackingStatus status = eyeManager.GetEyeTrackingStatus();

            eyeData["IsEyeTrackingAvailable"] = isEyeTrackingAvailable;
            eyeData["HasEyeTrackingData"] = hasEyeTrackingData;
            eyeData["EyeTrackingStatus"] = status.ToString();

            if (!isEyeTrackingAvailable || status == EyeTrackingStatus.NOT_START)
            {
                Debug.Log("--->>> Eye Tracking is not available or not started. Attempting to start...");
                LogMLInteraction("Eye Tracking is not available or not started.");
            }

            Vector3 leftEyeOrigin, rightEyeOrigin, combinedEyeOrigin;
            Vector3 leftEyeDirection, rightEyeDirection, combinedEyeDirection;
            float leftEyeOpenness, rightEyeOpenness;
            float leftEyePupilDiameter, rightEyePupilDiameter;
            Vector2 leftEyePupilPositionInSensorArea, rightEyePupilPositionInSensorArea;

            // Collect eye tracking data from EyeManager
            eyeManager.GetEyeOrigin(EyeType.Combined, out combinedEyeOrigin);
            eyeManager.GetEyeDirectionNormalized(EyeType.Combined, out combinedEyeDirection);
            eyeManager.GetEyeOrigin(EyeType.Left, out leftEyeOrigin);
            eyeManager.GetEyeDirectionNormalized(EyeType.Left, out leftEyeDirection);
            eyeManager.GetLeftEyeOpenness(out leftEyeOpenness);
            eyeManager.GetLeftEyePupilDiameter(out leftEyePupilDiameter);
            eyeManager.GetLeftEyePupilPositionInSensorArea(out leftEyePupilPositionInSensorArea);
            eyeManager.GetEyeOrigin(EyeType.Right, out rightEyeOrigin);
            eyeManager.GetEyeDirectionNormalized(EyeType.Right, out rightEyeDirection);
            eyeManager.GetRightEyeOpenness(out rightEyeOpenness);
            eyeManager.GetRightEyePupilDiameter(out rightEyePupilDiameter);
            eyeManager.GetRightEyePupilPositionInSensorArea(out rightEyePupilPositionInSensorArea);

            // Fall back to retry getting pupil diameter if it returns zero
            if (leftEyePupilDiameter == 0)
            {
                EyeManager.Instance.GetLeftEyePupilDiameter(out leftEyePupilDiameter);
            }
            if (rightEyePupilDiameter == 0)
            {
                EyeManager.Instance.GetRightEyePupilDiameter(out rightEyePupilDiameter);
            }

            // Additional data for reinforcement learning analysis
            Vector3 headPosePosition = Camera.main.transform.position;
            Vector3 headPoseRotation = Camera.main.transform.eulerAngles;

            // Adding all necessary data to eyeData dictionary
            eyeData["LeftEyeOriginX"] = leftEyeOrigin.x;
            eyeData["LeftEyeOriginY"] = leftEyeOrigin.y;
            eyeData["LeftEyeOriginZ"] = leftEyeOrigin.z;

            eyeData["RightEyeOriginX"] = rightEyeOrigin.x;
            eyeData["RightEyeOriginY"] = rightEyeOrigin.y;
            eyeData["RightEyeOriginZ"] = rightEyeOrigin.z;

            eyeData["CombinedEyeOriginX"] = combinedEyeOrigin.x;
            eyeData["CombinedEyeOriginY"] = combinedEyeOrigin.y;
            eyeData["CombinedEyeOriginZ"] = combinedEyeOrigin.z;

            eyeData["LeftEyeDirectionX"] = leftEyeDirection.x;
            eyeData["LeftEyeDirectionY"] = leftEyeDirection.y;
            eyeData["LeftEyeDirectionZ"] = leftEyeDirection.z;

            eyeData["RightEyeDirectionX"] = rightEyeDirection.x;
            eyeData["RightEyeDirectionY"] = rightEyeDirection.y;
            eyeData["RightEyeDirectionZ"] = rightEyeDirection.z;

            eyeData["CombinedEyeDirectionX"] = combinedEyeDirection.x;
            eyeData["CombinedEyeDirectionY"] = combinedEyeDirection.y;
            eyeData["CombinedEyeDirectionZ"] = combinedEyeDirection.z;

            eyeData["LeftEyeOpenness"] = leftEyeOpenness;
            eyeData["RightEyeOpenness"] = rightEyeOpenness;

            eyeData["LeftEyePupilDiameter"] = leftEyePupilDiameter;
            eyeData["RightEyePupilDiameter"] = rightEyePupilDiameter;

            eyeData["LeftEyePupilPositionX"] = leftEyePupilPositionInSensorArea.x;
            eyeData["LeftEyePupilPositionY"] = leftEyePupilPositionInSensorArea.y;

            eyeData["RightEyePupilPositionX"] = rightEyePupilPositionInSensorArea.x;
            eyeData["RightEyePupilPositionY"] = rightEyePupilPositionInSensorArea.y;

            headData["HeadPosePositionX"] = headPosePosition.x;
            headData["HeadPosePositionY"] = headPosePosition.y;
            headData["HeadPosePositionZ"] = headPosePosition.z;

            headData["HeadPoseRotationX"] = headPoseRotation.x;
            headData["HeadPoseRotationY"] = headPoseRotation.y;
            headData["HeadPoseRotationZ"] = headPoseRotation.z;

            // Lip expressions
            if (isLipExpEnabled && LipExpManager.Instance != null)
            {
                for (int i = 0; i < (int)LipExp.Max; i++)
                {
                    lipExpressionValues[i] = LipExpManager.Instance.GetLipExpression((LipExp)i);
                }

                // Log lip expression data
                for (int i = 0; i < lipExpressionValues.Length; i++)
                {
                    lipData[$"LipExpression_{(LipExp)i}"] = lipExpressionValues[i];
                }

                // Optionally log data
                // Debug.Log("debug 19 --->>> Lip expression data collected.");
                // LogMLInteraction("Lip expression data collected.");
            }
            else
            {
                // Optionally log warning
                // Debug.LogWarning("debug 19 --->>> Lip expressions are not enabled or LipExpManager instance is not available.");
            }

            lipReadCount = (lipReadCount + 1) % 2;

            // Eye expressions
            if (Wave.OpenXR.InputDeviceEye.HasEyeExpressionValue())
            {
                if (Wave.OpenXR.InputDeviceEye.GetEyeExpressionValues(out float[] values))
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        eyeExpressionValues[i] = values[i];
                        eyeData[$"EyeExpression_{(Wave.OpenXR.InputDeviceEye.Expressions)i}"] = values[i];
                    }

                    
                }
                else
                {
                    Debug.LogWarning("Failed to retrieve eye expression values.");
                }
            }
            else
            {
                // Debug.LogWarning("Eye expressions are not available.");
            }

            // Head movement data (if any additional processing is needed, add here)

            // Store the eye data in the current segment's buffer
            int currentSegmentIndex = (int)(segmentNumber % MaxSegments);
            if (segmentStatuses[currentSegmentIndex] == SegmentStatus.Recording)
            {
                
                tempSegmentData.EyeDataEntries.Add(eyeData);
                tempSegmentData.TotalEyeDataEntries++;  // Update the number of entries for the current segment

                // Similarly, store lip data and head data if needed
                if (isLipExpEnabled)
                {
                    tempSegmentData.LipDataEntries.Add(lipData);
                    tempSegmentData.TotalLipDataEntries++;
                }

                // Example: Add head data if collected
                tempSegmentData.HeadDataEntries.Add(headData);
                tempSegmentData.TotalHeadDataEntries++;
            }

            // Write to CSV for eye, lip, and head data
            string csvLine = $"{stopwatch.Elapsed.TotalMilliseconds},{sequenceNumber},{segmentNumber},{eyeData["DataType"]}," +
                             $"{eyeData["IsEyeTrackingAvailable"]},{eyeData["HasEyeTrackingData"]},{eyeData["EyeTrackingStatus"]}," +
                             $"{eyeData["LeftEyeOriginX"]},{eyeData["LeftEyeOriginY"]},{eyeData["LeftEyeOriginZ"]}," +
                             $"{eyeData["RightEyeOriginX"]},{eyeData["RightEyeOriginY"]},{eyeData["RightEyeOriginZ"]}," +
                             $"{eyeData["CombinedEyeOriginX"]},{eyeData["CombinedEyeOriginY"]},{eyeData["CombinedEyeOriginZ"]}," +
                             $"{eyeData["LeftEyeDirectionX"]},{eyeData["LeftEyeDirectionY"]},{eyeData["LeftEyeDirectionZ"]}," +
                             $"{eyeData["RightEyeDirectionX"]},{eyeData["RightEyeDirectionY"]},{eyeData["RightEyeDirectionZ"]}," +
                             $"{eyeData["CombinedEyeDirectionX"]},{eyeData["CombinedEyeDirectionY"]},{eyeData["CombinedEyeDirectionZ"]}," +
                             $"{eyeData["LeftEyeOpenness"]},{eyeData["RightEyeOpenness"]}," +
                             $"{eyeData["LeftEyePupilDiameter"]},{eyeData["RightEyePupilDiameter"]}," +
                             $"{eyeData["LeftEyePupilPositionX"]},{eyeData["LeftEyePupilPositionY"]}," +
                             $"{eyeData["RightEyePupilPositionX"]},{eyeData["RightEyePupilPositionY"]}," +
                             $"{headData["HeadPosePositionX"]},{headData["HeadPosePositionY"]},{headData["HeadPosePositionZ"]}," +
                             $"{headData["HeadPoseRotationX"]},{headData["HeadPoseRotationY"]},{headData["HeadPoseRotationZ"]}";

            // Add Lip Expression data
            if (isLipExpEnabled)
            {
                csvLine += $",{string.Join(",", lipExpressionValues)}";
            }
            else
            {
                // If lip expressions are not enabled, add empty values
                csvLine += ",";
            }

            // Add an empty column (reserved for future use)
            csvLine += ",";

            // Add Eye Expression data
            if (eyeExpressionValues != null && eyeExpressionValues.Length > 0)
            {
                csvLine += $"{string.Join(",", eyeExpressionValues)}";
            }
            else
            {
                csvLine += string.Join(",", new string[(int)Wave.OpenXR.InputDeviceEye.Expressions.MAX]);
            }

            // Write the line to the CSV
            // csvWriter.WriteLine(csvLine);
            // csvWriter.Flush();

            

            // string jsonData = JsonConvert.SerializeObject(eyeData, Formatting.Indented);
            // Log(jsonData);

            sequenceNumber++;
        }
    }

        
        public SegmentData GetSegmentData(int segmentIndex)
        {
            // keep segemnt index as latest available segment data
            
            Debug.Log($"Requested segment data for index {segmentIndex}");
            LogMLInteraction($"Requested segment data for index {segmentIndex}");
            if (segmentIndex >= MaxSegments)
            {
                Debug.LogError("Requested segment index exceeds maximum buffer size.");
                LogMLInteraction("Requested segment index exceeds maximum buffer size.");
                return null;
            }

            if (segmentStatuses[segmentIndex] == SegmentStatus.ReadyForModel)
            {
                SegmentData segmentData = segmentEyeDataBuffer[segmentIndex];

                // Validate that the segment index matches the segment number
                if (segmentData != null && segmentData.SegmentNumber == (ulong)segmentIndex)
                {
                    segmentStatuses[segmentIndex] = SegmentStatus.CollectedByModel;
                    Debug.Log($"Segment data for index {segmentIndex} is ready and collected.");
                    LogMLInteraction($"Segment data for index {segmentIndex} is ready and collected.");
                    return segmentData;
                }
                else
                {
                    Debug.LogError($"Mismatch in requested segment number for index {segmentIndex}");
                    LogMLInteraction($"Mismatch in requested segment number for index {segmentIndex}");
                }
            }
            else
            {
                Debug.LogError("Requested segment data is not ready or already collected.");
                LogMLInteraction("Requested segment data is not ready or already collected.");
            }
            return null;
        }

        
        public void Log(string message)
        {
            // using (StreamWriter writer = new StreamWriter(logFilePath, true))
            // {
            //     writer.WriteLine($"{System.DateTime.Now}: {message}");
            // }
        }

       
        public void LogMLInteraction(string message)
        {
            // using (StreamWriter writer = new StreamWriter(mlLogFilePath, true))
            // {
            //     writer.WriteLine($"{System.DateTime.Now}: {message}");
            // }
        }

        
public float[] GetPreprocessedEyeData(int segmentIndex)
{
    // Use the most recent segment available if needed.
    for (int i = MaxSegments - 1; i >= 0; i--)
    {
        if (segmentStatuses[i] == SegmentStatus.ReadyForModel)
        {
            segmentIndex = i;
            break;
        }
    }
    if (segmentIndex < 0 || segmentIndex >= segmentEyeDataBuffer.Count)
    {
        Debug.LogWarning($"Invalid segment index: {segmentIndex}");
        return null;
    }
    
    SegmentData segmentData = segmentEyeDataBuffer[segmentIndex];
    List<Dictionary<string, object>> eyeEntries = segmentData.EyeDataEntries;
    
    // Define the ordered keys for eye data.
    List<string> orderedKeys = new List<string>
    {
        "LeftEyeOriginX", "LeftEyeOriginY", "LeftEyeOriginZ",
        "RightEyeOriginX", "RightEyeOriginY", "RightEyeOriginZ",
        "CombinedEyeOriginX", "CombinedEyeOriginY", "CombinedEyeOriginZ",
        "LeftEyeDirectionX", "LeftEyeDirectionY", "LeftEyeDirectionZ",
        "RightEyeDirectionX", "RightEyeDirectionY", "RightEyeDirectionZ",
        "CombinedEyeDirectionX", "CombinedEyeDirectionY", "CombinedEyeDirectionZ",
        "LeftEyeOpenness", "RightEyeOpenness",
        "LeftEyePupilDiameter", "RightEyePupilDiameter",
        "LeftEyePupilPositionX", "LeftEyePupilPositionY",
        "RightEyePupilPositionX", "RightEyePupilPositionY"
    };

    
    int numEyeExpressions = (int)Wave.OpenXR.InputDeviceEye.Expressions.MAX;
    

    int featureCount = orderedKeys.Count;
    int timeSteps = 48;
    float[] preprocessedData = new float[timeSteps * featureCount];
    int index = 0;

    // Loop through each recorded entry.
    foreach (var entry in eyeEntries)
    {
        foreach (string key in orderedKeys)
        {
            if (entry.TryGetValue(key, out object value))
            {
                // Preserve the original precision by directly converting to float.
                if (value is float f)
                {
                    preprocessedData[index++] = f;
                }
                else if (value is double d)
                {
                    preprocessedData[index++] = (float)d;
                }
                else if (value is int intVal)
                {
                    preprocessedData[index++] = intVal;
                }
                else if (value is string s && float.TryParse(s, out float parsed))
                {
                    preprocessedData[index++] = parsed;
                }
                else
                {
                    preprocessedData[index++] = 0f;
                }
            }
            else
            {
                preprocessedData[index++] = 0f;
            }
            if (index >= preprocessedData.Length)
                break;
        }
        if (index >= preprocessedData.Length)
            break;
    }

    // Pad any missing values with zeros.
    while (index < preprocessedData.Length)
    {
        preprocessedData[index++] = 0f;
    }

    
    return preprocessedData;
}
public float[] GetLipProcessedData(int segmentIndex)
{
    // Use the most recent segment available.
    for (int i = MaxSegments - 1; i >= 0; i--)
    {
        if (segmentStatuses[i] == SegmentStatus.ReadyForModel)
        {
            segmentIndex = i;
            break;
        }
    }
    if (segmentIndex < 0 || segmentIndex >= segmentEyeDataBuffer.Count)
    {
        Debug.LogWarning($"Invalid segment index: {segmentIndex}");
        return null;
    }
    
    SegmentData segmentData = segmentEyeDataBuffer[segmentIndex];
    List<Dictionary<string, object>> lipEntries = segmentData.LipDataEntries;
    
    List<string> orderedKeys = new List<string>();
    int lipExpCount = (int)LipExp.Max;
    for (int i = 0; i < lipExpCount; i++)
    {
        orderedKeys.Add($"LipExpression_{(LipExp)i}");
        if(i==11) break;
    }
    
    int featureCount = orderedKeys.Count;
    int timeSteps = 48;
    float[] preprocessedData = new float[timeSteps * featureCount];
    int index = 0;

    foreach (var entry in lipEntries)
    {
        foreach (string key in orderedKeys)
        {
            if (entry.TryGetValue(key, out object value))
            {
                if (value is float f)
                    preprocessedData[index++] = f;
                else if (value is double d)
                    preprocessedData[index++] = (float)d;
                else if (value is int intVal)
                    preprocessedData[index++] = intVal;
                else if (value is string s && float.TryParse(s, out float parsed))
                    preprocessedData[index++] = parsed;
                else
                    preprocessedData[index++] = 0f;
            }
            else
            {
                preprocessedData[index++] = 0f;
            }
            if (index >= preprocessedData.Length)
                break;
        }
        if (index >= preprocessedData.Length)
            break;
    }

    while (index < preprocessedData.Length)
    {
        preprocessedData[index++] = 0f;
    }

    // Write the processed lip data to a CSV file.
    // string lipCsvPath = Path.Combine(Application.persistentDataPath, $"PreprocessedLipData_segment{segmentIndex}_video{dashVideoMaster.currentVideoID}.csv");
    // using (StreamWriter writer = new StreamWriter(lipCsvPath))
    // {
    //     // Write header using the ordered keys.
    //     writer.WriteLine(string.Join(",", orderedKeys));

    //     // Write one row per timestep.
    //     for (int row = 0; row < timeSteps; row++)
    //     {
    //         List<string> rowValues = new List<string>();
    //         for (int col = 0; col < featureCount; col++)
    //         {
    //             rowValues.Add(preprocessedData[row * featureCount + col].ToString());
    //         }
    //         writer.WriteLine(string.Join(",", rowValues));
    //     }
    // }

    return preprocessedData;
}
public float[] GetProcessedHeadData(int segmentIndex)
{
    // Use the most recent segment available.
    for (int i = MaxSegments - 1; i >= 0; i--)
    {
        if (segmentStatuses[i] == SegmentStatus.ReadyForModel)
        {
            segmentIndex = i;
            break;
        }
    }
    if (segmentIndex < 0 || segmentIndex >= segmentEyeDataBuffer.Count)
    {
        Debug.LogWarning($"Invalid segment index: {segmentIndex}");
        return null;
    }
    
    SegmentData segmentData = segmentEyeDataBuffer[segmentIndex];
    List<Dictionary<string, object>> headEntries = segmentData.HeadDataEntries;
    
    // Define the ordered keys for head data.
    List<string> orderedKeys = new List<string>
    {
        "HeadPosePositionX", "HeadPosePositionY", "HeadPosePositionZ",
        "HeadPoseRotationX", "HeadPoseRotationY", "HeadPoseRotationZ"
    };
    
    int featureCount = orderedKeys.Count;
    int timeSteps = 48;
    float[] preprocessedData = new float[timeSteps * featureCount];
    int index = 0;

    foreach (var entry in headEntries)
    {
        foreach (string key in orderedKeys)
        {
            if (entry.TryGetValue(key, out object value))
            {
                if (value is float f)
                    preprocessedData[index++] = f;
                else if (value is double d)
                    preprocessedData[index++] = (float)d;
                else if (value is int intVal)
                    preprocessedData[index++] = intVal;
                else if (value is string s && float.TryParse(s, out float parsed))
                    preprocessedData[index++] = parsed;
                else
                    preprocessedData[index++] = 0f;
            }
            else
            {
                preprocessedData[index++] = 0f;
            }
            if (index >= preprocessedData.Length)
                break;
        }
        if (index >= preprocessedData.Length)
            break;
    }

    while (index < preprocessedData.Length)
    {
        preprocessedData[index++] = 0f;
    }

    // Write the processed head data to a CSV file.
    // string headCsvPath = Path.Combine(Application.persistentDataPath, $"PreprocessedHeadData_segment{segmentIndex}_video{dashVideoMaster.currentVideoID}.csv");
    // using (StreamWriter writer = new StreamWriter(headCsvPath))
    // {
    //     // Write header using the ordered keys.
    //     writer.WriteLine(string.Join(",", orderedKeys));

    //     // Write one row per timestep.
    //     for (int row = 0; row < timeSteps; row++)
    //     {
    //         List<string> rowValues = new List<string>();
    //         for (int col = 0; col < featureCount; col++)
    //         {
    //             rowValues.Add(preprocessedData[row * featureCount + col].ToString());
    //         }
    //         writer.WriteLine(string.Join(",", rowValues));
    //     }
    // }

    return preprocessedData;
}

}
