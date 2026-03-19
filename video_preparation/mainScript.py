import os
from segmenter import convert_video_to_keyframes, split_video_exact_segments
from quality_segmenter import main as quality_segmenter_main
from manifester import generate_mpd

def convert_videosToKeyFrames_and_segment():
    i=0
    print("Count of videos in videos : " + str(len(os.listdir("Inputvideos"))))
    for video in os.listdir("Inputvideos"):
        print(video)
    for video in os.listdir("Inputvideos"):
        if video.endswith(".mp4"):
            video_path = os.path.join("Inputvideos", video)
            keyframe_video_path = os.path.join("Inputvideos", video.split(".")[0] + "frames.mp4")
            segments_output_dir = os.path.join(f"video{i}")
            if(not (os.path.exists(segments_output_dir))):
                print(f"Creating directory '{segments_output_dir}'")
                os.makedirs(segments_output_dir)
            convert_video_to_keyframes(video_path, keyframe_video_path)
            split_video_exact_segments(keyframe_video_path, segments_output_dir)
            i+=1
            # Process each segment and convert to 3 different qualities
            quality_segmenter_main(segments_output_dir, segments_output_dir)
            # Generate the manifest file
            generate_mpd(segments_output_dir, segments_output_dir)


convert_videosToKeyFrames_and_segment()
            
            
            
    
    