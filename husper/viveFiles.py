import os
import re
import glob
import pandas as pd

DEBUG = True

def debug_log(msg):
    if DEBUG:
        print(f"[DEBUG] {msg}")

###############################################################################
# 1) Parse controller file to build mapping: {segment_number: (Hand, Button)}
###############################################################################

def parse_controller_input_file(txt_path):
    debug_log(f"Parsing controller file: {txt_path}")
    mapping = {}
    current_input = None
    with open(txt_path, "r") as f:
        for line in f:
            line = line.strip()
            debug_log(f"Line: {line}")
            # Look for a line like "RightHand - TriggerButton pressed"
            match_input = re.match(r"(RightHand|LeftHand)\s*-\s*(\w+Button)\s+pressed", line, re.IGNORECASE)
            if match_input:
                hand = match_input.group(1)
                button = match_input.group(2)
                current_input = (hand, button)
                debug_log(f"  Found press: {current_input}")
                continue
            # Look for a line like "Segment number 12 logged with button press"
            match_seg = re.match(r"Segment\s+number\s+(\d+)\s+logged\s+with\s+button\s+press", line, re.IGNORECASE)
            if match_seg and current_input:
                seg_num = int(match_seg.group(1))
                mapping[seg_num] = current_input
                debug_log(f"  Mapped segment {seg_num} -> {current_input}")
                current_input = None
    debug_log(f"Finished parsing controller file. Total segments mapped: {len(mapping)}")
    return mapping

###############################################################################
# 2) Map (Hand, Button) to QoE value
###############################################################################

def assign_qoe_from_tuple(input_tuple):
    if not input_tuple:
        return None
    hand, button = input_tuple
    h = hand.lower()
    b = button.lower()
    debug_log(f"Assigning QoE for: (hand={hand}, button={button})")
    if h == "righthand":
        if "trigger" in b:
            return 5
        elif "grip" in b:
            return 4
        else:
            return 3
    elif h == "lefthand":
        if "trigger" in b:
            return 2
        elif "grip" in b:
            return 1
        else:
            return 4
    return None

###############################################################################
# 3) Process a single Eye CSV and its matching controller TXT
###############################################################################

def process_eye_and_controller(eye_csv_path, controller_txt_path):
    """
    1. Parse the controller file to get a mapping {segment_number: (Hand, Button)}.
    2. Read the Eye CSV (which must have a "SegmentNumber" column) and add a "QoE" column.
       QoE is assigned only to the first row of each segment.
    3. Save the processed CSV in the "Processed" subfolder.
    4. Return a list of summary rows: one row per segment with keys:
         "SegmentNumber", "ButtonPressed", and "QoE"
    """
    debug_log(f"Processing pair:\n  CSV: {eye_csv_path}\n  TXT: {controller_txt_path}")

    # Parse controller file
    mapping = parse_controller_input_file(controller_txt_path)
    
    # Read the Eye CSV
    df = pd.read_csv(eye_csv_path)
    if "SegmentNumber" not in df.columns:
        raise ValueError(f"File {eye_csv_path} does not have a 'SegmentNumber' column.")
    df["SegmentNumber"] = df["SegmentNumber"].astype(int)
    df["QoE"] = None

    summary_rows = []  # List to accumulate summary info per segment

    # For each segment group, assign QoE (if mapping exists)
    for seg, group in df.groupby("SegmentNumber"):
        if seg in mapping:
            qoe_val = assign_qoe_from_tuple(mapping[seg])
            first_idx = group.index[0]
            df.at[first_idx, "QoE"] = qoe_val
            # For the summary CSV, record the segment, the button pressed (from the mapping), and the QoE.
            # We use the button part from the tuple.
            summary_rows.append({
                "SegmentNumber": seg,
                "ButtonPressed": mapping[seg][1],
                "QoE": qoe_val
            })

    # Prepare output folder and filenames
    folder = os.path.dirname(eye_csv_path)
    base_no_ext = os.path.splitext(os.path.basename(eye_csv_path))[0]
    processed_folder = os.path.join(folder, "Processed")
    os.makedirs(processed_folder, exist_ok=True)
    output_csv_name = f"{base_no_ext}_processed.csv"
    output_csv_path = os.path.join(processed_folder, output_csv_name)

    df.to_csv(output_csv_path, index=False)
    debug_log(f"Saved processed CSV to: {output_csv_path}")
    return summary_rows

