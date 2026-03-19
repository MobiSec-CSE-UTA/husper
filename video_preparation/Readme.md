## Video Preparation Pipeline

**Video Preparation** segments raw MP4s into DASH-compatible, multi-bitrate chunks for VR playback.

### Implementation Overview
- **Input**: Drop your `.mp4` files into `video_preparation/Inputvideos/`.  
- **Run**:  
  ```bash
  cd video_preparation
  python phase4.py
