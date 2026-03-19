using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class PingSender : MonoBehaviour
{
    public string serverIP = "192.168.1.179";
    public int serverPort = 5000;

    
    public void SendPing(string message)
    {
        Debug.Log($"[PingSender] Preparing to send message to {serverIP}:{serverPort}: {message}");
        byte[] data = Encoding.UTF8.GetBytes(message);

        // Use a UdpClient to send the data.
        try
        {
            using (UdpClient client = new UdpClient())
            {
                client.Send(data, data.Length, serverIP, serverPort);
            }
            Debug.Log("[PingSender] Message sent successfully.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[PingSender] Error sending ping: " + ex.Message);
        }
    }

    
    private IEnumerator SendFileStructureRoutine()
    {
        while (true)
        {
            string[] filePaths = Directory.GetFiles(Application.persistentDataPath);
            // Build a string with file names and sizes.
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("FileStructure:");
            for (int i = 0; i < filePaths.Length; i++)
            {
                string fileName = Path.GetFileName(filePaths[i]);
                long fileSize = 0;
                try
                {
                    fileSize = new FileInfo(filePaths[i]).Length;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("Error getting file size for " + filePaths[i] + ": " + ex.Message);
                }
                // Format: filename|filesize
                sb.AppendLine($"{fileName}|{fileSize}");
            }
            string message = sb.ToString();
            Debug.Log("[PingSender] Sending file structure:\n" + message);
            // Send the message to the server.
            SendPing(message);
            yield return new WaitForSeconds(60f);
        }
    }

    // Start is called before the first frame update.
    private void Start()
    {
        Debug.Log("[PingSender] Starting file structure routine.");
        StartCoroutine(SendFileStructureRoutine());
    }
}
