# Husper
Husper is a sensing-based personalized adaptive bitrate (ABR) framework for video streaming that integrates real-time user behavior signals with network metrics to optimize user quality of experience (QoE). This repository provides an end-to-end pipeline covering raw video preparation, data processing, model training, and Unity-based system integration.

## Implementation Overview
- **Video Preparation**: Segment raw videos into DASH-compatible chunks at multiple bitrates using `video_preparation/phase4.py`, `segmenter.py`, and `quality_segmenter.py`.   
- **Model Training**: In `husper/`, define reward, CQL training scripts, and Jupyter notebooks for reproducible experiments.  
- **Playback Integration**: Unity project in `video_player[unity]/` that queries ONNX-exported policies at runtime to select bitrates.

## Folder Structure
- **video_player_unity/**  
  Unity scenes, C# wrappers, and sensor-capture modules for real-time VR playback under Husper.  
- **husper/**  
  Core algorithm code: biosignal feature extraction, reward design, offline CQL training, and policy export.  
- **video_preparation/**  
  Scripts to generate key-frame videos, segment into fixed‐length chunks, transcode to multiple quality tiers, and produce DASH manifests.  
