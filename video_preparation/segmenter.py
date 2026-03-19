import subprocess
import os

def convert_video_to_keyframes(video_path, output_path):
    command = [
        "ffmpeg",
        "-i", video_path,
        "-vf", "fps=30", 
        "-g", "1",
        "-keyint_min", "1", 
        "-c:v", "libx264",
        "-crf", "18", 
        "-preset", "fast",
        "-an",  
        
        output_path
    ]
    subprocess.run(command, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)


def split_video_exact_segments(video_path, output_dir, segment_length=4, max_segments=15):
    if not os.path.exists(output_dir):
        os.makedirs(output_dir)
    
    temp_output_pattern = os.path.join(output_dir, "temp_%d.mp4")
    
    # Use FFmpeg to split the video into exact segments
    command = [
        "ffmpeg",
        "-i", video_path,
        "-c", "copy",
        "-f", "segment",
        "-segment_time", str(segment_length),
        "-reset_timestamps", "1",
        temp_output_pattern
    ]
    subprocess.run(command, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
    
    for filename in sorted(os.listdir(output_dir)):
        if filename.startswith("temp_") and filename.endswith(".mp4"):
            original_index = int(filename.split('_')[1].split('.')[0])
            if original_index >= max_segments:
                os.remove(os.path.join(output_dir, filename))
            else:
                old_file = os.path.join(output_dir, filename)
                new_file = os.path.join(output_dir, "{}.mp4".format(original_index + 1))
                os.rename(old_file, new_file)
                print(f"Renamed {old_file} to {new_file}")
