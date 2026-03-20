import os
import glob
import re
import pandas as pd

def parse_controller_input_file(file_path):
    """
    Parses the controller inputs file to build a mapping from local segment number to a tuple (Hand, Button).
    The file is expected to include lines such as:
      LeftHand - triggerButton pressed
      Segment number 1 logged with button press.
      
    Returns:
        A dictionary mapping local segment number (int) to a tuple: (Hand, Button).
    """
    mapping = {}
    current_input = None
    with open(file_path, 'r') as f:
        for line in f:
            line = line.strip()
            # Match button press lines (e.g., "LeftHand - triggerButton pressed")
            m = re.match(r'(RightHand|LeftHand)\s*-\s*(\w+Button)\s+pressed', line)
            if m:
                current_input = (m.group(1), m.group(2))
            # Match the segment logging line (e.g., "Segment number 1 logged with button press")
            m_seg = re.search(r'Segment number\s+(\d+)\s+logged with button press', line, re.IGNORECASE)
            if m_seg and current_input:
                seg_num = int(m_seg.group(1))
                mapping[seg_num] = current_input
                current_input = None
    return mapping

def assign_qoe_from_controller_input(input_tuple):
    """
    Given a tuple (Hand, Button), assign a QoE value based on the following rules:
    
      - RightHand:
          - triggerButton -> QoE = 1
          - gripButton -> QoE = 2
          - any other  -> QoE = 3
      - LeftHand:
          - triggerButton -> QoE = 4
          - gripButton -> QoE = 5
          - any other  -> QoE = 4
    """
    if not input_tuple:
        return None
    hand, button = input_tuple
    if hand == "RightHand":
        if button == "triggerButton":
            return 1
        elif button == "gripButton":
            return 2
        else:
            return 3
    elif hand == "LeftHand":
        if button == "triggerButton":
            return 4
        elif button == "gripButton":
            return 5
        else:
            return 4
    else:
        return None

def process_tracking_file(tracking_csv_path, output_csv_path):
    """
    Processes the tracking log CSV by appending a QoE column based on the corresponding
    controller inputs file. Since the tracking file uses global (continuous) segment numbers and
    the controller file uses local segment numbers (starting from 1), an offset is computed.
    
    QoE is assigned only to the first row of each segment (i.e. one entry per button press).
    """
    # Extract the file identifier (e.g. 400, 800, etc.) from the tracking file name.
    basename = os.path.basename(tracking_csv_path)
    match = re.search(r'^tracking_log_(\d{1,4})\.csv$', basename)
    if not match:
        raise ValueError(f"Tracking log file name does not match expected pattern: {basename}")
    file_id = match.group(1)
    
    # Build the corresponding controller inputs file name (expected to be in the same folder as tracking file).
    controller_file = os.path.join(os.path.dirname(tracking_csv_path), f"controller_inputs{file_id}.txt")
    if not os.path.exists(controller_file):
        raise FileNotFoundError(f"Controller inputs file not found: {controller_file}")
    
    # Parse controller inputs.
    controller_mapping = parse_controller_input_file(controller_file)
    print(f"Controller mapping for file {controller_file}: {controller_mapping}")
    
    # Read the tracking CSV.
    df_tracking = pd.read_csv(tracking_csv_path)
    
    # Calculate the offset using the minimum segment number from the tracking file.
    # For example, if the first segment is 400 then offset = 400 - 1 = 399.
    min_seg = df_tracking['SegmentNumber'].min()
    offset = min_seg - 1
    
    # Add a new column 'QoE' and initialize with None.
    df_tracking['QoE'] = None
    
    # Group rows by segment. For each segment, assign the QoE only to the first row.
    for seg, group in df_tracking.groupby('SegmentNumber'):
        # Convert global segment number to local segment number.
        local_seg = seg - offset
        if local_seg in controller_mapping:
            qoe_value = assign_qoe_from_controller_input(controller_mapping[local_seg])
            first_index = group.index[0]
            df_tracking.at[first_index, 'QoE'] = qoe_value
        else:
            print(f"No mapping found for global segment {seg} (local segment {local_seg}) in file {tracking_csv_path}")
    
    # Save the processed CSV.
    df_tracking.to_csv(output_csv_path, index=False)
    print(f"Processed file saved to: {output_csv_path}")

def generate_qoe_summary_in_folder(processed_folder):
    """
    Scans the given processed_folder for processed CSV files and generates a summary
    of QoE counts based on button presses (one count per segment). The summary is saved
    as 'qoe_summary.txt' within the same processed_folder.
    """
    summary_lines = []
    overall_counts = {}
    
    processed_files = glob.glob(os.path.join(processed_folder, "*_processed.csv"))
    if not processed_files:
        print(f"No processed files found in {processed_folder}")
        return

    for file in processed_files:
        df = pd.read_csv(file)
        file_name = os.path.basename(file)
        summary_lines.append(f"File: {file_name}")
        # Only count the rows where QoE is assigned (one per segment)
        file_counts = df['QoE'].dropna().astype(int).value_counts().sort_index()
        for qoe, count in file_counts.items():
            summary_lines.append(f"  QoE {qoe}: {count}")
            overall_counts[qoe] = overall_counts.get(qoe, 0) + count
        summary_lines.append("")
    
    summary_lines.append("Overall Summary:")
    for qoe in sorted(overall_counts.keys()):
        summary_lines.append(f"  QoE {qoe}: {overall_counts[qoe]}")
    
    summary_file = os.path.join(processed_folder, "qoe_summary.txt")
    with open(summary_file, "w") as f:
        f.write("\n".join(summary_lines))
    print(f"QoE summary written to: {summary_file}")

