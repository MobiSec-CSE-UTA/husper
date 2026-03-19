using UnityEngine;
using Unity.Barracuda;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public class SimpleDQNInference : MonoBehaviour
{
    [Tooltip("Assign your trained ONNX DQN model here.")]
    public NNModel modelAsset; // Set via Inspector

    private IWorker worker;

    // Define your bitrate options here
    public int[] bitrates = { 2000, 6000, 9500, 15000, 30000, 85000 };

    // Path for the debug log file
    private string debugLogFilePath;

    // Expected shape for the state input (for batch size 1): [1, 6, 8]
    private int[] stateShape = new int[] { 1, 6, 8,1 };


    private int[] eyeShape = new int[] { 1, 48, 26, 1 };       // Eye: 1x1x48x40
private int[] lipShape = new int[] { 1, 48, 12,1 };       // Lip: 1x1x48x37
private int[] headShape = new int[] { 1,  48, 6 , 1};      

    void Start()
    {
        debugLogFilePath = Path.Combine(Application.persistentDataPath, "dqnDebug.txt");
        InitializeDebugLog();

        if (modelAsset == null)
        {
            LogDebug("NNModel asset not assigned to SimpleDQNInference.");
            Debug.LogError("NNModel asset not assigned to SimpleDQNInference.");
            return;
        }

        try
        {
            // Load the ONNX model and create a worker of type Compute
            var runtimeModel = ModelLoader.Load(modelAsset);
            worker = WorkerFactory.CreateWorker(WorkerFactory.Type.CSharp, runtimeModel);

            LogDebug("DQN model loaded and worker created successfully.");
            Debug.Log("DQN model loaded and worker created successfully.");
        }
        catch (Exception ex)
        {
            LogDebug($"Failed to load DQN model: {ex.Message}");
            Debug.LogError($"Failed to load DQN model: {ex.Message}");
        }
    }

    /// <summary>
    /// Selects an action based on the provided state tensor.
    /// The state tensor should be of shape [1,6,8] and normalized as per the Python ABREnv.
    /// </summary>
    /// <param name="stateData">A 3D float array of shape [1,6,8] representing the state.</param>
    /// <returns>Selected action index.</returns>
    public int SelectAction(float[,,] stateData, float[] eyeData, float[] lipData, float[] headData)
    {
        // Flatten the 3D stateData into a 1D array
        int batch = stateData.GetLength(0);
        int dim1 = stateData.GetLength(1);
        int dim2 = stateData.GetLength(2);
        int totalLength = batch * dim1 * dim2;
        float[] flatState = new float[totalLength];
        int idx = 0;
        for (int i = 0; i < batch; i++)
        {
            for (int j = 0; j < dim1; j++)
            {
                for (int k = 0; k < dim2; k++)
                {
                    flatState[idx++] = stateData[i, j, k];
                }
            }
        }

        

        // Create a tensor from the flat state array with shape [1,6,8]
        // Create a tensor from the flat state array with shape [1,6,8,1]
Tensor stateTensor = new Tensor(new TensorShape(1,6,8,1), flatState);

        

        // Prepare the input dictionary. 
        // Ensure the key ("state") matches your ONNX model's input name.
        

        if (eyeData.Length != 48 * 26)
        {
            string errorMsg = $"Eye data must have 19200 elements (48x40). Current length: {eyeData.Length}";
            LogDebug(errorMsg);
            Debug.LogError(errorMsg);
            return 0; // Default action
        }
        if (lipData.Length != 48 * 12)
        {
            string errorMsg = $"Lip data must have 17760 elements (48x37). Current length: {lipData.Length}";
            LogDebug(errorMsg);
            Debug.LogError(errorMsg);
            return 0;
        }
        if (headData.Length != 48 * 6)
        {
            string errorMsg = $"Head data must have 2880 elements (48x6). Current length: {headData.Length}";
            LogDebug(errorMsg);
            Debug.LogError(errorMsg);
            return 0;
        }
        for(int i=0;i<5;i++)
        {
            Debug.Log("-->> debug 18 eye data in dqn : "+eyeData[i]);
        }
        for(int i=0;i<5;i++)
        {
            Debug.Log("-->> debug 18 lip data in dqn : "+lipData[i]);
        }
        for(int i=0;i<5;i++)
        {
            Debug.Log("-->> debug 18 head data in dqn : "+headData[i]);
        }
        for(int i=0;i<15;i++)
        {
            Debug.Log("-->> debug 18 state data in dqn : "+flatState[i]);
        }

        Tensor eyeTensor = new Tensor(eyeShape, eyeData);
        Tensor lipTensor = new Tensor(lipShape, lipData);
        Tensor headTensor = new Tensor(headShape, headData);
        try{
        // int[] newShape = {stateTensor.shape[0], stateTensor.shape[1], stateTensor.shape[2]};
        // Tensor t3D_manual = new Tensor(new TensorShape(1, 6, 8), flatState);
        }
        catch (Exception ex)
        {
            LogDebug($"Failed to reshape state tensor: {ex.Message}");
            Debug.LogError($"Failed to reshape state tensor: {ex.Message}");
            stateTensor.Dispose();
            return 0;
        }
        
        Debug.Log($"State Tensor shape: {string.Join("x", stateTensor.shape)}"); // Expect 1x6x8
        Debug.Log($"Eye Tensor shape: {string.Join("x", eyeTensor.shape)}");   // Expect 1x1x48x12
        Debug.Log($"Lip Tensor shape: {string.Join("x", lipTensor.shape)}");   // Expect 1x1x48x4
        Debug.Log($"Head Tensor shape: {string.Join("x", headTensor.shape)}"); // Expect 1x1x48x6
        Debug.Log($"State Tensor shape: {stateTensor.shape} (Expected: 1x6x8x1)");
Debug.Log($"Eye Tensor shape: {eyeTensor.shape} (Expected: 1x48x12x1)");
Debug.Log($"Lip Tensor shape: {lipTensor.shape} (Expected: 1x48x4x1)");
Debug.Log($"Head Tensor shape: {headTensor.shape} (Expected: 1x48x6x1)");

        var inputs = new Dictionary<string, Tensor>()
        {
            { "abr_state", stateTensor },
            { "eye", eyeTensor },
            { "lip", lipTensor },
            { "head", headTensor }
        };
        if(worker == null)
{
    LogDebug("Worker is null before execution.");
    Debug.LogError("Worker is null before execution.");
    
}

        try
        {
            worker.Execute(inputs);
            LogDebug("Model executed successfully in SelectAction.");
            Debug.Log("Model executed successfully in SelectAction.");
        }
        catch (Exception ex)
        {
            LogDebug($"Failed to execute the model: {ex.Message}");
            Debug.LogError($"Failed to execute the model: {ex.Message}");
            stateTensor.Dispose();
            return 0; // Default action
        }

        Tensor outputTensor = null;
        try
        {
            // Fetch the output tensor. (Ensure "q_values" is the correct output name in your model.)
            outputTensor = worker.PeekOutput("q_values");
            LogDebug("Output tensor fetched successfully.");
            Debug.Log("Output tensor fetched successfully.");
        }
        catch (Exception ex)
        {
            LogDebug($"Failed to fetch output tensor: {ex.Message}");
            Debug.LogError($"Failed to fetch output tensor: {ex.Message}");
            stateTensor.Dispose();
            return 0;
        }

        // Determine the action with the highest Q-value.
        float maxQValue = float.MinValue;
        int selectedAction = 0;
        for (int i = 0; i < outputTensor.length; i++)
        {
            float qVal = outputTensor[i];
            // Debug.Log($"Q-value for action {i}: {qVal}");
            if (qVal > maxQValue)
            {
                maxQValue = qVal;
                selectedAction = i;
            }
        }
        // Debug.Log($"--->>> Selected action: {selectedAction} with Q-value: {maxQValue}");
        // Debug.Log($"--->>> Corresponding bitrate: {bitrates[selectedAction]} kbps");

        // Dispose tensors to free memory.
        stateTensor.Dispose();
        outputTensor.Dispose();

        return selectedAction;
    }

    /// <summary>
    /// Initializes the debug log file.
    /// </summary>
    private void InitializeDebugLog()
    {
        try
        {
            if (!File.Exists(debugLogFilePath))
            {
                File.Create(debugLogFilePath).Dispose();
                LogDebug("Debug log file created.");
                Debug.Log("Debug log file created.");
            }
            else
            {
                LogDebug("Debug log file exists. Appending logs.");
                Debug.Log("Debug log file exists. Appending logs.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to initialize debug log file: {ex.Message}");
        }
    }

    /// <summary>
    /// Logs a message to the debug log file.
    /// </summary>
    private void LogDebug(string message)
    {
        try
        {
            File.AppendAllText(debugLogFilePath, $"{DateTime.Now}: {message}\n");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to write to debug log: {ex.Message}");
        }
    }

    void OnDestroy()
    {
        if (worker != null)
        {
            try
            {
                worker.Dispose();
                LogDebug("Worker disposed.");
                Debug.Log("Worker disposed.");
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to dispose worker: {ex.Message}");
                Debug.LogError($"Failed to dispose worker: {ex.Message}");
            }
        }
    }
}
