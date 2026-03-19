using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System;
using Random = System.Random;
using System.Diagnostics; 
using Debug = UnityEngine.Debug;


 

public class DashVideoPlayer : MonoBehaviour
{
    private VideoPlayer videoPlayer1;
    private VideoPlayer videoPlayer2;
    private RenderTexture renderTexture1;
    private RenderTexture renderTexture2;
    private int startedInputTracking = 0;
    private Material skyboxMaterial;
    private float switchTimeBeforeEnd = 0f; // Default time in seconds before the video ends to switch
    private DashVideoMaster videoMaster; // Reference to the master script
    private int lowestIndexAvaialable = 1;
    private EyeQoEMetricsLogger eyeQoEMetricsLogger;
    private InputTracker inputTracker;
    // UI Elements
    private MPDParser mpdParser;
    private SegmentFetcher segmentFetcher;
    private BufferManager bufferManager;
    private ABRAlgorithm abrAlgorithm;
    public int segmentNumber = 1;
    private int videoIndex=0;
    public int currentVideoID = 0;
    private int startedTrackingeye = 0;
    public int qualityIndex = 0;
    private float starttemp =0;
    private float endtemp = 4;
    private int reburringCount=0;
    private float count_of_rebuufer_for_avg = 0.0f;
    private bool isRebuffering = false;
    private float rebufferingTime = 0;
    private bool XSecondsInitiated = false;
    private List<int> reburringIntervalIndexes = new List<int>();
    public bool streamended = false;
    private string rebufferingLog = "";
    private int maxBufferCapacity = 2;
    private int MaxSegments = 15;
    private Stopwatch rebufferStopwatch;
    public string activeABR = "random";
    public float XSecondsToEnd = 240f; // Default time in seconds to end the video


