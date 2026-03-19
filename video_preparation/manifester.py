import os
import subprocess



def get_video_info(file_path):
    """Get video resolution and duration using ffprobe"""
    cmd = ['ffprobe', '-v', 'error', '-select_streams', 'v:0', '-show_entries', 'stream=width,height,duration', '-of', 'csv=p=0', file_path]
    output = subprocess.check_output(cmd).decode().strip()
    width, height, duration = map(float, output.split(','))
    return int(width), int(height), duration

def get_average_bitrate(log_file):
    """Extract average bitrate from log file"""
    with open(log_file, 'r') as f:
        lines = f.readlines()
    for line in lines:
        if "Average bitrate achieved" in line:
            return int(float(line.split(":")[1].strip().split()[0]))  # Convert to float first, then to int
    return 0

def generate_mpd(outputPath, manifestPath):
  
    period_id = "period0"
    segment_template = """
      <SegmentTemplate timescale="90000" initialization="$RepresentationID$/Header.mp4" media="$RepresentationID$/$Number$.mp4" startNumber="1" duration="359408" presentationTimeOffset="0"/>
    """
    
    representation_template = """
      <Representation id="{id}" bandwidth="{bandwidth}" codecs="avc1.4D401E" width="{width}" height="{height}" frameRate="30" sar="1:1" scanType="progressive"/>
    """
    
    representations = ""
    
    for quality in ['v1', 'v2', 'v3', 'v4', 'v5', 'v6']:
        output_dir = os.path.join(outputPath, quality)
        log_file = os.path.join(output_dir, 'log.txt')
        average_bitrate = get_average_bitrate(log_file)
        
        first_segment = next(f for f in os.listdir(output_dir) if f.endswith('.mp4'))
        segment_path = os.path.join(output_dir, first_segment)
        width, height, _ = get_video_info(segment_path)
        
        representation_id = quality
        representations += representation_template.format(
            id=representation_id,
            bandwidth=average_bitrate * 1000,  # Convert from kbps to bps
            width=width,
            height=height
        )
    
    mpd_template = f"""<?xml version='1.0' encoding='UTF-8'?>
<MPD xmlns="urn:mpeg:dash:schema:mpd:2011" profiles="urn:mpeg:dash:profile:isoff-live:2011" type="static" minBufferTime="PT5.000S" maxSegmentDuration="PT2.005S" availabilityStartTime="2024-07-15T00:00:00Z" mediaPresentationDuration="PT193.680S">
  <Period id="{period_id}">
    <AdaptationSet mimeType="video/mp4" segmentAlignment="true" startWithSAP="1" maxWidth="4096" maxHeight="2048" maxFrameRate="30" par="1:1">
      {segment_template}
      {representations}
    </AdaptationSet>
  </Period>
</MPD>
    """
    
    manifestPath = os.path.join(manifestPath, "Manifest.mpd")
    
    with open(manifestPath, "w") as f:
        f.write(mpd_template)
    print("Manifest.mpd created successfully.")


