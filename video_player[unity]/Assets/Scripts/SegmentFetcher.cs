using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using System;
using Random = System.Random;
using System.Text;
using System.Linq;
using System.Diagnostics; // For Stopwatch
using Debug = UnityEngine.Debug;

public class SegmentFetcher : MonoBehaviour
{
    private BufferManager bufferManager;
  
    public bool fetchedAllSegments = false;
    public string streamURLPrefix = "";
    private SimpleDQNInference dqnAgent; // DQN agent for ABR decisions
    
    public List<float> past_throughputs = new List<float>();
    public List<float> past_buffer_levels = new List<float>();
    public List<int> past_bitrates = new List<int>();

    private PingSender pingSender;

    private Stopwatch decisionStopwatch; // Timing decision latency
    private Stopwatch downloadStopwatch;   // Timing download latency
    private const int MaxSegments = 15; // Maximum number of segments to fetch

    private EyeQoEMetricsLogger eyeQoEMetricsLogger; // EyeQoE metrics logger
    private RateBasedABRController rateBasedABRController; // Rate-based ABR controller
    private MPEGDASHABRController mpegdashABRController; // MPEG-DASH ABR controller
    private PensieveModel pensiveABR; // Pensieve ABR model
    private const int RandomizedSegments = 3;  // Number of initial segments to randomize
    public int segmentNumber = 1;
    public int video_segment = 1;
    public int eyeSegment = 1;
    private string latencyLogFile = "";
    private float avgLatency = 0;
    private float count = 1.0f;
    private List<int> decisionArray = new List<int>();  // Array to store decisions per segment
    public string activeABR  = "dqn"; // Default ABR type
    private DateTime appStartTime;
    private DashVideoPlayer dashVideoPlayer;
    private string[] abrTypes = { "bola", "rate", "mpc", "pensive", "dqn", "random" , "test"}; // ABR types
    private int[] bitrates = { 2000,6000,9500,15000,30000,85000 }; // Bitrates in kbps

    
    private Dictionary<string, List<int>> representationChunkSizes = new Dictionary<string, List<int>>();

    // NEW: Store the last download delay (in seconds) for use in state row 3.
    private float lastDownloadDelay = 0f;
    private BOLAABRController bolaABRController;
   


    private float[,] dqnStateHistory; // Dimensions: [6,8]

    public string baseUrl_public = "";
    private int groupVariable = 1;
    private DashVideoMaster videoMaster;
    public int lastSegmentDownloaded = 0;
     public int bufferSizeDqn = 12; // Buffer size for DQN ABR

    void Awake()
    {
        bufferManager = BufferManager.Instance;
      
        latencyLogFile = Path.Combine(Application.persistentDataPath, "logLatency.txt");

        // Initialize DQNAgent (all parameters handled internally by the agent)
        dqnAgent = FindObjectOfType<SimpleDQNInference>();

        rateBasedABRController = gameObject.AddComponent<RateBasedABRController>();

        // Initialize MPEG-DASH ABR Controller
        mpegdashABRController = gameObject.AddComponent<MPEGDASHABRController>();
        
        // Initialize Pensieve ABR model
        pensiveABR = gameObject.AddComponent<PensieveModel>();

        // Initialize EyeQoE metrics logger
        eyeQoEMetricsLogger = FindObjectOfType<EyeQoEMetricsLogger>();

        dashVideoPlayer = FindObjectOfType<DashVideoPlayer>();
        videoMaster = FindObjectOfType<DashVideoMaster>();
        bolaABRController = gameObject.AddComponent<BOLAABRController>();
        
        pingSender = FindObjectOfType<PingSender>();

        decisionStopwatch = new Stopwatch();
        downloadStopwatch = new Stopwatch();
        appStartTime = DateTime.Now;

        if (!File.Exists(latencyLogFile))
        {
            File.AppendAllText(latencyLogFile, $"Base URL: {streamURLPrefix}\n");
        }
        
        // Set buffer size (in seconds)
        bufferManager.SetBufferSize(bufferSizeDqn);

        dqnStateHistory = new float[6, 8];
        for (int i = 0; i < 6; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                dqnStateHistory[i, j] = 0f;
            }
        }
        
