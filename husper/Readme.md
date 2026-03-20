
```markdown
## Husper Model Training & Evaluation

**Model Training** ingests sensor/network CSVs and learns a Conservative Q-Learning policy for personalized ABR.


### DQN Training Guide

1. **Prepare Processed QoE Data**  
   - Copy your processed QoE assessment CSVs into `Data3/`.

2. **Process Raw Biosignals (if needed)**  
   - Place raw biosignal files **and** their controller logs together in a folder located **two levels above** `viveFiles.py`.  
   - Run:
     ```bash
     cd Data1
     python3 viveFiles.py
     ```
   - This generates cleaned CSVs under `Data1/Processed/`.

3. **Assemble QoE Training Sets**  
   - Create (if not existing) `QoE_train/Net_train/` and `QoE_train/Eye_train/`.  
   - Move each of your **four** processed datasets into the respective folders:
     ```
     QoE_train/
     ├── Net_train/    # processed network + QoE labels
     └── Eye_train/    # processed biosignal features
     ```

4. **Run Processing Notebook**  
   - Open `husper/process.ipynb`.  
   - Execute all cells to transform your QoE and biosignal data into model‑ready arrays under `Data3/`.

5. **Train & Export CQL Model**  
   - Open `husper/CQL.ipynb`.  
   - Configure any hyperparameters as needed.  
   - Execute all cells to train the policy and export ONNX weights into `husper/models/`.

6. **Unity Next Steps**  
   - Refer to the **Unity Video Player** README for importing the ONNX model and running the VR playback demo.
