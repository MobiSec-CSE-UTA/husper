import os
import shutil
import math
import random

# Base folders.
SOURCE_BASE = "400ms_6Q_HQ_last5"
TARGET_BASE = "400ms_15set_3Q_HQ"

MIN_COUNT = 10 

quality_candidates = {
    "good": [(6, 85000), (5, 30000), (4, 15000)],
    "moderate": [(6, 85000), (5, 30000), (4, 15000), (3, 9500), (2, 6000), (1, 2000)],
    "low": [(1, 2000), (2, 6000), (4, 15000)]
}


quality_ratios = {
    "good": [70, 20, 10],
    "moderate": [10, 20, 25, 20, 20, 10],
    "low": [35, 35, 30]
}


quality_targets = {
    "good": round(3850 * 19.77),    
    "moderate": round(2500 * 19.77), 
    "low": round(900 * 19.77)         
}

def create_assignment_exploratory(num_segments, target, candidates, desired_ratios, tolerance_pct=0.05, tolerance_fixed=2500):

    total_ratio = sum(desired_ratios)
    normalized = [r / total_ratio for r in desired_ratios]
    
    num_candidates = len(candidates)
    base_counts = [MIN_COUNT] * num_candidates
    remaining = num_segments - sum(base_counts)
    
    ideal_extras = [n * remaining for n in normalized]
    extra_counts = [int(round(x)) for x in ideal_extras]
    
    diff = remaining - sum(extra_counts)
    while diff != 0:
        if diff > 0:
            remainders = [ideal_extras[i] - extra_counts[i] for i in range(num_candidates)]
            idx = max(range(num_candidates), key=lambda i: remainders[i])
            extra_counts[idx] += 1
            diff -= 1
        else:
            remainders = [extra_counts[i] - ideal_extras[i] for i in range(num_candidates)]
            idx = max(range(num_candidates), key=lambda i: remainders[i])
            if extra_counts[idx] > 0:
                extra_counts[idx] -= 1
                diff += 1
            else:
                break
                
    counts = [base_counts[i] + extra_counts[i] for i in range(num_candidates)]
    
    def calc_avg(cnts):
        return sum(cnt * bitrate for cnt, (_, bitrate) in zip(cnts, candidates)) / num_segments

    best_counts = counts.copy()
    best_avg = calc_avg(best_counts)
    best_error = abs(best_avg - target)
    tol = min(target * tolerance_pct, tolerance_fixed)
    
    print("Initial candidate counts:", best_counts, "with avg =", best_avg)
    
    improved = True
    iterations = 0
    max_iter = 10000
    while improved and iterations < max_iter:
        improved = False
        for i in range(num_candidates):
            for j in range(num_candidates):
                if i == j:
                    continue
                if best_counts[i] > MIN_COUNT:
                    new_counts = best_counts.copy()
                    new_counts[i] -= 1
                    new_counts[j] += 1
                    new_avg = calc_avg(new_counts)
                    new_error = abs(new_avg - target)
                    if new_error < best_error:
                        best_counts = new_counts.copy()
                        best_avg = new_avg
                        best_error = new_error
                        improved = True
                        if best_error <= tol:
                            assignment = []
                            for count, candidate in zip(best_counts, candidates):
                                assignment.extend([candidate] * count)
                            random.shuffle(assignment)
                            print("Final candidate counts:", best_counts, "with avg =", best_avg)
                            return assignment
        iterations += 1

    print("Final candidate counts after exploration:", best_counts, "with avg =", best_avg)
    assignment = []
    for count, candidate in zip(best_counts, candidates):
        assignment.extend([candidate] * count)
    random.shuffle(assignment)
    return assignment

