import os
from segmenter import convert_video_to_keyframes, split_video_exact_segments
from quality_segmenter import main as quality_segmenter_main
from manifester import generate_mpd

def convert_videos_to_keyframes_and_segment():
    segment_lengths = [0.4]
    
    input_videos_path = "Inputvideos"
    output_base_path = "SegmentedVideos"
    
    # Print the count of input videos
    print("Count of videos in Inputvideos: " + str(len(os.listdir(input_videos_path))))
    video_list = os.listdir(input_videos_path)
    for video in video_list:
        print("checking if video is keyframes", video)
        print("checking if video has a keyframes version", ( os.path.exists(os.path.join(input_videos_path, video.replace(".mp4", "_keyframes.mp4")))))
        if video.endswith("_keyframes.mp4"):
            continue
        elif os.path.exists(os.path.join(input_videos_path, video.replace(".mp4", "_keyframes.mp4"))):
            
            continue
        processed_video = video.replace(".mp4", "_keyframes.mp4")
        convert_video_to_keyframes(os.path.join(input_videos_path, video), os.path.join(input_videos_path, processed_video))
        
    for video in os.listdir(input_videos_path):
        if video.endswith("_keyframes.mp4"):
            video_path = os.path.join(input_videos_path, video)
            print(f"Processing video: {video}")

            # Loop through each segment length to create different versions
            for segment_length in segment_lengths:
                version_dir_name = f"{video}_segments_{segment_length}s"
                segments_output_dir = os.path.join(output_base_path, version_dir_name)

                # Create the output directory if it doesn't exist
                if not os.path.exists(segments_output_dir):
                    print(f"Creating directory '{segments_output_dir}'")
                    os.makedirs(segments_output_dir)
                if len(os.listdir(segments_output_dir)) > 0:
                    print(f"Directory '{segments_output_dir}' is not empty. Skipping processing.")
                    continue
                
                # Split the video into segments of the specified length
                print(f"Splitting video into segments of length {segment_length} seconds")
                split_video_exact_segments(video_path, segments_output_dir, segment_length=segment_length, max_segments=150)
                
                # Process each segment and convert to different quality levels
                print(f"Processing segments into different quality levels for version with segment length {segment_length} seconds")
                quality_segmenter_main(segments_output_dir, segments_output_dir)
                
                # Generate the manifest file for DASH streaming
                print(f"Generating MPEG-DASH manifest for version with segment length {segment_length} seconds")
                generate_mpd(segments_output_dir, segments_output_dir)

convert_videos_to_keyframes_and_segment()