        if(videoMaster.currentGroupIndex == 0){
            activeABR = "dqn";
            Debug.Log("--->> debug 1X22 : Abr " + activeABR + " setting buffer size to " + bufferSizeDqn);
            bufferManager.SetBufferSize(bufferSizeDqn);
        }
        else if(videoMaster.currentGroupIndex == 1){
            activeABR = "pensive";
            Debug.Log("--->> debug 1X22 : Abr " + activeABR + " setting buffer size to " + 15);
            bufferManager.SetBufferSize(15);
        }
        else if(videoMaster.currentGroupIndex == 2){
            activeABR = "mpc";
            Debug.Log("--->> debug 1X22 : Abr " + activeABR + " setting buffer size to " + 15);
            bufferManager.SetBufferSize(15);
        }
        else if(videoMaster.currentGroupIndex == 3){
            activeABR = "bola";
            Debug.Log("--->> debug 1X22 : Abr " + activeABR + " setting buffer size to " + 15);
            bufferManager.SetBufferSize(15);
        }
        
        else if(videoMaster.currentGroupIndex == 4){
            activeABR = "rate";
            Debug.Log("--->> debug 1X22 : Abr " + activeABR + " setting buffer size to " + 15);
            bufferManager.SetBufferSize(15);
        }
        InvokeRepeating(nameof(SendPing), 0, 10);
    }

    private string GetCsvFilePath(string prefix, int sessionID)
    {
        return Path.Combine(Application.persistentDataPath, $"{prefix}_Session{sessionID}.csv");
    }

    string ConvertTimeToReadable(float time)
    {
        DateTime actualTime = appStartTime.AddSeconds(time);
        return actualTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
    }

    private void WriteToCsv(int sessionID, int segmentNumber, float decisionStartTime, float decisionEndTime, float downloadStartTime, float downloadEndTime, float totalTime, 
        string algoName = "DQN")
    {
        string csvFilePath = GetCsvFilePath($"{algoName}_Latency", sessionID);
        bool fileExists = File.Exists(csvFilePath);

        StringBuilder row = new StringBuilder();
        if (!fileExists)
        {
            row.AppendLine("SegmentNumber,DownloadStartTime,DownloadEndTime,TotalTime");
        }

        string readableDecisionStart = ConvertTimeToReadable(decisionStartTime);
        string readableDecisionEnd = ConvertTimeToReadable(decisionEndTime);
        string readableDownloadStart = ConvertTimeToReadable(downloadStartTime);
        string readableDownloadEnd = ConvertTimeToReadable(downloadEndTime);

        row.AppendLine($"{segmentNumber},{readableDownloadStart},{readableDownloadEnd},{totalTime:F3}");
        File.AppendAllText(csvFilePath, row.ToString());
    }

   
    private IEnumerator FetchChunkSizesForRepresentations(List<Representation> representations)
    {
        foreach (var rep in representations)
        {
            string chunkSizesUrl = streamURLPrefix + rep.Media
                .Replace("$RepresentationID$", rep.Id)
                .Replace("$Number$", "ChunkSizes")
                .Replace(".mp4", (GetPlatformCode() == 'L' ? "" : ""));
            UnityWebRequest request = UnityWebRequest.Get(chunkSizesUrl);
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                string text = request.downloadHandler.text;
                string[] lines = text.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                List<int> sizes = new List<int>();
                foreach (string line in lines)
                {
                    if (int.TryParse(line.Trim(), out int size))
                    {
                        sizes.Add(size);
                    }
                }
                representationChunkSizes[rep.Id] = sizes;
                Debug.Log($"Fetched chunk sizes for rep {rep.Id}, count: {sizes.Count}");
            }
            else
            {
                Debug.LogError($"Error fetching chunk sizes for rep {rep.Id}: {request.error}");
                representationChunkSizes[rep.Id] = new List<int>();
            }
        }
    }
    [Serializable]
public class PensieveRequestString
{
    // Use a jagged array so that Unity's JsonUtility can serialize it.
    public string state;
}

[Serializable]
public class PensieveResponse
{
    public int action;  // The chosen action index (0 to 5)
}
private string ConvertStateToString(float[,] state)
    {
        int rows = state.GetLength(0);
        int cols = state.GetLength(1);
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                sb.Append(state[i, j].ToString("G6"));
                // Append comma if not last element
                if (i != rows - 1 || j != cols - 1)
                    sb.Append(",");
            }
        }
        return sb.ToString();
    }