def process_video(video_folder_name):
    source_video_path = os.path.join(SOURCE_BASE, video_folder_name)
    version1_path = os.path.join(source_video_path, "v1")
    
    # List segments based on the files in v1.
    segment_files = sorted([f for f in os.listdir(version1_path) if f.endswith(".mp4")],
                            key=lambda x: int(os.path.splitext(x)[0]))
    num_segments = len(segment_files)

    target_video_path = os.path.join(TARGET_BASE, video_folder_name)
    os.makedirs(target_video_path, exist_ok=True)
    for quality in ["good", "moderate", "low"]:
        os.makedirs(os.path.join(target_video_path, quality), exist_ok=True)

    quality_assignments = {}
    quality_avgs = {}
    for quality in ["good", "moderate", "low"]:
        target = quality_targets[quality]
        candidates = quality_candidates[quality]
        ratios = quality_ratios[quality]
        print(f"Processing {video_folder_name} quality {quality} (target avg {target})...")
        assignment = create_assignment_exploratory(num_segments, target, candidates, ratios)
        quality_assignments[quality] = assignment
        avg = sum(bitrate for (_, bitrate) in assignment) / num_segments
        quality_avgs[quality] = avg
        tol = min(target * 0.05, 2500)
        if abs(avg - target) > tol:
            print(f"Warning: {video_folder_name} quality {quality} average {avg:.1f} deviates from target {target} by more than {tol} kbps.")
        else:
            print(f"{video_folder_name} quality {quality} assignment OK (avg = {avg:.1f}).")

    # Copy each segment from the assigned version folder.
    for idx in range(num_segments):
        seg_filename = f"{idx+1}.mp4"
        for quality in ["good", "moderate", "low"]:
            version, bitrate = quality_assignments[quality][idx]
            src_seg_path = os.path.join(source_video_path, f"v{version}", seg_filename)
            tgt_seg_path = os.path.join(target_video_path, quality, seg_filename)
            if not os.path.exists(src_seg_path):
                print(f"Warning: Source segment {src_seg_path} does not exist!")
            else:
                shutil.copy(src_seg_path, tgt_seg_path)

    # Copy manifest files.
    src_manifest = os.path.join(source_video_path, "Manifest.mpd")
    if os.path.exists(src_manifest):
        shutil.copy(src_manifest, target_video_path)
    else:
        print(f"Warning: Manifest.mpd not found in {source_video_path}")
    root_manifest = os.path.join(".", "Manifest.mpd")
    if os.path.exists(root_manifest):
        shutil.copy(root_manifest, target_video_path)
    else:
        print("Warning: Root Manifest.mpd not found.")

    video_id = int(video_folder_name.replace("video", ""))
    quality_log_ids = {"good": 0, "moderate": 1, "low": 2}
    for quality in ["good", "moderate", "low"]:
        log_filename = f"bitrate_log_{video_id * 100 + quality_log_ids[quality]}.txt"
        log_filepath = os.path.join(target_video_path, log_filename)
        with open(log_filepath, "w") as log_file:
            for idx, (version, bitrate) in enumerate(quality_assignments[quality]):
                log_file.write(f"Segment {idx+1} ({idx+1}.mp4): {bitrate} kbps (v{version})\n")

    # Write average bitrate file.
    avg_filepath = os.path.join(target_video_path, "avg")
    with open(avg_filepath, "w") as avg_file:
        avg_file.write(f"Good: {quality_avgs['good']:.1f} kbps\n")
        avg_file.write(f"Moderate: {quality_avgs['moderate']:.1f} kbps\n")
        avg_file.write(f"Low: {quality_avgs['low']:.1f} kbps\n")

    print(f"{video_folder_name} processing complete:")
    for quality in ["good", "moderate", "low"]:
        print(f"  {quality}: target {quality_targets[quality]}, actual average {quality_avgs[quality]:.1f}")

def main():
    os.makedirs(TARGET_BASE, exist_ok=True)
    # Process video folders: video0 to video39.
    j=15
    for i in range(40):
        video_folder = f"video{j}"
        process_video(video_folder)
        j+=1
    print("Assignment, copying, and logging complete.")

if __name__ == "__main__":
    main()