def remove_low_count_segments(df, min_rows=100):
    """
    Filters the DataFrame to only include segments that have at least `min_rows` rows.
    """
    segment_counts = df['SegmentNumber'].value_counts()
    valid_segments = segment_counts[segment_counts >= min_rows].index
    return df[df['SegmentNumber'].isin(valid_segments)]

def remove_low_variability_columns(df, threshold=0.98):
    """
    Drops any column where a single value accounts for at least `threshold` proportion of rows.
    Returns the modified DataFrame and a list of dropped columns.
    """
    columns_to_drop = []
    for col in df.columns:
        if col == 'QoE':
            continue
        most_common_prop = df[col].value_counts(normalize=True, dropna=False).max()
        if most_common_prop >= threshold:
            columns_to_drop.append(col)
    return df.drop(columns=columns_to_drop), columns_to_drop

def process_clean_file(input_csv_path, output_csv_path):
    """
    Reads a processed CSV file, removes segments with less than 100 rows, and drops low variability
    columns (with one value dominating at 98% or more). Saves the cleaned DataFrame to a new CSV.
    """
    df = pd.read_csv(input_csv_path)
    
    df = remove_low_count_segments(df, min_rows=100)
    df, dropped_columns = remove_low_variability_columns(df, threshold=0.98)
    if dropped_columns:
        print(f"In file '{os.path.basename(input_csv_path)}', dropped columns: {dropped_columns}")
    else:
        print(f"In file '{os.path.basename(input_csv_path)}', no columns dropped for low variability.")
    
    df.to_csv(output_csv_path, index=False)
    print(f"Cleaned file saved to: {output_csv_path}\n")

def process_tracking_files_recursively(root_dir="."):
    """
    Recursively searches for tracking CSV files (tracking_log_*.csv) in the root_dir.
    Files that have '_processed' in their name are skipped.
    For each found file, it creates a 'Processed' subfolder in the same directory and saves
    the processed file there.
    """
    pattern = os.path.join(root_dir, "**", "tracking_log_*.csv")
    for tracking_csv_path in glob.glob(pattern, recursive=True):
        # Skip files that are already processed (e.g. containing "_processed" in the name)
        if "_processed" in os.path.basename(tracking_csv_path):
            continue
        folder = os.path.dirname(tracking_csv_path)
        processed_folder = os.path.join(folder, "Processed")
        if not os.path.exists(processed_folder):
            os.mkdir(processed_folder)
        output_csv_name = os.path.basename(tracking_csv_path).replace(".csv", "_processed.csv")
        output_csv_path = os.path.join(processed_folder, output_csv_name)
        try:
            process_tracking_file(tracking_csv_path, output_csv_path)
        except Exception as e:
            print(f"Error processing {tracking_csv_path}: {e}")
    
    # After processing all files in a folder, generate a summary in that folder's Processed subfolder.
    # Find all Processed folders under root_dir.
    processed_folders = set(os.path.dirname(f) for f in glob.glob(os.path.join(root_dir, "**", "*_processed.csv"), recursive=True))
    for folder in processed_folders:
        generate_qoe_summary_in_folder(folder)

def process_clean_files_recursively(root_dir="."):
    
    CLEANED_ROOT = os.path.join(root_dir, "All_Cleaned_Data")
    os.makedirs(CLEANED_ROOT, exist_ok=True)

    pattern = os.path.join(root_dir, "**", "*_processed.csv")
    for processed_csv_path in glob.glob(pattern, recursive=True):
        # Compute relative path from root_dir
        rel_path = os.path.relpath(processed_csv_path, root_dir)
        # Remove the "_processed.csv" suffix and append "_cleaned.csv"
        rel_cleaned = rel_path.replace("_processed.csv", "_cleaned.csv")
        # Determine the target directory & ensure it exists
        target_dir = os.path.join(CLEANED_ROOT, os.path.dirname(rel_cleaned))
        os.makedirs(target_dir, exist_ok=True)
        # Final output path
        output_csv_path = os.path.join(target_dir, os.path.basename(rel_cleaned))

        try:
            process_clean_file(processed_csv_path, output_csv_path)
        except Exception as e:
            print(f"Error cleaning {processed_csv_path}: {e}")


if __name__ == "__main__":
    # Adjust the root_dir as needed; here, it's set to "../../" as an example.
    root_dir = "../../"
    
    # First, process tracking files recursively.
    process_tracking_files_recursively(root_dir)
    
    # Then, process the cleaned files recursively.
    process_clean_files_recursively(root_dir)