public IEnumerator SendPensieveRequestAndGetDecision(float[,] stateHistory, Action<int> callback)
    {
        string stateStr = ConvertStateToString(stateHistory);
        PensieveRequestString req = new PensieveRequestString { state = stateStr };
        string jsonData = JsonUtility.ToJson(req);
        Debug.Log("Sending Pensieve request (string): " + jsonData);
        string serverUrl = "http://192.168.1.179:5050/";
        UnityWebRequest www = new UnityWebRequest(serverUrl, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            string responseText = www.downloadHandler.text;
            Debug.Log("Received Pensieve response: " + responseText);
            try
            {
                PensieveResponse response = JsonUtility.FromJson<PensieveResponse>(responseText);
                callback(response.action);
            }
            catch (Exception ex)
            {
                Debug.LogError("Error parsing Pensieve response: " + ex.Message);
                callback(GetFallbackMPCDecision());
            }
        }
        else
        {
            Debug.LogError("Pensieve request error: " + www.error);
            callback(GetFallbackMPCDecision());
        }
    }





    private void SendPing(string message)
    {
        pingSender.SendPing(message);
    }
   

    private void RollDqnStateHistory()
    {
        for (int i = 0; i < 6; i++)
        {
            for (int j = 0; j < 7; j++)
            {
                dqnStateHistory[i, j] = dqnStateHistory[i, j + 1];
            }
            
            if (i != 4)
            {
                dqnStateHistory[i, 7] = 0f;
            }
        }
    }