    public void Initialize(Material skyboxMat, DashVideoMaster master)
    {
        skyboxMaterial = skyboxMat;
        videoMaster = master;
        int height = 2048;
        int width = 4096;
        
        
        // Create video players and render textures at runtime
        videoPlayer1 = gameObject.AddComponent<VideoPlayer>();
        videoPlayer2 = gameObject.AddComponent<VideoPlayer>();
        renderTexture1 = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32);
        renderTexture2 = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32);
         renderTexture1.filterMode = FilterMode.Bilinear;
         renderTexture2.filterMode = FilterMode.Bilinear;

        videoPlayer1.targetTexture = renderTexture1;
        videoPlayer2.targetTexture = renderTexture2;
        
        skyboxMaterial.mainTexture = renderTexture1;

        videoPlayer1.playOnAwake = false;
        videoPlayer2.playOnAwake = false;
        videoPlayer1.isLooping = false;
        videoPlayer2.isLooping = false;

        // Test
        videoPlayer1.skipOnDrop = true;
        videoPlayer2.skipOnDrop = true;

        mpdParser = new MPDParser();
        segmentFetcher = gameObject.AddComponent<SegmentFetcher>();
        bufferManager = BufferManager.Instance;
       
        eyeQoEMetricsLogger = gameObject.AddComponent<EyeQoEMetricsLogger>();
        inputTracker = gameObject.AddComponent<InputTracker>();
         
        abrAlgorithm = new ABRAlgorithm();
        int currentGroupIndex = videoMaster.currentGroupIndex;
        
        // if(videoMaster.currentGroupIndex == 0){
        //     segmentFetcher.activeABR = "dqn";
        //     bufferManager.SetBufferSize(8);
        // }
        // else if(videoMaster.currentGroupIndex == 1){
        //     segmentFetcher.activeABR = "pensive";
        //     bufferManager.SetBufferSize(15);
        // }
        // else if(videoMaster.currentGroupIndex == 2){
        //     segmentFetcher.activeABR = "mpc";
        //     bufferManager.SetBufferSize(15);
        // }
        // else if(videoMaster.currentGroupIndex == 3){
        //     segmentFetcher.activeABR = "bola";
        //     bufferManager.SetBufferSize(15);
        // }
        
        // else if(videoMaster.currentGroupIndex == 4){
        //     segmentFetcher.activeABR = "rate";
        //     bufferManager.SetBufferSize(15);
        // }
        string activeABR = segmentFetcher.activeABR;
        rebufferingLog = Path.Combine(Application.persistentDataPath, $"rebufferingLog{currentVideoID}_{currentGroupIndex}_{activeABR}.txt");
        
    }

    public void PlayVideo(string mpdURL, float switchTime=0.2f, int videoID=0)
    {
        switchTimeBeforeEnd = switchTime;
        currentVideoID = videoID;
        StartCoroutine(SetupVideoPlayer(mpdURL, videoID));
    }

    private IEnumerator SetupVideoPlayer(string mpdURL, int videoID)
    {
        // Reinitialize the variables
        segmentNumber = 1;
        startedTrackingeye = 0;
        
        videoIndex = 0;
        starttemp = 0;
        endtemp = 4;

        videoPlayer1.Stop();
        videoPlayer2.Stop();

        videoPlayer1.targetTexture = renderTexture1;
        videoPlayer2.targetTexture = renderTexture2;
        skyboxMaterial.mainTexture = renderTexture1;

        yield return StartCoroutine(DeleteAllExistingVideos());

        string[] files = GetVideoFiles();
        Debug.Log("-->>>>> Files Found : " + files.Length + " All deleted, continuing process");
        yield return StartCoroutine(mpdParser.FetchMPD(mpdURL));
        var representations = mpdParser.GetRepresentations();
        segmentFetcher.fetchedAllSegments = false;
        Debug.Log("---->>>> debug 19 Quality Index: " + qualityIndex);
        abrAlgorithm.representationIndex = qualityIndex;
        segmentFetcher.streamURLPrefix = GetStreamURLPrefix(mpdURL);
        Debug.Log("---->>>> debug 19 Stream URL Prefix: " + segmentFetcher.streamURLPrefix);
        StartCoroutine(segmentFetcher.FetchSegments(representations, videoID, qualityIndex));
        activeABR = segmentFetcher.activeABR;
        
        int currentGroupIndex = videoMaster.currentGroupIndex;
        int currentGroupVideoIndex = videoMaster.currentGroupVideoIndex;
        rebufferingLog = Path.Combine(Application.persistentDataPath, $"rebufferingLog{currentGroupVideoIndex}_{currentGroupIndex}_{activeABR}.txt");
        setRandomRebufferingIntervals();
         
        rebufferingLog = Path.Combine(Application.persistentDataPath, $"rebufferingLog{currentVideoID}_{currentGroupIndex}_{activeABR}.txt");
        // Prepare 2 video players in parallel
        
        StartCoroutine(PrepareSegment(videoPlayer1));
    }
    
    private IEnumerator PrepareSegment(VideoPlayer videoPlayer)
    {
        Debug.Log("---->>>> debug 19 Prepare segment function fired");
        int temp = 10;
        while (temp == 10)
        {
            string filePath = Path.Combine("/home/mobisec/Desktop/optiplex/pensive-PyTorch-Temp/temp/Videos", "segment" + segmentNumber + ".webm");
            if (GetPlatformCode() == 'W' || GetPlatformCode() == 'A')
                filePath = Path.Combine(Application.persistentDataPath, "segment" + segmentNumber + ".mp4");

            Debug.Log($" ----->>>> debug 19 Processing Video: {filePath}");
           

            if (CheckIfFileExists(filePath))
            {
                Debug.Log("----->>>> debug 19 Prepared started for segment " + segmentNumber);
                videoPlayer.url = "file://" + filePath;
                bufferManager.RemoveFromBuffer();
                videoPlayer.errorReceived += HandleVideoError;
                videoPlayer.prepareCompleted += PrepareCompleted;
                videoPlayer.Prepare();
                temp = 1;

                yield return null;
            }
            else if (segmentFetcher.fetchedAllSegments)
            {
                
                yield break;
            }
            else
            {
                rebufferingTime+=0.01f;
                count_of_rebuufer_for_avg+=1.0f;

                yield return new WaitForSeconds(0.01f);
            }
        }
    }

    private void PrepareCompleted(VideoPlayer videoPlayer)
{
    
    if (segmentNumber % 2 != 0)
    {
        videoPlayer = videoPlayer1;
    }
    else
    {
        videoPlayer = videoPlayer2;
    }

    try
    {
        if (videoPlayer == videoPlayer1)
        {
            
            videoPlayer1.targetTexture = renderTexture1;
            
         
        }
        else
        {
           
            videoPlayer2.targetTexture = renderTexture2;
           
        }
    }
    catch (Exception e)
    {
        Debug.Log("---->>>> debug 19 Exception while trying to set new render texture to material: " + e.Message);
    }

    
    if ((videoPlayer == videoPlayer1 && videoPlayer2.isPlaying) ||
        (videoPlayer == videoPlayer2 && videoPlayer1.isPlaying))
    {
        videoPlayer.Pause();
        
        if (videoPlayer == videoPlayer1)
        {
            StartCoroutine(StartPlaybackAfterWait(videoPlayer1, videoPlayer2));
        }
        else
        {
            StartCoroutine(StartPlaybackAfterWait(videoPlayer2, videoPlayer1));
        }
        return;
    }

    // If the other is not playing, start playback immediately.
    StartPlayback(videoPlayer);
}


