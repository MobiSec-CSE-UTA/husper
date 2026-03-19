using Unity.Barracuda;
using UnityEngine;
using System.IO;

public class PensieveModel : MonoBehaviour
{
    private Model actorModel;
    private IWorker actorWorker;
    private string logFilePath;

    void Awake()
    {
        // // Define the log file path
        // logFilePath = Path.Combine(Application.persistentDataPath, "PensieveLogs.txt");

        // // Create or clear the log file
        // File.WriteAllText(logFilePath, "Pensieve Model Logs\n=====================\n");

        // string modelPath = Path.Combine(Application.streamingAssetsPath, "SavedModels/pensieve_actor_simplified.onnx");

        // Log($"Loading model from: {modelPath}");

        // if (!File.Exists(modelPath))
        // {
        //     LogError($"Pensieve model not found at path: {modelPath}");
        //     return;
        // }

        // // Load the ONNX model from the StreamingAssets folder
        // actorModel = ModelLoader.LoadFromStreamingAssets(modelPath);

        // if (actorModel == null)
        // {
        //     LogError("Failed to load the Pensieve model. Ensure the ONNX model is valid.");
        //     return;
        // }

        // // Create a worker to execute the model
        // actorWorker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, actorModel);

        // Log("Pensieve model loaded and ready for predictions.");
    }

    public float[] Predict(float[,,] stateInput)
    {
        Log("Starting prediction...");

        // Flatten the float[,,] array into a 1D array
        float[] flattenedInput = FlattenArray(stateInput);

        // Log the input data
        Log($"State input (flattened): {string.Join(", ", flattenedInput)}");

        // Create input tensor with explicit dimensions
        Tensor inputTensor = new Tensor(new TensorShape(1, stateInput.GetLength(1), stateInput.GetLength(2), 1), flattenedInput);

        // Execute the model
        actorWorker.Execute(inputTensor);

        // Get the output tensor
        Tensor outputTensor = actorWorker.PeekOutput();

        // Convert output to array
        float[] actionProbabilities = outputTensor.ToReadOnlyArray();

        // Log the output probabilities
        Log($"debug 19 -->>>>>>>>> Predicted action probabilities: {string.Join(", ", actionProbabilities)}");
        Debug.Log($"debug 19 -->>>>>>>>> Predicted action probabilities: {string.Join(", ", actionProbabilities)}");
        // Cleanup tensors
        inputTensor.Dispose();
        outputTensor.Dispose();

        return actionProbabilities;
    }

    public int SelectAction(float[] actionProbabilities)
    {
        Log($"Selecting action from probabilities: {string.Join(", ", actionProbabilities)}");

        int selectedAction = 0;
        float maxProbability = actionProbabilities[0];

        for (int i = 1; i < actionProbabilities.Length; i++)
        {
            if (actionProbabilities[i] > maxProbability)
            {
                maxProbability = actionProbabilities[i];
                selectedAction = i;
            }
        }

        Log($"Selected Action: {selectedAction}, Probability: {maxProbability}");
        return selectedAction;
    }

    private float[] FlattenArray(float[,,] array)
    {
        int size = array.Length;
        float[] flatArray = new float[size];
        int index = 0;

        for (int i = 0; i < array.GetLength(0); i++)
        {
            for (int j = 0; j < array.GetLength(1); j++)
            {
                for (int k = 0; k < array.GetLength(2); k++)
                {
                    flatArray[index++] = array[i, j, k];
                }
            }
        }

        return flatArray;
    }

    private void OnDestroy()
    {
        // Dispose of the worker when the object is destroyed
        actorWorker.Dispose();
        Log("Pensieve model worker disposed.");
    }

    // Helper method to log messages
    private void Log(string message)
    {
        Debug.Log(message);
        File.AppendAllText(logFilePath, $"{System.DateTime.Now}: {message}\n");
    }

    // Helper method to log errors
    private void LogError(string message)
    {
        Debug.LogError(message);
        File.AppendAllText(logFilePath, $"{System.DateTime.Now} [ERROR]: {message}\n");
    }
}