private float NormalizeThroughput_Linear(float realThroughputMbps)
{
    float t = Mathf.Clamp(realThroughputMbps, 20f, 85f);

    // 2) Linear interpolation

    float xMin = 20f, xMax = 85f;
    float yMin = 0.2f, yMax = 4.3f;

    float scaled = yMin + (yMax - yMin) * ((t - xMin) / (xMax - xMin));
    return scaled;
}

    private void ABR_State_Manager(float lastBitrate, float currentBuffer, float lastThroughput, float lastDownloadDelay, int segmentNumber, int totalChunks, float[] avg_chunkSizes)
{
    // Compute normalized measurements:
    float lastQualityNormalized = lastBitrate / 85000f;     // Normalize bitrate by max bitrate (4300 kbps)
    float bufferNormalized = currentBuffer*4 / 10f;          // Normalize buffer (assuming 10 sec max)
    float throughputNormalized = lastThroughput / 1000f;
    throughputNormalized  = NormalizeThroughput_Linear(throughputNormalized);
    float delayNormalized = lastDownloadDelay / 10f;       // Normalize delay (assuming division by 10)

    float[] nextChunkSizesNormalized = new float[6];
    for (int i = 0; i < 6; i++)
    {
        nextChunkSizesNormalized[i] = (i < avg_chunkSizes.Length ? avg_chunkSizes[i] : 0f) / 1e6f;
    }

    // Compute normalized remaining chunks.
    int remaining = Mathf.Max(totalChunks - segmentNumber, 0);
    float remainingNormalized = remaining / (float)totalChunks;

    // Roll and update the persistent state history (dqnStateHistory) using your pre‐defined function.
    
    RollDqnStateHistory();
    UpdateDqnStateHistory(lastQualityNormalized, bufferNormalized, throughputNormalized, delayNormalized, nextChunkSizesNormalized, remainingNormalized);
}

    
    private void UpdateDqnStateHistory(float lastQualityNormalized, float bufferNormalized, float throughputNormalized, float delayNormalized, float[] nextChunkSizesNormalized, float remainingNormalized)
    {
        // For rows 0,1,2,3,5: update column 7 with new scalar measurement.
        dqnStateHistory[0, 7] = lastQualityNormalized;
        dqnStateHistory[1, 7] = bufferNormalized;
        dqnStateHistory[2, 7] = throughputNormalized;
        dqnStateHistory[3, 7] = delayNormalized;
        dqnStateHistory[5, 7] = remainingNormalized;

        // For row 4: update columns 0..5 with the new vector.
        for (int q = 0; q < 6; q++)
        {
            dqnStateHistory[4, q] = nextChunkSizesNormalized[q];
        }
    }

    // FetchSegments: First pre-fetch chunk sizes, then fetch segments.
    public IEnumerator FetchSegments(List<Representation> representations, int currentVideoID, int currentQualityIndex)
    {   
        currentVideoID = videoMaster.currentGroupIndex*100 + videoMaster.currentGroupVideoIndex;
        // Pre-fetch chunk sizes for all representations.
        yield return StartCoroutine(FetchChunkSizesForRepresentations(representations));

        if (eyeQoEMetricsLogger == null)
        {
            eyeQoEMetricsLogger = FindObjectOfType<EyeQoEMetricsLogger>();
        }
        var Rand = new Random();
        baseUrl_public = streamURLPrefix;
        Debug.Log("---->>>> debug 19 Fetching segments.");
        int action = 2000; // Default action
         
        if(videoMaster.currentGroupIndex == 0){
            activeABR = "dqn";
            Debug.Log("--->> debug 1X22 : Abr " + activeABR + " setting buffer size to " + bufferSizeDqn);
            bufferManager.SetBufferSize(bufferSizeDqn);
        }
        else 
        if(videoMaster.currentGroupIndex == 1){
            activeABR = "pensive";
            Debug.Log("--->> debug 1X22 : Abr " + activeABR + " setting buffer size to " + 15);
            bufferManager.SetBufferSize(15);
        }
        else if(videoMaster.currentGroupIndex == 2){
            activeABR = "mpc";
            Debug.Log("--->> debug 1X22 : Abr " + activeABR + " setting buffer size to " + 15);
            bufferManager.SetBufferSize(15);
        }
        else if(videoMaster.currentGroupIndex == 3){
            activeABR = "bola";
            Debug.Log("--->> debug 1X22 : Abr " + activeABR + " setting buffer size to " + 15);
            bufferManager.SetBufferSize(15);
        }
        
        else if(videoMaster.currentGroupIndex == 4){
            activeABR = "rate";
            Debug.Log("--->> debug 1X22 : Abr " + activeABR + " setting buffer size to " + 15);
            bufferManager.SetBufferSize(15);
        }

        Debug.Log("---->>>> debug 19 Fetching segments with ABR: " + activeABR);
        pingSender.SendPing("video group : " + videoMaster.currentGroupIndex + " video index: " + videoMaster.currentGroupVideoIndex + " ABR: " + activeABR);
        
        
        while (fetchedAllSegments == false)
        {
            if (bufferManager.GetBufferSize() < bufferManager.GetMaxBufferSize())
            {
                float startTimeLatency = Time.time;
                float endTimeLatency = Time.time;
                // if (segmentNumber <= RandomizedSegments && activeABR == "dqn")
                // {
                //     action = bitrates[Rand.Next(representations.Count)];
                // }
                // else
                if(activeABR!="NULL")
                { 
                    
                    int prev_action = action;
                    if (activeABR == "rate")
                    {
                        Debug.Log("---->>>> debug 19 User set ABR :Rate-based ABR decision.");
                        decisionStopwatch.Restart();
                        float currentThroughput = past_throughputs.Count > 0 ? past_throughputs[^1] : 30000f;
                        startTimeLatency = Time.time;
                        action = rateBasedABRController.RateBasedABRDecision(currentThroughput);
                        endTimeLatency = Time.time;
                        decisionStopwatch.Stop();
                    }
                    else if (activeABR == "mpc")
{
    Debug.Log("---->>>> debug: Using MPC ABR decision.");
    decisionStopwatch.Restart();
    float currentThroughput = 30000f;
    if (past_throughputs.Count >= 5)
    {
        int throughput_count = past_throughputs.Count;
        float lastThroiughput = throughput_count>0?past_throughputs[throughput_count-1]:currentThroughput;
        currentThroughput = lastThroiughput;
    }
    else if (past_throughputs.Count > 0)
    {
        currentThroughput = past_throughputs[past_throughputs.Count - 1];
    }
    startTimeLatency = Time.time;
    // Get current buffer level from the buffer manager.
    float currentBuffer = bufferManager.GetBufferSize();
    // Call the MPC ABR decision method.
    int mpcQualityIndex = mpegdashABRController.MPEGDASHABRDecision(currentThroughput, currentBuffer);
    // Map the quality index to a bitrate (ensure consistency with your bitrates array).
    action = bitrates[mpcQualityIndex];
    decisionStopwatch.Stop();
    endTimeLatency = Time.time;
}

                    else if (activeABR == "pensive")
{
    Debug.Log("---->>>> debug: Using Pensieve ABR decision via server.");
    
    // Assume these values are computed earlier:
    float lastBitrate = past_bitrates.Count > 0 ? past_bitrates[past_bitrates.Count - 1] : bitrates[1];
    float currentBuffer = bufferManager.GetBufferSize();
    float lastThroughput = past_throughputs.Count > 0 ? past_throughputs[past_throughputs.Count - 1] : 1400000f;
    float delay = lastDownloadDelay;
    int totalChunks = 60;  // Total expected segments/chunks
  float[] avg_chunkSizes = new float[6];
avg_chunkSizes = new float[] { 1.2f, 3.8f, 5.9f, 9.1f, 14.1f, 45.5f };

    ABR_State_Manager(lastBitrate, currentBuffer, lastThroughput, delay, segmentNumber, totalChunks, avg_chunkSizes);
    
    bool pensieveDecided = false;
    int serverAction = -1;
    yield return StartCoroutine(SendPensieveRequestAndGetDecision(dqnStateHistory, (actionFromServer) =>
    {
        serverAction = actionFromServer;
        pensieveDecided = true;
    }));
    
    if (pensieveDecided)
    {
        Debug.Log($"Pensieve server selected action: {serverAction}");
        if(serverAction>=2000){
            action = serverAction;
        }
        else
        action = bitrates[serverAction]; // Or use serverAction directly if it already maps to bitrate index.
    }
    else
    {
        Debug.LogWarning("Pensieve server failed to respond; using fallback MPC decision.");
        
    }
    
}

                    else if (activeABR == "bola")
{
    Debug.Log("---->>>> debug: Using BOLA ABR decision.");
    decisionStopwatch.Restart();
    startTimeLatency = Time.time;
    // Get the current buffer level (in seconds) from your buffer manager.
    float currentBuffer = bufferManager.GetBufferSize();
    // Make the BOLA decision.
    int segmentDuration = 4;
    int bolaQualityIndex = bolaABRController.BOLAABRDecision(currentBuffer, segmentDuration);
    // Map the selected index to the corresponding bitrate.
    action = bitrates[bolaQualityIndex];
    
    decisionStopwatch.Stop();
    endTimeLatency = Time.time;
}

                    else if (activeABR == "dqn")
                    {
                        Debug.Log("---->>>> debug 19 User set ABR: DQN ABR decision.");
                        if (dqnAgent != null)
                        {
                            try
                            {
                                // NEW: Roll the persistent state history.
                                RollDqnStateHistory();

                                // Compute new measurements:
                                // Row 0: Last video quality normalized.
                                float lastBitrate = past_bitrates.Count > 0 ? past_bitrates[past_bitrates.Count - 1] : bitrates[3];
                                float maxBitrate = 85000f;
                                float lastQualityNormalized = lastBitrate / maxBitrate;

                                // Row 1: Buffer size normalized.
                                float bufferLevel = bufferManager.GetBufferSize();
                                float bufferNormalized = bufferLevel / 6.0f;

                                
                                float lastThroughputNorm = past_throughputs.Count > 0 ? past_throughputs[past_throughputs.Count - 1] : 1400000f;
                                lastThroughputNorm = lastThroughputNorm;
                                float throughputNormalized = lastThroughputNorm;
                                float delayNormalized = lastDownloadDelay / 10.0f;
                                float[] nextChunkSizesNormalized = new float[6];
                                for (int q = 0; q < 6; q++)
                                {
                                    if (q < representations.Count)
                                    {
                                        string repId = representations[q].Id;
                                        if (representationChunkSizes.TryGetValue(repId, out List<int> chunkSizes))
                                        {
                                            int idx = segmentNumber - 1; // assuming 0-indexed
                                            if (idx < chunkSizes.Count)
                                            {
                                                nextChunkSizesNormalized[q] = chunkSizes[idx] / 1000000f; // bytes -> MB
                                            }
                                            else
                                            {
                                                nextChunkSizesNormalized[q] = 0f;
                                            }
                                        }
                                        else
                                        {
                                            nextChunkSizesNormalized[q] = 0f;
                                        }
                                    }
                                    else
                                    {
                                        nextChunkSizesNormalized[q] = 0f;
                                    }
                                }

                                // Row 5: Remaining chunks normalized.
                                int totalChunks = 1200;
                                int remaining = Mathf.Max(totalChunks - segmentNumber, 0);
                                float remainingNormalized = Mathf.Min(remaining, totalChunks) / (float)totalChunks;

                                // NEW: Update the persistent state history with the new measurements.
                                UpdateDqnStateHistory(lastQualityNormalized, bufferNormalized, throughputNormalized, delayNormalized, nextChunkSizesNormalized, remainingNormalized);

                                // Copy the persistent state history into a 3D tensor.
                                float[,,] dqnStateToPass = new float[1, 6, 8];
                                for (int i = 0; i < 6; i++)
                                {
                                    for (int j = 0; j < 8; j++)
                                    {
                                        dqnStateToPass[0, i, j] = dqnStateHistory[i, j];
                                    }
                                }

                                float[] eyeData = new float[48*26];
                                float[] lipData = new float[48*12];
                                float[] headData = new float[48*6];

                                eyeData = eyeQoEMetricsLogger.GetPreprocessedEyeData(eyeSegment);
                                lipData = eyeQoEMetricsLogger.GetLipProcessedData(eyeSegment);
                                headData = eyeQoEMetricsLogger.GetProcessedHeadData(eyeSegment);
                                eyeSegment += 1;



                                decisionStopwatch.Restart();
                                startTimeLatency = Time.time;
                                int selectedAction = dqnAgent.SelectAction(dqnStateToPass, eyeData, lipData, headData);
                                if (selectedAction < 0 || selectedAction >= bitrates.Length)
                                {
                                    Debug.LogWarning($"Invalid action selected: {selectedAction}. Defaulting to 0.");
                                    selectedAction = 0;
                                }
                                action = selectedAction;
                            }
                            catch (System.Exception dqnEx)
                            {
                                Debug.LogError($"Error during DQN SelectAction: {dqnEx.Message}");
                                action = bitrates.FirstOrDefault();
                            }
                            decisionStopwatch.Stop();
                            endTimeLatency = Time.time;
                            Debug.Log($"DQN Decision for Segment {segmentNumber}: Action={action}, Decision Latency={(endTimeLatency - startTimeLatency):F3} sec");
                        }
                        else
                        {
                            dqnAgent =FindObjectOfType<SimpleDQNInference>();
                            Debug.LogError("DQN agent is null! Cannot make DQN decisions.");
                        }
                       

                    }
                    
                    else if (activeABR == "random")
                    {
                        Debug.Log("---->>>> debug 19 User set ABR: Random ABR decision.");
                        decisionStopwatch.Restart();
                        startTimeLatency = Time.time;
                        action = bitrates[Rand.Next(bitrates.Length)];
                        endTimeLatency = Time.time;
                        decisionStopwatch.Stop();
                    }
                    else
                    {
                        File.AppendAllText(latencyLogFile, $"Unknown ABR type: {activeABR}\n");
                        action = 0;
                        decisionStopwatch.Stop();
                        endTimeLatency = startTimeLatency;
                    }

                    float decisionTime = (float)decisionStopwatch.Elapsed.TotalMilliseconds;
                    File.AppendAllText(latencyLogFile, $"Segment {segmentNumber} Decision Time: {decisionTime} ms | ABR: {activeABR}\n");
                    string readableStart = ConvertTimeToReadable(startTimeLatency);
                    string readableEnd = ConvertTimeToReadable(endTimeLatency);
                    File.AppendAllText(latencyLogFile, $"Decision Start: {readableStart}, Decision End: {readableEnd}\n");
                }

                Debug.Log("---->>>> debug 19 Deciison Made:" + " ABR "  + activeABR +  " Segment " + segmentNumber + " selected bitrate: " + action + " kbps");
 
              
                 

                // Segment download
                string segmentUrl = "";
                int actionIndex = 0;
                try{
                 actionIndex = 0;
                if(action>=bitrates[0]){
                try{
                for (int i = 0; i < bitrates.Length; i++)
                {
                    if (bitrates[i] == action)
                    {
                        actionIndex = i;
                        break;
                    }
                }
                }
                catch (Exception e)
                {
                    Debug.Log("Error in setting index by using bitrate from action in segemnt fetcher: " + e.Message);
                }
                }
                else{
                    actionIndex = action;
                }

                SendPing("Segment Fetcher:  Playing Segment " + segmentNumber + " with ABR " + activeABR );
                // actionIndex = currentQualityIndex;
                Debug.Log($"---->>>> debug 19 index debug Segment {segmentNumber} Decision Index: {actionIndex} representation count: {representations.Count}");
                Representation rep = representations[actionIndex];  // Select representation based on the action
                
               
               
     streamURLPrefix = baseUrl_public;
     video_segment = segmentNumber%16;
     if(activeABR == "dqn"){
        video_segment = segmentNumber%151;
     }
        if(video_segment==0){
            video_segment = 1;
                                string prefix = "";
                    string urlSuffix = "";
                  
for (int i = baseUrl_public.Length - 1; i >= 0; i--)
{
    if (char.IsDigit(baseUrl_public[i]) && baseUrl_public[i-1] == 'o')
    { 
        prefix = baseUrl_public.Substring(0, i);
        while(char.IsDigit(baseUrl_public[i]))
        {
            i++;
        }
        urlSuffix = baseUrl_public.Substring(i);
        break;
    }
}
int nextId = videoMaster.NextVideoID();
if(nextId == -1){
    Debug.Log("---->>>> debug 19 No more videos to play in group after this index. Stopping the fetch process.");
    fetchedAllSegments = true;
    break;
}
string nextIDString = nextId.ToString();
        
        streamURLPrefix = prefix + nextIDString + urlSuffix;
        }
        
segmentUrl = streamURLPrefix + rep.Media.Replace("$RepresentationID$", rep.Id)
               .Replace("$Number$", video_segment.ToString())
               .Replace(".mp4", (GetPlatformCode() == 'L' ? ".webm" : ".mp4"));
                baseUrl_public = streamURLPrefix;
                }
                catch (System.Exception e)
                {
                    Debug.Log("----->>>>> Debug 19 Error while creating download url: " + e.Message);
                }
                try{
                
                }
                catch (System.Exception e)
                {
                    Debug.Log("----->>>>> Debug 19 Error while creating download url: " + e.Message);
                }
                past_bitrates.Add(actionIndex);
                Debug.Log("---->>>> debug 19 Will try to download segment from: " + segmentUrl);
                bool success = false;
                float startTime = Time.time;
                downloadStopwatch.Restart();
                yield return StartCoroutine(DownloadSegment(segmentUrl, segmentNumber, (result) => success = result));
                downloadStopwatch.Stop();
                float endTime = Time.time;
                string filePath = Path.Combine("/home/mobisec/Desktop/optiplex/pensive-PyTorch-Temp/temp/Videos", "segment" + segmentNumber + ".webm");
                if (GetPlatformCode() == 'W' || GetPlatformCode() == 'A')
                {
                    filePath = Path.Combine(Application.persistentDataPath, "segment" + segmentNumber + ".mp4");
                }
                Debug.Log("---->>>> debug 19 Segment Download function exited. download time: " + (endTime - startTime) + " seconds.");
                float segmentSize = new FileInfo(filePath).Length * 8; // in bits
                if(action>=2000){
                    past_bitrates.Add(action);
                }
                else{
                    for(int i=0;i<bitrates.Length;i++){
                        if(action==bitrates[i]){
                            past_bitrates.Add(i);
                            break;
                        }
                    }
                }
                

                float downloadTimeX = (float)downloadStopwatch.Elapsed.TotalSeconds;
                lastDownloadDelay = downloadTimeX;
                float throughput_bps = segmentSize / downloadTimeX;  // bits/s
                float throughput_kbps = throughput_bps / 1000f;      // convert to kbps
                past_throughputs.Add(throughput_kbps);
                 float throughput =  throughput_kbps;
                File.AppendAllText(latencyLogFile, $"Segment {segmentNumber} Throughput: {throughput_kbps / 1e6:F2} Kbps (calculated)\n");

                past_buffer_levels.Add(dashVideoPlayer.GetBufferInSeconds());

                // string bufferFilePath = Path.Combine(Application.persistentDataPath, $"bufferLogger_{currentVideoID}_{activeABR}.txt");
                // File.AppendAllText(bufferFilePath, $"Segment {segmentNumber} Buffer Size: {bufferManager.GetBufferSize()}\n");

                string ThroughputFilePath = Path.Combine(Application.persistentDataPath, $"Throughput_{currentVideoID}_{activeABR}.txt");
                float prev_throughput = past_throughputs.Count > 0 ? past_throughputs[past_throughputs.Count - 1] : -1f;
                File.AppendAllText(ThroughputFilePath, $"Segment {segmentNumber} Throughput: {prev_throughput}\n");
                int chosenBitrate = (action)<2000? bitrates[actionIndex] : action;
                File.AppendAllText(Path.Combine(Application.persistentDataPath, $"bitrate_{currentVideoID}_{activeABR}.txt"), $"Segment {segmentNumber} Bitrate: {chosenBitrate} kbps\n");

                // string arrivalTimeTextFile = Path.Combine(Application.persistentDataPath, $"Session{videoMaster.currentGroupIndex*100+videoMaster.currentGroupVideoIndex}_{activeABR}ArrivalTime.txt");
                // File.AppendAllText(arrivalTimeTextFile, $"Segment {segmentNumber - 1} Arrival Time: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fffffff")}\n");

                float downloadTime = downloadTimeX;
                avgLatency += downloadTime;
                avgLatency /= count;
                count += 1.0f;
                // File.AppendAllText(latencyLogFile, $"Segment {segmentNumber} Latency: {downloadTime} ms\n");
                // // File.AppendAllText(latencyLogFile, $"Average Latency: {avgLatency} ms\n");

                try
                {
                    filePath = Path.Combine("/home/mobisec/Desktop/optiplex/pensive-PyTorch-Temp/temp/Videos", "segment" + segmentNumber + ".webm");
                    if (GetPlatformCode() == 'W' || GetPlatformCode() == 'A')
                    {
                        filePath = Path.Combine(Application.persistentDataPath, "segment" + segmentNumber + ".mp4");
                    }
                    Debug.Log($"--->>> debug 19 segment number + {segmentNumber} + throughput + {throughput}");
                } 
                catch (System.Exception e)
                {
                    Debug.Log("Error: " + e.Message);
                }

                if (!success)
                {
                    Debug.Log("---->>>> Debug 19 No more segments to fetch. Stopping the fetch process.");
                    break;
                }

                segmentNumber++;
                if (segmentNumber > RandomizedSegments && activeABR == "dqn" && eyeQoEMetricsLogger.segmentNumber > 1)
                {
                    Debug.Log("---->>>> debug 19 Waiting for at least one eye data to be filled for dqn to fetch next segment");
                    yield return new WaitForSeconds(0.1f);
                }
                yield return new WaitForSeconds(0.005f);
            }
            else
            {
                Debug.Log("Buffer is full. Waiting for space.");
                yield return new WaitForSeconds(0.1f);
            }
        }
        
    }

    private IEnumerator DownloadSegment(string url, int segmentNumber, System.Action<bool> callback)
{
    // Determine the file path for saving the segment.
    string filePath = Path.Combine("/home/mobisec/Desktop/optiplex/pensive-PyTorch-Temp/temp/Videos", "segment" + segmentNumber + ".webm");
    if (GetPlatformCode() == 'W' || GetPlatformCode() == 'A')
    {
        filePath = Path.Combine(Application.persistentDataPath, "segment" + segmentNumber + ".mp4");
    }
    
    string currentUrl = url;
    
    while (true)
    {
        using (UnityEngine.Networking.UnityWebRequest webRequest = UnityEngine.Networking.UnityWebRequest.Get(currentUrl))
        {
            yield return webRequest.SendWebRequest();
            
            if (webRequest.responseCode == 404)
            {
                Debug.Log($"Segment {segmentNumber} not found (404) at: {currentUrl}");
                
                // Extract the suffix after the video ID.
                // Assume currentUrl is structured as: baseUrl_public + [digits] + suffix.
//                 baseUrl_public = currentUrl;
//                 int baseLen = baseUrl_public.Length;
//                 int index = baseLen;
//                 while (index < currentUrl.Length && char.IsDigit(currentUrl[index]))
//                 {
//                     index++;
//                 }
//                 string urlSuffix = (index < currentUrl.Length) ? currentUrl.Substring(index) : "";
                
//                 // Get the next video id from the master.
//                 int nextId = videoMaster.NextVideoID();
//                 if (nextId == -1)
//                 {
//                     Debug.Log("No next video ID available; group ended.");
//                     fetchedAllSegments = true;
//                     dashVideoPlayer.InputTracker_EndPlayback();
//                     callback(false);
//                     yield break;
//                 }
//                 else
//                 {
//                     string prefix = "";
//                     urlSuffix = "";
                  
// for (int i = baseUrl_public.Length - 1; i >= 0; i--)
// {
//     if (char.IsDigit(baseUrl_public[i]) && baseUrl_public[i-1] == 'o')
//     { 
//         prefix = baseUrl_public.Substring(0, i);
//         while(char.IsDigit(baseUrl_public[i]))
//         {
//             i++;
//         }
//         urlSuffix = baseUrl_public.Substring(i);
//         break;
//     }
// }

// string nextIDString = nextId.ToString();
// baseUrl_public = prefix + nextIDString+"/";
// Debug.Log($"--->> set new base url: {baseUrl_public}");

//                     currentUrl = prefix + nextIDString + urlSuffix;

//                     Debug.Log($"Trying alternative URL: {currentUrl}");
//                     continue; // Retry download with new URL.
//                 }
                      Debug.LogError("Segment fetch error: " + webRequest.error);
                fetchedAllSegments = true;
                callback(false);
                yield break;

            }
            else if (webRequest.result == UnityEngine.Networking.UnityWebRequest.Result.ConnectionError ||
                     webRequest.result == UnityEngine.Networking.UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("Segment fetch error: " + webRequest.error);
                fetchedAllSegments = true;
                callback(false);
                yield break;
            }
            else
            {
                Debug.Log($"Download succeeded for segment {segmentNumber} from {currentUrl}");
                File.WriteAllBytes(filePath, webRequest.downloadHandler.data);
                bufferManager.AddToBuffer(filePath);
                lastSegmentDownloaded = segmentNumber;
                callback(true);
                yield break;
            }
        }
    }
}


    public static char GetPlatformCode()
    {
    #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        return 'W'; // Windows
    #elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        return 'L'; // Linux
    #elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        return 'M'; // macOS
    #elif UNITY_ANDROID
        return 'A'; // Android
    #elif UNITY_IOS
        return 'I'; // iOS
    #else
        return 'U'; // Unknown or unsupported platform
    #endif
    }

    public void cleanup()
    {
        past_throughputs.Clear();
        past_buffer_levels.Clear();
        past_bitrates.Clear();
        decisionArray.Clear();
        segmentNumber = 1;
        fetchedAllSegments = false;
        Destroy(rateBasedABRController);
        Destroy(mpegdashABRController);
        Destroy(pensiveABR);
        Destroy(dqnAgent);
        Destroy(bolaABRController);
        bufferManager.ClearBuffer();

    }
}