private void StartPlayback(VideoPlayer vp)
{
    

    Debug.Log("---->>>> debug 19 Playing video: " + segmentNumber);
    vp.Play();
    if (activeABR == "random" || activeABR == "dqn" || activeABR == "test")
    {
        eyeQoEMetricsLogger.IncreaseSegmentNumber();
    }
    if(vp == videoPlayer1){
        skyboxMaterial.mainTexture = renderTexture1;
    }
    else{
        skyboxMaterial.mainTexture = renderTexture2;
    }
  
    vp.Play();
    ++segmentNumber;
      vp.prepareCompleted -= PrepareCompleted;
    vp.loopPointReached += OnVideoEnded;
    
    StartCoroutine(PrepareNextSegment(vp));
    if (XSecondsInitiated == false)
    {
        XSecondsInitiated = true;
        CheckIfXSecondsPassedAndEndPlayback(XSecondsToEnd);
    }
    if (activeABR == "random" || activeABR == "dqn" || activeABR == "test")
    {
        if (startedTrackingeye == 0)
        {
            startedTrackingeye = 1;
            eyeQoEMetricsLogger.StartLoggingEverySecond();
        }
    }
    if (startedInputTracking == 0)
    {
        startedInputTracking = 1;
        inputTracker.StartTracking();
    }
  
    
}