###############################################################################
# 4) Generate summary files in a Processed folder
###############################################################################

def generate_summary_files(processed_folder, all_summary_rows):
    """
    Given a list of summary rows (each with keys "SegmentNumber", "ButtonPressed", "QoE")
    from all processed files in this folder, create:
      a) A text file "qoe_summary.txt" listing counts of each QoE value.
      b) A CSV file "controller_summary.csv" with the 3 columns.
    """
    if not all_summary_rows:
        debug_log(f"No summary rows to write in {processed_folder}")
        return

    # Count QoE occurrences
    counts = {}
    for row in all_summary_rows:
        q = row["QoE"]
        counts[q] = counts.get(q, 0) + 1

    summary_lines = []
    summary_lines.append("QoE Counts Summary:")
    for qoe in sorted(counts.keys()):
        summary_lines.append(f"  QoE {qoe}: {counts[qoe]}")
    summary_text = "\n".join(summary_lines)
    summary_txt_path = os.path.join(processed_folder, "qoe_summary.txt")
    with open(summary_txt_path, "w") as f:
        f.write(summary_text)
    debug_log(f"Saved text summary to: {summary_txt_path}")
    print(f"QoE summary written to: {summary_txt_path}")

    # Create controller_summary.csv with columns: SegmentNumber, ButtonPressed, QoE
    summary_df = pd.DataFrame(all_summary_rows)
    summary_csv_path = os.path.join(processed_folder, "controller_summary.csv")
    summary_df.to_csv(summary_csv_path, index=False)
    debug_log(f"Saved CSV summary to: {summary_csv_path}")
    print(f"Controller summary CSV written to: {summary_csv_path}")

###############################################################################
# 5) Main routine: walk subfolders that have "vive", process files, and generate summaries
###############################################################################

def process_vive_folders(root_dir="."):
    """
    Walk all subdirectories under root_dir.
      - Only consider those directories whose path contains "vive" (case-insensitive).
      - In each such directory, look for:
           * A CSV file starting with "Eye" and ending with ".csv"
           * A TXT file starting with "controller" and ending with ".txt"
      - Process each pair (matched in alphabetical order).
      - For each processed folder, generate:
           * A text summary file (qoe_summary.txt) that lists counts of QoE values.
           * A CSV summary file (controller_summary.csv) with three columns.
    """
    for dirpath, dirnames, filenames in os.walk(root_dir):
        # Only process directories with "vive" in their path.
        if "vive" not in dirpath.lower():
            continue

        debug_log(f"Checking folder: {dirpath}")
        # Find files that start with "Eye" and end with ".csv"
        eye_csvs = sorted([f for f in filenames if f.lower().startswith("eye") and f.lower().endswith(".csv")])
        # Find files that start with "controller" and end with ".txt"
        controller_txts = sorted([f for f in filenames if f.lower().startswith("controller") and f.lower().endswith(".txt")])
        
        if not eye_csvs or not controller_txts:
            debug_log(f"No matching Eye CSV or controller TXT in {dirpath}")
            continue

        # In this example, we pair the files in order.
        num_pairs = min(len(eye_csvs), len(controller_txts))
        folder_summary_rows = []
        for i in range(num_pairs):
            eye_csv_path = os.path.join(dirpath, eye_csvs[i])
            controller_txt_path = os.path.join(dirpath, controller_txts[i])
            # Skip if the CSV is already processed
            if "_processed" in eye_csv_path.lower():
                debug_log(f"Skipping already processed file: {eye_csv_path}")
                continue

            try:
                summary_rows = process_eye_and_controller(eye_csv_path, controller_txt_path)
                folder_summary_rows.extend(summary_rows)
            except Exception as ex:
                print(f"ERROR processing pair:\n  {eye_csv_path}\n  {controller_txt_path}\n  {ex}")
        
        # If any pairs were processed in this folder, write the summary files in the Processed subfolder.
        if folder_summary_rows:
            processed_folder = os.path.join(dirpath, "Processed")
            generate_summary_files(processed_folder, folder_summary_rows)

###############################################################################
# 6) Main entry point
###############################################################################

if __name__ == "__main__":
    root_dir = "./"  # Adjust as needed
    process_vive_folders(root_dir)

