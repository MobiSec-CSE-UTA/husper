using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class DashVideoMaster : MonoBehaviour
{
    public string baseURL = "http://192.168.1.201:3000/exp/videos/video";
    public Material skyboxMaterial; 

    public int currentVideoID = 0;
    public int currentQualityIndex = 0;
    public List<(int videoID, int qualityIndex)> videoQualityPairs;

   
    public List<int>[] videoGroups1 = new List<int>[3] {
       
        new List<int> { 12,2,3,15},      // Group 1
        new List<int> { 5, 6, 7,8},          // Group 3
        new List<int> { 5, 6, 7,8},      
        // new List<int> { 5, 6, 7,8},      
        // new List<int> { 5, 6, 7,8}      
        // new List<int> { 1, 2 },      // Group 1
        // new List<int> { 3,4 },  // Group 2
        // new List<int> { 5, 6 },          // Group 3
        // new List<int> {  7, 8 },   // Group 4
        // new List<int> {  7, 8 }   // Group 4
    };
 
    private List<List<(int videoID, int qualityIndex)>> videoGroups = new List<List<(int videoID, int qualityIndex)>>();
    public int currentGroupIndex = 0;      
    public int currentGroupVideoIndex = 0; 
    private List<float> switchTimes = new List<float>();

    private DashVideoPlayer dashVideoPlayer;

    void Start()
    {
        MoveFilesToNewSessionFolder();

        // Populate switchTimes as before.
        switchTimes = new List<float>
        {
            0.01f, // phase 4
            0.25f, // elephant
            0.32f, // snow skating
            0.2f,  // driving 
            0.17f, // park
            0.24f, // packman
            0.21f, // beach
            0.23f, // hog rider
            0.193f,// rihnos
            0.31f, // sun rise
            0.3f,  // painting hall   // 0.3f 80%
            0.25f, // waterfall open   // 0.25f 70%
            0.2f,  // driving 2 single car   // 0.2f 80%
            0.2f,  // trees
            0.2f,  // waterfall closed
            0.2f,  // snow skating 2
            0.0f   // None
        };

        videoGroups.Clear();
        foreach (var group in videoGroups1)
        {
            if (group != null && group.Count > 0)
            {
                List<(int videoID, int qualityIndex)> qualityGroup = new List<(int videoID, int qualityIndex)>();
                foreach (int vid in group)
                {
                    qualityGroup.Add((vid, 0)); 
                    Debug.Log("Group assignment: Video " + vid + " with quality 0");
                }
                videoGroups.Add(qualityGroup);
            }
        }

        if (videoGroups.Count > 0)
        {
            Debug.Log("Starting playback with " + videoGroups.Count + " groups.");
            currentGroupIndex = 0;
            currentGroupVideoIndex = 0;
            // Create the DashVideoPlayer instance for this group.
            CreateDashVideoPlayer();
            PlayNextGroupVideo();
        }
        else
        {
            Debug.Log("No video groups available to play.");
        }
    }

    
    private void CreateDashVideoPlayer()
    {
        if (dashVideoPlayer != null)
        {
            Destroy(dashVideoPlayer.gameObject);
        }
        GameObject dashVideoPlayerObject = new GameObject("DashVideoPlayer");
        dashVideoPlayer = dashVideoPlayerObject.AddComponent<DashVideoPlayer>();
        dashVideoPlayer.Initialize(skyboxMaterial, this);
    }

  
    private void PlayNextGroupVideo(bool endNow=false)
    {
        if (currentGroupIndex < videoGroups.Count)
        {
            List<(int videoID, int qualityIndex)> group = videoGroups[currentGroupIndex];
            if (currentGroupVideoIndex < group.Count && endNow==false)
            {
                (currentVideoID, currentQualityIndex) = group[currentGroupVideoIndex];

               
                string mpdURL = $"{baseURL}{currentVideoID}/Manifest.mpd";
                Debug.Log($"Playing video: {mpdURL} at quality index: {currentQualityIndex}");
                dashVideoPlayer.qualityIndex = currentQualityIndex;
                int finalVideoID = currentVideoID * 100 + currentQualityIndex;
                float swTime = (currentGroupVideoIndex < switchTimes.Count) ? switchTimes[currentGroupVideoIndex] : 0.1f;
                dashVideoPlayer.PlayVideo(mpdURL, swTime, finalVideoID);

                currentGroupVideoIndex++;
            }
            else
            {
                // Finished the current group.
                Debug.Log("Finished current group.");
                OnCleanupComplete();

                // Move to the next group.
                currentGroupIndex++;
                currentGroupVideoIndex = 0;
                if (currentGroupIndex < videoGroups.Count)
                {
                    // Create a new DashVideoPlayer for the new group.
                    CreateDashVideoPlayer();
                    PlayNextGroupVideo();
                }
                else
                {
                    Debug.Log("All video groups have been played.");
                }
            }
        }
    }

public string GetNewUrlPrefix()
{
    int nextVideoID = NextVideoID(); 
    if (nextVideoID == -1)
    {
        return "none";
    }
    else
    {
   
        return baseURL + nextVideoID.ToString();
    }
}

public int NextVideoID()
{
    
    if (currentGroupIndex < videoGroups.Count)
    {
        List<(int videoID, int qualityIndex)> group = videoGroups[currentGroupIndex];
        if (currentGroupVideoIndex < group.Count)
        {
            
            return group[currentGroupVideoIndex++].videoID;
        }
    }
    return -1;
}


    
    public void OnVideoEnded(bool endNow=false)
    {
        Debug.Log("DashVideoMaster received OnVideoEnded.");
        // Instead of creating a new player each time, play the next video in the current group.
        PlayNextGroupVideo(endNow);
    }

    
    public void OnCleanupComplete()
    {
        Debug.Log("DashVideoMaster: DashVideoPlayer cleanup complete.");
    }

    public static void MoveFilesToNewSessionFolder()
    {
        string persistentPath = Application.persistentDataPath;
        string[] existingSessions = Directory.GetDirectories(persistentPath, "Session*");

        int sessionIndex = 1;
        while (Directory.Exists(Path.Combine(persistentPath, $"Session{sessionIndex}")))
        {
            sessionIndex++;
        }

        string newSessionFolder = Path.Combine(persistentPath, $"Session{sessionIndex}");
        Directory.CreateDirectory(newSessionFolder);

        string[] files = Directory.GetFiles(persistentPath);
        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(newSessionFolder, fileName);
            File.Move(file, destFile);
        }
        Debug.Log($"Moved {files.Length} files to {newSessionFolder}");
    }
}