private IEnumerator StartPlaybackAfterWait(VideoPlayer currentPlayer, VideoPlayer otherPlayer)
{
    // Wait until the other video player is no longer playing.
    while (otherPlayer.isPlaying)
    {
        yield return null;
    }

    
    // Continue with playback as usual.
    StartPlayback(currentPlayer);
}


    private IEnumerator PrepareNextSegment(VideoPlayer videoPlayer)
    {

        Debug.Log("---->>>> debug 19 Prepare next segment function fired: " + segmentNumber);
        double timeRemaining = videoPlayer.length - videoPlayer.time;
        Debug.Log($"-->>>> debug 19  Time remaining: {timeRemaining} seconds");
        float rebufferTime = 0f;

        int temp = 100;
        while (temp == 100)
        {
            string nextFilePath = Path.Combine("/home/mobisec/Desktop/optiplex/pensive-PyTorch-Temp/temp/Videos", "segment" + segmentNumber + ".webm");
            if (GetPlatformCode() == 'W' || GetPlatformCode() == 'A')
                nextFilePath = Path.Combine(Application.persistentDataPath, "segment" + segmentNumber + ".mp4");

            Debug.Log($"--->>>> debug 19 Processing Next Segment: {nextFilePath}");
          

            if (CheckIfFileExists(nextFilePath))
            {
                Debug.Log("---->>>> debug 19 Prepared started for next segment " + segmentNumber);
                if(isRebuffering){
                    isRebuffering = false;
                    rebufferStopwatch.Stop(); // Stop the stopwatch
                    float rebufferDuration = (float)rebufferStopwatch.Elapsed.TotalSeconds; // Convert elapsed time to seconds
                    File.AppendAllText(rebufferingLog, $"[Rebuffer End] Segment: {segmentNumber}, Duration: {rebufferDuration:F4}s, Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n");
                    rebufferingTime += rebufferDuration;
                    count_of_rebuufer_for_avg += 1.0f;
                    File.AppendAllText(rebufferingLog, $"[Avg Rebuffer Time] {rebufferingTime / count_of_rebuufer_for_avg:F4}s\n");
                }
                if (videoPlayer == videoPlayer1)
                {
                    videoPlayer2.url = nextFilePath;
                    bufferManager.RemoveFromBuffer();
                    videoPlayer2.prepareCompleted += PrepareCompleted;
                    videoPlayer2.Prepare();
                }
                else
                {
                    videoPlayer1.url = nextFilePath;
                    bufferManager.RemoveFromBuffer();
                    videoPlayer1.prepareCompleted += PrepareCompleted;
                    videoPlayer1.Prepare();
                }
                temp = 1;
                yield return null;
            }
            else if (segmentFetcher.fetchedAllSegments)
            {
                Debug.Log("---->>>> debug 19 In prepare next segemnt All segments fetched. Exiting.");
                
                yield break;
            }
            else
            {
              if (!videoPlayer1.isPlaying && !videoPlayer2.isPlaying)
{
    if (!isRebuffering)
    {
        isRebuffering = true;
        rebufferStopwatch = Stopwatch.StartNew(); // Start the stopwatch
        File.AppendAllText(rebufferingLog, $"[Rebuffer Start] Segment: {segmentNumber}, Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n");
    }

    yield return new WaitForSeconds(0.01f); // Wait before checking again
}
else
{
    if (isRebuffering)
    {
        isRebuffering = false;
        rebufferStopwatch.Stop(); // Stop the stopwatch
        float rebufferDuration = (float)rebufferStopwatch.Elapsed.TotalSeconds; // Convert elapsed time to seconds
        File.AppendAllText(rebufferingLog, $"[Rebuffer End] Segment: {segmentNumber}, Duration: {rebufferDuration:F4}s, Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n");
        rebufferingTime += rebufferDuration;
        count_of_rebuufer_for_avg += 1.0f;
        File.AppendAllText(rebufferingLog, $"[Avg Rebuffer Time] {rebufferingTime / count_of_rebuufer_for_avg:F4}s\n");
    }
}

                yield return new WaitForSeconds(0.01f);
            }
        }
    }

    public void CheckIfXSecondsPassedAndEndPlayback(float seconds)
    {
        StartCoroutine(CheckIfXSecondsPassedAndEndPlaybackCoroutine(seconds));
    }

    private IEnumerator CheckIfXSecondsPassedAndEndPlaybackCoroutine(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        StartCoroutine(EndPlayback(true));
        yield return null;
    }

    private void OnVideoEnded(VideoPlayer videoPlayer)
{
    ++videoIndex;
    
    Debug.Log("---->>>> debug 19 Video ended, segment: " + (segmentNumber - 1));
    File.AppendAllText(Path.Combine(Application.persistentDataPath, $"Session{(videoMaster.currentGroupIndex*100 + videoMaster.currentGroupVideoIndex)}_{segmentFetcher.activeABR}FinishTime.txt"), $"Segment {segmentNumber - 1} Finish Time: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fffffff")}\n");
    File.AppendAllText(Path.Combine(Application.persistentDataPath, $"Session{currentVideoID}.csv"), $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fffffff")}, {videoPlayer.time}\n");
   
    videoPlayer.loopPointReached -= OnVideoEnded;
   
    string filePath = Path.Combine("/home/mobisec/Desktop/optiplex/pensive-PyTorch-Temp/temp/Videos", "segment" + (segmentNumber - 1) + ".webm");
    if (GetPlatformCode() == 'W' || GetPlatformCode() == 'A')
        filePath = Path.Combine(Application.persistentDataPath, "segment" + (segmentNumber - 1) + ".mp4");
    
    
    if (File.Exists(filePath))
    {
        File.Delete(filePath);
        Debug.Log("---->>>> debug 19 Video file deleted: " + filePath);
    }
    if(segmentFetcher.lastSegmentDownloaded <= segmentNumber){
        Debug.Log("---->>>> debug 19 Last segment downloaded. Ending playback.");
        StartCoroutine(EndPlayback());
    }
    

        
}

        private IEnumerator DeleteAllExistingVideos()
    {
        string[] files = GetVideoFiles();
        Debug.Log("-->>>>> Files Found : " + files.Length + " Deleting...");
        foreach (string file in files)
        {
            File.Delete(file);
        }
        yield return null;
    }

    private string[] GetVideoFiles()
    {
        if (GetPlatformCode() == 'L')
            return Directory.GetFiles("/home/mobisec/Desktop/optiplex/pensive-PyTorch-Temp/temp/Videos", "*.webm");
        if (GetPlatformCode() == 'W' || GetPlatformCode() == 'A')
            return Directory.GetFiles(Application.persistentDataPath, "*.mp4");

        return new string[0];
    }

    private bool CheckIfFileExists(string filePath)
    {
        // using buffer manager to check if the file exists
        Debug.Log("---->>>> debug 19 Checking if file exists: " + filePath);
        Debug.Log("---->>>> debug 19 Buffer size: " + bufferManager.GetBufferSize());
        bufferManager.printQueue();
        if(bufferManager.GetBufferSize() ==0 && segmentFetcher.fetchedAllSegments && segmentNumber>=MaxSegments-1){
            Debug.Log("---->>>> debug 19 Buffer is empty and all segments have been fetched. Exiting dash player.");
            StartCoroutine(EndPlayback());
            
        }
        Debug.Log("----->>>>>> debug 19 Segment fetcher all fetched status : " + segmentFetcher.fetchedAllSegments);
        if(File.Exists(filePath) && bufferManager.isSegmentFirstInBuffer(filePath)){
            Debug.Log("---->>>> debug 19 File exists and is first in buffer: " + filePath);
        }
        return File.Exists(filePath) && bufferManager.isSegmentFirstInBuffer(filePath);
    }

    public void InputTracker_EndPlayback()
    {
        StartCoroutine(EndPlayback());
    }
    private IEnumerator EndPlayback(bool XSecondsPassed = false)
    {
        if(XSecondsPassed){
            Cleanup();
            DeleteAllExistingVideosCleanUp();
            videoMaster.OnVideoEnded(true);
            yield break;
        }
        if(videoPlayer1.isPlaying || videoPlayer2.isPlaying){
            Debug.Log("---->>>> debug 19 File does not exist but video is playing. Waiting for video to end.");
            yield break;
        }
        if(segmentFetcher.lastSegmentDownloaded > segmentNumber){
            Debug.Log("---->>>> debug 19 Still segments exist in memory to be played. Waiting for them to be played.");
            yield break;
        }
        streamended = true;
            Debug.Log("---->>>> debug 19 test 1 File does not exist and all segments have been fetched. Stopping the video player.");
            Debug.Log("---->>>> debug 19 input queue : " + inputTracker.inputQueue.Count + " , segment number: " + inputTracker.trackQueueSize);
            Debug.Log("---->>>> debug 19 checking if all inputs are given by user");
            
             if(segmentFetcher.fetchedAllSegments || XSecondsPassed){
                Debug.Log("---->>>> debug 19 All inputs logged. exiting dash player.");
                
                Cleanup();
                DeleteAllExistingVideosCleanUp();
                videoMaster.OnVideoEnded();
               
                yield break;
            }
        
             
    }
    

    void setRandomRebufferingIntervals(){
        reburringIntervalIndexes.Clear();
        var rand = new Random();

        if(qualityIndex == 0){
            reburringCount = rand.Next(0,1);
        }
        else if(qualityIndex == 1){
            reburringCount = rand.Next(2,5);
        }
        else{
            reburringCount = rand.Next(5,7);
        }
        Debug.Log("---->>>> debug 19 Rebuffering count: " + reburringCount);
        for(int i=0;i<reburringCount;i++){
            reburringIntervalIndexes.Add(rand.Next(1,MaxSegments));
        }
    }

    public int GetBufferInSeconds(){
        int bufferSize = bufferManager.GetBufferSize();
        int bufferSeconds = (bufferSize-1) * 4;
        if(videoPlayer1.isPlaying){
            bufferSeconds += (int)videoPlayer1.time;
        }
        if(videoPlayer2.isPlaying){
            bufferSeconds += (int)videoPlayer2.time;
        }
        return bufferSeconds;
        
    }

    void HandleVideoError(VideoPlayer vp, string message)
    {
        Debug.LogError("---->>> debug 19 Error playing video: " + message);
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

    private IEnumerator CheckIfPlayerIsPlayingAndNotify(VideoPlayer videoPlayer)
    {
        while (!videoPlayer.isPlaying)
        {
            yield return new WaitForSeconds(0.05f);
        }
        if(activeABR == "random" || activeABR == "dqn" || activeABR == "test")
        eyeQoEMetricsLogger.IncreaseSegmentNumber();
        
    }

    private string GetStreamURLPrefix(string mpdURL)
    {
        string[] parts = mpdURL.Split('/');
        string temp = string.Join("/", parts, 0, parts.Length - 1);
        return temp + "/";
    }

    public void DeleteAllExistingVideosCleanUp(){
        try{
        string[] files = GetVideoFiles();
        foreach (string file in files)
        {
            File.Delete(file);
        }
        }
        catch(Exception e){
            Debug.Log("---->>>> debug 19 Error while deleting files: " + e.Message);
        }

    }

    private void deleteLowest(){
        string path = Path.Combine("/home/mobisec/Desktop/optiplex/pensive-PyTorch-Temp/temp/Videos", "segment" + lowestIndexAvaialable + ".webm");
        if (GetPlatformCode() == 'W' || GetPlatformCode() == 'A')
            path = Path.Combine(Application.persistentDataPath, "segment" + lowestIndexAvaialable + ".mp4");
        
        while(segmentNumber - lowestIndexAvaialable>  maxBufferCapacity){
            if(File.Exists(path)){
                File.Delete(path);
                lowestIndexAvaialable++;
            }
            else{
                break;
            }
        
        }

    }

    public void Cleanup()
    {
        // Cleanup all resources and destroy components
        
        Debug.Log("---->>>> XXXXXXXX ->>> debug 19 Cleaning up DashVideoPlayer");
       
       if(activeABR == "random" || activeABR == "dqn" || activeABR == "test")
         eyeQoEMetricsLogger.StopLogging();
         segmentFetcher.cleanup();
        Destroy(videoPlayer1);
        Destroy(videoPlayer2);
        Destroy(renderTexture1);
        Destroy(renderTexture2);
        Destroy(segmentFetcher);

        Destroy(eyeQoEMetricsLogger);
        Destroy(inputTracker);
        
        
        

       
        

        // Set non-UnityEngine.Object references to null
        mpdParser = null;
        abrAlgorithm = null;

        // Notify the master script that cleanup is complete
        videoMaster.OnCleanupComplete();
        Destroy(this);
    }
}

