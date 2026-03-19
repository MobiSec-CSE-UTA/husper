import os
import subprocess
import shutil

# Define the input directory and the output directories
relativeInputPath = './'
outputPath = {
    'v1': '/v1',
    'v2': '/v2',
    'v3': '/v3',
    'v4': '/v4',
    'v5': '/v5',
    'v6': '/v6'
}

# Updated target bitrates in kbps (converted from Mbps)
bitrates = {
    'v1': 2000,   # 2 Mbps
    'v2': 6000,   # 6 Mbps
    'v3': 9500,   # 9.5 Mbps
    'v4': 15000,  # 15 Mbps
    'v5': 30000,  # 30 Mbps
    'v6': 85000   # 85 Mbps
}

# Updated target resolutions (vertical height in pixels)
resolutions = {
    'v1': 360,
    'v2': 480,
    'v3': 720,
    'v4': 1080,
    'v5': 1440,
    'v6': 2160
}

# Cache for storing the learned target bitrates for each version
learned_bitrates = {}

def ensure_even(value):
    """Ensure the value is even."""
    return value if value % 2 == 0 else value - 1

def get_video_info(file_path):
    """Get video resolution using ffprobe."""
    cmd = ['ffprobe', '-v', 'error', '-select_streams', 'v:0',
           '-show_entries', 'stream=width,height', '-of', 'csv=p=0', file_path]
    output = subprocess.check_output(cmd).decode().strip()
    width, height = map(int, output.split(','))
    return width, height

def get_bitrate(file_path):
    """Get the bitrate of a video segment using ffprobe."""
    cmd = ['ffprobe', '-v', 'error', '-select_streams', 'v:0',
           '-show_entries', 'stream=bit_rate', '-of', 'csv=p=0', file_path]
    output = subprocess.check_output(cmd).decode().strip()
    return int(output) // 1000  # Convert from bps to kbps

def process_segment(input_path, output_path, target_bitrate, target_resolution):
    """Process a single segment with fixed bitrate and target resolution (vertical height)."""
    orig_width, orig_height = get_video_info(input_path)
    # Determine scaling factor based on target resolution (do not upscale if original is smaller)
    if orig_height > target_resolution:
        scale_factor = target_resolution / orig_height
    else:
        scale_factor = 1
    new_width = ensure_even(int(orig_width * scale_factor))
    new_height = ensure_even(int(orig_height * scale_factor))
    target_bitrate_kbps = f"{target_bitrate}k"

    cmd = [
        'ffmpeg', '-i', input_path, '-vf', f'scale={new_width}:{new_height}',
        '-b:v', target_bitrate_kbps, '-minrate', target_bitrate_kbps,
        '-maxrate', target_bitrate_kbps, '-bufsize', target_bitrate_kbps,
        '-y', output_path
    ]
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)

def dynamic_adjust_bitrate(current_target, actual_bitrate, desired_bitrate):
    """Adjust the target bitrate based on the ratio between the actual and desired bitrates."""
    if actual_bitrate == 0:
        raise ValueError("Actual bitrate cannot be zero.")

    ratio = desired_bitrate / actual_bitrate
    new_target = current_target * ratio
    print(ratio)
    new_target = max(new_target, 100)  # Ensure bitrate doesn't go below 100 kbps
    new_target = min(new_target, current_target * 1.5)  # Avoid increasing by more than 50% in one step
    new_target = max(new_target, current_target * 0.5)  # Avoid decreasing by more than 50% in one step
    return int(new_target)

def log_info(output_dir, bitrates_list, target_bitrate):
    """Log information about the processed segments."""
    log_file = os.path.join(output_dir, 'log.txt')
    avg_bitrate = sum(bitrate for _, bitrate in bitrates_list) // len(bitrates_list)
    
    with open(log_file, 'w') as f:
        f.write(f"Target Bitrate: {target_bitrate} kbps\n")
        f.write(f"Average bitrate achieved: {avg_bitrate} kbps\n")
        f.write("Processed segments:\n")
        for segment, bitrate in bitrates_list:
            f.write(f"{segment}: {bitrate} kbps\n")

def main(relativePath, outputPathParam, max_attempts=10, tolerance=130):
    outputPath = {
        'v1': '/v1',
        'v2': '/v2',
        'v3': '/v3',
        'v4': '/v4',
        'v5': '/v5',
        'v6': '/v6'
    }
    relativeInputPath = relativePath
    print(f"relativeInputPath: {relativeInputPath}")
    print(f"outputPathParam: {outputPathParam}")
    for version, output_dir in outputPath.items():
        outputPath[version] = os.path.join(outputPathParam, output_dir.strip('/'))
        print(f"outputPath[{version}]: {outputPath[version]}")

    if not os.path.exists(relativeInputPath):
        print(f"Input directory '{relativeInputPath}' does not exist.")
        return

    for version, output_dir in outputPath.items():
        if os.path.exists(output_dir):
            shutil.rmtree(output_dir)
        print(f"creating directory '{output_dir}'")
        os.makedirs(output_dir)

        segments = [f for f in os.listdir(relativeInputPath) if f.endswith('.mp4')]
        if not segments:
            continue

        bitrates_for_logging = []
        target_bitrate = learned_bitrates.get(version, bitrates[version])
        target_resolution = resolutions[version]

        # Process the first 3 segments for adjustment
        temp_dir = os.path.join(output_dir, "temp")
        os.makedirs(temp_dir, exist_ok=True)
        initial_segments = segments[:3]

        for attempt in range(max_attempts):
            actual_bitrates = []
            for segment in initial_segments:
                input_path = os.path.join(relativeInputPath, segment)
                output_path_temp = os.path.join(temp_dir, segment)
                process_segment(input_path, output_path_temp, target_bitrate, target_resolution)
                actual_bitrates.append(get_bitrate(output_path_temp))

            avg_bitrate = sum(actual_bitrates) / len(actual_bitrates)
            tolerance_val = max(500, bitrates[version] * 0.15)
            print(f"Attempt {attempt + 1}: Target={bitrates[version]} kbps, Average={avg_bitrate} kbps, Tolerance={tolerance_val} kbps")

            if abs(avg_bitrate - bitrates[version]) <= tolerance_val:
                print(f"Acceptable bitrate achieved: {avg_bitrate} kbps")
                learned_bitrates[version] = target_bitrate
                break

            target_bitrate = dynamic_adjust_bitrate(target_bitrate, avg_bitrate, bitrates[version])
        else:
            print(f"Failed to converge on acceptable bitrate for {version}. Compromiosing.")
            print(f"Compromised bitrate achieved: {avg_bitrate} kbps")
            learned_bitrates[version] = target_bitrate
            break
            

        # Process all segments with the final acceptable bitrate
        for segment in segments:
            input_path = os.path.join(relativeInputPath, segment)
            output_path_final = os.path.join(output_dir, segment)
            print(f"output path: {output_path_final}")    
            process_segment(input_path, output_path_final, target_bitrate, target_resolution)
            actual_bitrate = get_bitrate(output_path_final)
            bitrates_for_logging.append((segment, actual_bitrate))

        log_info(output_dir, bitrates_for_logging, target_bitrate)
        shutil.rmtree(temp_dir)

# Example usage:
# main('./input', './output')
